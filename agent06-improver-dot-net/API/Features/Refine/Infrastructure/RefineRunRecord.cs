using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using TranslationImprover.Features.Refine.Application;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// The record of a run next to its output, <c>&lt;output&gt;.prompt.md</c>: which prompts, model and block
/// geometry produced that output, written at the START of the run (kept even when the run fails) so runs can be
/// compared; what the run came to is added at its end. A run without an output file has no record. A record
/// that cannot be written never fails the run.
/// </summary>
public sealed class RefineRunRecord
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly string? _path;
    private readonly string? _outputName;
    private readonly ILogger? _logger;

    public RefineRunRecord(RefineJobRequest request, string artifactRoot, ILogger? logger = null)
    {
        _logger = logger;
        if (string.IsNullOrEmpty(request.OutputFilePath))
            return;
        var outFull = Path.Combine(artifactRoot, RefineJobRequestMapper.RelativePath(request.OutputFilePath));
        _outputName = Path.GetFileName(outFull);
        _path = Path.Combine(Path.GetDirectoryName(outFull)!, Path.GetFileNameWithoutExtension(outFull) + ".prompt.md");
    }

    public async Task WriteStartAsync(RefineJobRequest request, string systemPrompt, string promptTemplate, int batchSize, int contextLines, CancellationToken ct)
    {
        if (_path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var inv = CultureInfo.InvariantCulture;
            var lines = new[]
            {
                "# Refiner run",
                "",
                $"- Started: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", inv)} UTC",
                $"- Source: {request.SourceName ?? "(inline content)"}",
                $"- Output: {_outputName}",
                $"- Model: {request.Model}, temperature {request.Temperature.ToString(inv)}",
                $"- Lines per block: {batchSize}, context lines: {contextLines}",
                request.RespellingOnly
                    ? "- Check of edits: on — of what the model changes only a word in Cyrillic letters respelled in Latin letters is kept"
                    : "- Check of edits: off — the lines are taken as the model wrote them",
                "",
                "## System prompt" + (request.SystemPrompt == null ? " (built-in)" : ""),
                "",
                "```text",
                systemPrompt.Trim(),
                "```",
                "",
                "## User prompt template" + (string.IsNullOrEmpty(request.PromptFile) ? " (built-in)" : ""),
                "",
                "```text",
                promptTemplate.Trim(),
                "```",
                ""
            };
            await File.WriteAllTextAsync(_path, string.Join("\n", lines), Utf8, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Could not write refiner run record for {Output}", _outputName);
        }
    }

    /// <summary>The words in doubt of a run, for the head of its record.</summary>
    /// <param name="Lines">The lines of the transcript.</param>
    /// <param name="LinesWithWords">The lines that have a word in doubt.</param>
    /// <param name="Requests">The requests the words are parted into.</param>
    /// <param name="Judges">The models every request is put to; none named — the model of the request alone.</param>
    public sealed record WordsInDoubt(int Lines, int LinesWithWords, int OfBothAlphabets, int SoundingSpanish, int ShortInSpanish, int Requests,
        IReadOnlyList<string>? Judges = null)
    {
        public int Words => OfBothAlphabets + SoundingSpanish + ShortInSpanish;

        /// <summary>«3, each put to 2 models» — what is billed is the product.</summary>
        public string RequestsText => Requests > 0 && Judges is { Count: > 1 }
            ? $"{Requests}, each put to {Judges.Count} models"
            : Requests.ToString(CultureInfo.InvariantCulture);

        public string Summary =>
            $"{Words} in {LinesWithWords} of {Lines} lines — {SoundingSpanish} in Cyrillic letters that sound like a Spanish word of the transcript, "
            + $"{OfBothAlphabets} of both alphabets, {ShortInSpanish} short ones inside Spanish speech; requests: {RequestsText}";
    }

    /// <summary>The head of the record of a run that asks about the words in doubt only.</summary>
    public async Task WriteWordsStartAsync(RefineJobRequest request, string systemPrompt, WordsInDoubt words, CancellationToken ct)
    {
        if (_path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var inv = CultureInfo.InvariantCulture;
            var judges = words.Judges is { Count: > 0 } ? words.Judges : new[] { request.Model };
            var lines = new[]
            {
                "# Refiner run",
                "",
                $"- Started: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", inv)} UTC",
                $"- Source: {request.SourceName ?? "(inline content)"}",
                $"- Output: {_outputName}",
                $"- {(judges.Count > 1 ? "Models" : "Model")}: {string.Join(" and ", judges)}, temperature {request.Temperature.ToString(inv)}",
                "- Mode: words in doubt — the code finds them, the model says for each in which language it was said, the code writes the spelling; no line is sent to be rewritten"
                + (judges.Count > 1 ? "; a word is written anew only when every model names the same language for it" : ""),
                $"- Words in doubt: {words.Summary}",
                "",
                "## System prompt (built-in)",
                "",
                "```text",
                systemPrompt.Trim(),
                "```",
                ""
            };
            await File.WriteAllTextAsync(_path, string.Join("\n", lines), Utf8, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Could not write refiner run record for {Output}", _outputName);
        }
    }

    /// <summary>What a run about the words in doubt came to.</summary>
    /// <param name="Changes">Every word written anew: the time mark of its line, the word before and after.</param>
    /// <param name="Left">Why the other words stayed, with how many for each reason.</param>
    /// <param name="Tokens">The tokens of each model apart — they are billed at different prices.</param>
    public sealed record WordsOutcome(IReadOnlyList<string> Changes, IReadOnlyList<(string Why, int Words)> Left,
        IReadOnlyList<(string Model, int In, int Out)>? Tokens = null);

    public async Task AppendWordsOutcomeAsync(WordsInDoubt words, RefineRunTotals totals, WordsOutcome outcome, CancellationToken ct)
    {
        if (_path == null || !File.Exists(_path)) return;
        try
        {
            var lines = new List<string>
            {
                "## Words in doubt",
                "",
                $"- Written anew: {totals.EditsLetThrough}"
            };
            lines.AddRange(outcome.Changes.Select(c => "  - " + c));
            lines.Add($"- Left as they were: {totals.EditsPutBack}");
            lines.AddRange(outcome.Left.Where(l => l.Words > 0).Select(l => $"  - {l.Why}: {l.Words}"));
            lines.Add("");
            lines.AddRange(new[]
            {
                "## Outcome",
                "",
                totals.FailedBatches == 0
                    ? $"- Requests: {words.RequestsText}, none failed"
                    : $"- Requests: {words.RequestsText}, FAILED: {totals.FailedBatches} — their words are in the output as they were",
                totals.PromptTokens + totals.CompletionTokens > 0
                    ? $"- Tokens billed: {totals.PromptTokens} in, {totals.CompletionTokens} out"
                      + (outcome.Tokens is { Count: > 1 } ? " — " + string.Join("; ", outcome.Tokens.Select(t => $"{t.Model}: {t.In} in, {t.Out} out")) : "")
                    : "- Tokens billed: not reported by the API",
                ""
            });
            await File.AppendAllTextAsync(_path, string.Join("\n", lines), Utf8, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Could not add the outcome to the run record of {Output}", _outputName);
        }
    }

    /// <summary>What the check of edits did in a run, for the record.</summary>
    /// <param name="Rule">Which rule about the neighbours of a word was in force, and why.</param>
    /// <param name="Kept">Every change let through: the time mark of its line, the words before and after.</param>
    public sealed record EditCheckReport(string Rule, IReadOnlyList<string> Kept);

    /// <summary>Adds what the run came to, once it is through.</summary>
    /// <param name="check">The run had the check of edits on: what it did is written too.</param>
    public async Task AppendOutcomeAsync(int blocks, RefineRunTotals totals, EditCheckReport? check, CancellationToken ct)
    {
        if (_path == null || !File.Exists(_path)) return;
        try
        {
            var lines = new List<string>();
            if (check != null)
            {
                lines.AddRange(new[]
                {
                    "## Check of edits",
                    "",
                    $"- {check.Rule}",
                    $"- Changes of the model let through: {totals.EditsLetThrough}",
                    $"- Changes of the model put back as they were: {totals.EditsPutBack}"
                });
                lines.AddRange(check.Kept.Select(k => "  - " + k));
                lines.Add("");
            }

            lines.AddRange(new[]
            {
                "## Outcome",
                "",
                totals.FailedBatches == 0
                    ? $"- Blocks: {blocks}, none failed"
                    : $"- Blocks: {blocks}, FAILED: {totals.FailedBatches} — their lines are in the output as they were, not refined",
                totals.PromptTokens + totals.CompletionTokens > 0
                    ? $"- Tokens billed: {totals.PromptTokens} in, {totals.CompletionTokens} out"
                    : "- Tokens billed: not reported by the API",
                ""
            });
            await File.AppendAllTextAsync(_path, string.Join("\n", lines), Utf8, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Could not add the outcome to the run record of {Output}", _outputName);
        }
    }
}
