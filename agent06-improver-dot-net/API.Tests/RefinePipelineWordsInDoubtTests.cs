using System.Text.Json;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>
/// A run asked with <c>CandidatesOnly</c>: the model is asked about the words in doubt only, no line is sent to
/// be rewritten, and the output differs from the source in those words alone.
/// </summary>
public class RefinePipelineWordsInDoubtTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "refine-words-in-doubt-" + Guid.NewGuid().ToString("N"));

    public RefinePipelineWordsInDoubtTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private const string Transcript =
        ">>>>>>>\n"
        + "- 1.00 Olga: \"Vale, tengo una casa en Madrid. Un tema.\"\n"
        + "- 2.00 Maria: \"Да, вале, это мой дом.\"\n"
        + "- 3.00 Olga: \"Каса у меня большая, Теngo и Nет.\"\n"
        + "- 4.00 Maria: \"Una casa эн Madrid, это тема.\"\n"
        + "- 5.00 Olga: \"Ну, вале, пошли. Кiero casaчка.\"\n"
        + "<<<<<\n";

    /// <summary>The run must not send a line to be rewritten.</summary>
    private sealed class NoLinesClient : IOpenAIRefineClient
    {
        public Task<BatchResult> RefineBatchAsync(BatchInfo batchInfo, string model, float temperature, string systemPrompt,
            string userPromptTemplate, string? baseUrlOverride = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A run about the words in doubt sent lines to the model");
    }

    private sealed class FakeJudge : ISpellingJudge
    {
        /// <summary>The number of the request that fails, as does every one after it; 0 — none fails.</summary>
        public int FailsFrom { get; init; }

        /// <summary>What a request made again answers otherwise for a word: a language, or null for no answer.</summary>
        public Dictionary<string, SaidIn?> AgainSays { get; } = new();

        public List<IReadOnlyList<SpellingQuestion>> Asked { get; } = new();
        public string? ModelSeen { get; private set; }

        public string SystemPrompt => "Say what each word is.";

        public Task<SpellingAnswers> AskAsync(IReadOnlyList<SpellingQuestion> questions, string model, float temperature,
            string? baseUrlOverride = null, CancellationToken cancellationToken = default)
        {
            Asked.Add(questions);
            ModelSeen = model;
            if (FailsFrom > 0 && Asked.Count >= FailsFrom)
                throw new InvalidOperationException("HTTP 500");

            var again = Asked.Count % 2 == 0;
            var said = new Dictionary<int, SaidIn>();
            foreach (var q in questions)
            {
                SaidIn? answer = q.Word switch
                {
                    "вале" => q.Text.Contains("пошли") ? SaidIn.Russian : SaidIn.Spanish,
                    "Каса" or "Теngo" or "эн" or "Кiero" => SaidIn.Spanish,
                    "Nет" or "тема" => SaidIn.Russian,
                    "casaчка" => SaidIn.Both,
                    _ => null
                };
                if (again && AgainSays.TryGetValue(q.Word, out var other))
                    answer = other;
                if (answer is { } language)
                    said[q.Id] = language;
            }

            return Task.FromResult(new SpellingAnswers(said, "{\"answers\":[]}", PromptTokens: 700, CompletionTokens: 90));
        }
    }

    private async Task<(RefineJobStatus Job, string Output, string Record)> RunAsync(ISpellingJudge? judge, string output)
    {
        var store = new InMemoryRefineJobStore();
        var pipeline = new RefinePipeline(store, new NoLinesClient(), new FilePromptLoader(), judge: judge);
        var jobId = store.Create();

        await pipeline.RunAsync(jobId, RefineJobRequestMapper.Normalize(new RefineJobRequest
        {
            InputContent = Transcript,
            OutputFilePath = output,
            CandidatesOnly = true,
            IntermediateDir = "refine_batches",
        }), _root, _root);

        var job = store.Get(jobId)!;
        string Read(string name) => File.Exists(Path.Combine(_root, name)) ? File.ReadAllText(Path.Combine(_root, name)).Replace("\r\n", "\n") : "";
        return (job, Read(output), Read(Path.GetFileNameWithoutExtension(output) + ".prompt.md"));
    }

    [Fact]
    public async Task Only_the_words_in_doubt_are_asked_and_the_code_writes_the_spelling()
    {
        var judge = new FakeJudge();

        var (job, output, record) = await RunAsync(judge, "transcript_fixed_words.md");

        Assert.Equal(RefineJobState.Completed, job.State);
        Assert.Equal(
            ">>>>>>>\n"
            + "- 1.00 Olga: \"Vale, tengo una casa en Madrid. Un tema.\"\n"
            + "- 2.00 Maria: \"Да, vale, это мой дом.\"\n"
            + "- 3.00 Olga: \"Casa у меня большая, Tengo и Нет.\"\n"
            + "- 4.00 Maria: \"Una casa en Madrid, это тема.\"\n"
            + "- 5.00 Olga: \"Ну, вале, пошли. Кiero casaчка.\"\n"
            + "<<<<<\n",
            output);

        // One request with the nine words, made twice; the judge sees the text of a line, never its time mark or speaker.
        Assert.Equal(CandidateRespelling.TimesAsked, judge.Asked.Count);
        var questions = judge.Asked[0];
        Assert.Equal(questions, judge.Asked[1]);
        Assert.Equal(new[] { "вале", "Каса", "Теngo", "Nет", "эн", "тема", "вале", "Кiero", "casaчка" }, questions.Select(q => q.Word));
        Assert.All(questions, q => Assert.DoesNotContain("Olga", q.Text));
        Assert.Equal(RefineJobRequestMapper.DefaultJudgeModel, judge.ModelSeen);

        Assert.Equal(new RefineRunTotals { PromptTokens = 1400, CompletionTokens = 180, EditsLetThrough = 5, EditsPutBack = 4 }, job.Totals);
        var block = Assert.Single(job.Batches);
        Assert.Equal((2, 5), (block.StartLine, block.EndLine));
        Assert.Contains("Да, вале, это мой дом.", block.OriginalText);
        Assert.Contains("Да, vale, это мой дом.", block.RefinedText);
        Assert.DoesNotContain("1.00 Olga", block.OriginalText); // a line without a word in doubt is not part of a request
        Assert.Equal("вале — vale\nКаса — casa\nТеngo — tengo\nNет\nэн — en\nтема — tema\nвале — vale\nКiero\ncasaчка", block.AskedWords);
        Assert.Equal("", block.ContextText);

        Assert.Contains("- Mode: words in doubt", record);
        Assert.Contains("a word is written anew only when every answer to its request names the same language", record);
        Assert.Contains("- Words in doubt: 9 in 4 of 5 lines — 4 in Cyrillic letters that sound like a Spanish word of the transcript, 4 of both alphabets, 1 short ones inside Spanish speech; requests: 1, each made 2 times", record);
        Assert.Contains("Say what each word is.", record);
        Assert.Contains("- Written anew: 5\n  - 2.00 «вале,» → «vale,»\n  - 3.00 «Каса» → «Casa»\n  - 3.00 «Теngo» → «Tengo»\n  - 3.00 «Nет.» → «Нет.»\n  - 4.00 «эн» → «en»\n", record);
        Assert.Contains("- Left as they were: 4\n", record);
        Assert.Contains("  - said in Russian: 2\n", record);
        Assert.Contains("  - made of both languages, or the judge could not tell: 1\n", record);
        Assert.Contains("  - no spelling the code can vouch for: 1\n", record);
        Assert.Contains("- Requests: 1, each made 2 times, none failed", record);
        Assert.Contains("- Tokens billed: 1400 in, 180 out", record);

        var stored = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "refine_batches", "batch_0001_of_0001.json"))).RootElement;
        Assert.Equal("[answer 1]\n{\"answers\":[]}\n\n[answer 2]\n{\"answers\":[]}", stored.GetProperty("rawText").GetString());
        Assert.Equal(5, stored.GetProperty("editsLetThrough").GetInt32());
    }

    [Fact]
    public async Task A_word_the_two_answers_disagree_on_stays_as_it_was()
    {
        // Made again, the request gets «Каса» called Russian and no answer for «эн».
        var judge = new FakeJudge { AgainSays = { ["Каса"] = SaidIn.Russian, ["эн"] = null } };

        var (job, output, record) = await RunAsync(judge, "transcript_fixed_differ.md");

        Assert.Equal(
            ">>>>>>>\n"
            + "- 1.00 Olga: \"Vale, tengo una casa en Madrid. Un tema.\"\n"
            + "- 2.00 Maria: \"Да, vale, это мой дом.\"\n"
            + "- 3.00 Olga: \"Каса у меня большая, Tengo и Нет.\"\n"
            + "- 4.00 Maria: \"Una casa эн Madrid, это тема.\"\n"
            + "- 5.00 Olga: \"Ну, вале, пошли. Кiero casaчка.\"\n"
            + "<<<<<\n",
            output);
        Assert.Equal(new RefineRunTotals { PromptTokens = 1400, CompletionTokens = 180, EditsLetThrough = 3, EditsPutBack = 6 }, job.Totals);
        Assert.Contains("- Written anew: 3\n", record);
        Assert.Contains("  - the answers of the judge differ: 2\n", record);
    }

    [Fact]
    public async Task When_the_request_fails_the_second_time_its_words_stay_and_the_first_answer_is_billed()
    {
        var (job, output, record) = await RunAsync(new FakeJudge { FailsFrom = 2 }, "transcript_fixed_failed_again.md");

        Assert.Equal(Transcript, output);
        Assert.Equal(new RefineRunTotals { FailedBatches = 1, PromptTokens = 700, CompletionTokens = 90, EditsPutBack = 9 }, job.Totals);
        var block = Assert.Single(job.Batches);
        Assert.False(block.Ok);
        Assert.Equal("[answer 1]\n{\"answers\":[]}", block.RawText);
        Assert.Contains("  - the request failed: 9\n", record);
    }

    [Fact]
    public async Task A_request_that_fails_leaves_its_words_as_they_were()
    {
        var (job, output, record) = await RunAsync(new FakeJudge { FailsFrom = 1 }, "transcript_fixed_failed.md");

        Assert.Equal(RefineJobState.Completed, job.State);
        Assert.Equal(Transcript, output);
        Assert.Equal(new RefineRunTotals { FailedBatches = 1, EditsPutBack = 9 }, job.Totals);
        var block = Assert.Single(job.Batches);
        Assert.False(block.Ok);
        Assert.Equal("HTTP 500", block.Error);
        Assert.Contains("- Written anew: 0\n", record);
        Assert.Contains("  - the request failed: 9\n", record);
        Assert.Contains("- Requests: 1, each made 2 times, FAILED: 1", record);
    }

    [Fact]
    public async Task A_transcript_without_a_word_in_doubt_comes_back_as_it_was_and_nothing_is_asked()
    {
        var judge = new FakeJudge();
        var store = new InMemoryRefineJobStore();
        var pipeline = new RefinePipeline(store, new NoLinesClient(), new FilePromptLoader(), judge: judge);
        var jobId = store.Create();
        const string plain = "- 1.00 Olga: \"Vale, tengo una casa.\"\n- 2.00 Maria: \"Это мой дом.\"\n";

        await pipeline.RunAsync(jobId, new RefineJobRequest { InputContent = plain, OutputFilePath = "plain_fixed.md", CandidatesOnly = true }, _root, _root);

        Assert.Equal(RefineJobState.Completed, store.Get(jobId)!.State);
        Assert.Empty(judge.Asked);
        Assert.Equal(plain, File.ReadAllText(Path.Combine(_root, "plain_fixed.md")).Replace("\r\n", "\n"));
        Assert.Contains("- Requests: 0, none failed", File.ReadAllText(Path.Combine(_root, "plain_fixed.prompt.md")));
    }

    [Fact]
    public async Task Without_a_judge_the_run_fails_and_says_why()
    {
        var (job, output, _) = await RunAsync(judge: null, "transcript_fixed_nojudge.md");

        Assert.Equal(RefineJobState.Failed, job.State);
        Assert.Contains("no judge", job.ErrorMessage);
        Assert.Equal("", output);
    }
}
