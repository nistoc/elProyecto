using System.Text.Encodings.Web;
using System.Text.Json;
using TranslationImprover.Features.Refine.Application;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// The judge of the words in doubt, a chat model. It gets the words, each with its line, and answers for each,
/// as one JSON object: was it said in Russian ("ru"), in Spanish ("es"), or is it made of both ("mix"). It is
/// not asked for a spelling and none is read from its answer.
/// <para>The prompt has the model say first whether Russian has the word as it is written. That is a step on
/// the way to the answer, and the code does not read it. Asked only "in which language was it said", a small
/// model called Spanish every Russian word that has a Spanish word like it and stands near Spanish speech (33
/// lesson dialogues, gpt-4o-mini: of 106 words called Spanish about 45 were Russian — «роль», «без», «её»).
/// Holding a word back whenever the model says that Russian has it was measured too (gpt-4.1-mini, the same
/// dialogues): it stopped four respellings, all of them right — a Spanish word that Russian has borrowed — and
/// not one wrong.</para>
/// <para>The answer repeats the word: an answer whose word is another one belongs to another item, and is
/// dropped.</para>
/// </summary>
public sealed class OpenAISpellingJudge : ISpellingJudge
{
    public const string Prompt =
        "You get words from the transcript of a Spanish lesson given in Russian. The speakers switch between Russian and Spanish all the time. "
        + "The transcriber wrote some Spanish words in Cyrillic letters by ear. For every item decide what the marked word is.\n"
        + "\n"
        + "An item has \"id\", \"word\" (the word as it is written) and \"text\" (its line, the word marked [[like this]]).\n"
        + "\n"
        + "Answer two things about the word, in this order.\n"
        + "\n"
        + "\"russian\": true when the word, as it is written, is a word of the Russian language. Any form of any word that a Russian dictionary has counts: "
        + "a noun in any case (момент, системы, результату, вариантов), a verb form (смотри, понимаешь), a pronoun, a preposition or a particle (её, без, вас), an interjection (ага), "
        + "a loan word that Russian has taken in (интернет, офис, кофе, финал), a first name, a surname, the name of a city, a country or a company. false when Russian has no such word.\n"
        + "\n"
        + "\"said\":\n"
        + "- \"ru\": a Russian word said in Russian. A word with \"russian\": true that stands in Russian speech is \"ru\", also when the Spanish word for it sounds almost the same and when Spanish words stand next to it.\n"
        + "- \"es\": a Spanish word; the letters only spell its sound (грасиас is gracias, кьеро is quiero, комер is comer, уна is una).\n"
        + "- \"mix\": a Spanish word with a Russian ending put on it (индикативом, перфектно), or you cannot tell.\n"
        + "\n"
        + "A word written in letters of both alphabets is judged as the word it was meant to be: Nет is the Russian нет (\"russian\": true, \"ru\"), "
        + "Теngo is the Spanish tengo (\"russian\": false, \"es\"), a Spanish word with a Russian ending put on it is \"mix\".\n"
        + "\n"
        + "Answer with JSON only, one answer for every item, the word repeated as it was given: "
        + "{\"answers\":[{\"id\":1,\"word\":\"момент\",\"russian\":true,\"said\":\"ru\"},{\"id\":2,\"word\":\"уна\",\"russian\":false,\"said\":\"es\"}]}";

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly OpenAIChatClient _chat;

    public OpenAISpellingJudge(OpenAIChatClient chat) => _chat = chat;

    public string SystemPrompt => Prompt;

    public async Task<SpellingAnswers> AskAsync(IReadOnlyList<SpellingQuestion> questions, string model, float temperature,
        string? baseUrlOverride = null, CancellationToken cancellationToken = default)
    {
        if (!_chat.HasKey)
            throw new InvalidOperationException("OpenAI API key not configured");

        var answer = await _chat.CompleteAsync(model, temperature, Prompt, BuildUserPrompt(questions), baseUrlOverride, jsonAnswer: true, cancellationToken);
        return new SpellingAnswers(ReadAnswers(answer.Content, questions), answer.Content, answer.PromptTokens, answer.CompletionTokens);
    }

    /// <summary>The questions as one JSON object; Cyrillic letters are written as they are.</summary>
    public static string BuildUserPrompt(IReadOnlyList<SpellingQuestion> questions) =>
        JsonSerializer.Serialize(new { items = questions.Select(q => new { id = q.Id, word = q.Word, text = q.Text }) }, Json);

    /// <summary>
    /// The answers of the judge by the number of the word. What cannot be read gives no answer for its word, and
    /// the word stays: a text that is no JSON, an item without a number or with a number nobody asked about, an
    /// answer that is none of the three, an answer that repeats another word than the one asked under its number.
    /// </summary>
    public static IReadOnlyDictionary<int, SaidIn> ReadAnswers(string content, IReadOnlyList<SpellingQuestion> questions)
    {
        var said = new Dictionary<int, SaidIn>();
        var asked = questions.ToDictionary(q => q.Id, q => TranscriptWords.LettersOf(q.Word));
        int from = content.IndexOf('{'), to = content.LastIndexOf('}');
        if (from < 0 || to <= from)
            return said;

        try
        {
            using var json = JsonDocument.Parse(content[from..(to + 1)]);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Array)
                return said;

            foreach (var item in answers.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idValue))
                    continue;
                int id;
                if (idValue.ValueKind == JsonValueKind.Number && idValue.TryGetInt32(out var number))
                    id = number;
                else if (idValue.ValueKind != JsonValueKind.String || !int.TryParse(idValue.GetString(), out id))
                    continue;
                if (!asked.TryGetValue(id, out var word))
                    continue;
                if (item.TryGetProperty("word", out var echo) && echo.ValueKind == JsonValueKind.String
                    && !string.Equals(TranscriptWords.LettersOf(echo.GetString()!), word, StringComparison.Ordinal))
                    continue; // the answer of another item

                var answer = item.TryGetProperty("said", out var saidValue) && saidValue.ValueKind == JsonValueKind.String
                    ? saidValue.GetString()!.Trim().ToLowerInvariant()
                    : "";
                var language = answer switch
                {
                    "es" => SaidIn.Spanish,
                    "ru" => SaidIn.Russian,
                    "mix" => SaidIn.Both,
                    _ => SaidIn.Unknown
                };
                if (language != SaidIn.Unknown)
                    said[id] = language;
            }
        }
        catch (JsonException)
        {
            // Not the JSON asked for: no answer for any word of the request.
        }

        return said;
    }
}
