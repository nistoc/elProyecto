namespace TranslationImprover.Features.Refine.Application;

/// <summary>In which language a word of the transcript was said, as the judge answers.</summary>
public enum SaidIn
{
    /// <summary>No answer for the word, or one that is none of the three.</summary>
    Unknown,
    Spanish,
    Russian,

    /// <summary>A word made of both on purpose: a Spanish stem with a Russian ending.</summary>
    Both
}

/// <summary>One word in doubt as it is put to the judge.</summary>
/// <param name="Id">Number of the word inside its request, from 1.</param>
/// <param name="Word">The word as the transcript writes it.</param>
/// <param name="Text">The line around the word, the word marked as <c>[[word]]</c>.</param>
public sealed record SpellingQuestion(int Id, string Word, string Text);

/// <param name="Said">The answer by the number of the word; a word without an answer is not in it.</param>
/// <param name="RawText">The answer as it came.</param>
public sealed record SpellingAnswers(IReadOnlyDictionary<int, SaidIn> Said, string RawText, int PromptTokens, int CompletionTokens);

/// <summary>
/// Says for each word in doubt in which language it was said. It never writes a word: the spelling is the
/// code's (<c>CandidateRespelling</c>). A request that cannot be answered throws.
/// </summary>
public interface ISpellingJudge
{
    /// <summary>What the judge is told once per request; recorded with the run.</summary>
    string SystemPrompt { get; }

    Task<SpellingAnswers> AskAsync(IReadOnlyList<SpellingQuestion> questions, string model, float temperature,
        string? baseUrlOverride = null, CancellationToken cancellationToken = default);
}
