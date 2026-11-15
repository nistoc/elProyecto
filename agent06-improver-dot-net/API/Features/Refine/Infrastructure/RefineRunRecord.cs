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
