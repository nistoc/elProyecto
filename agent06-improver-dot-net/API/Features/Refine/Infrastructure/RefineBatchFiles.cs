using System.Text;
using System.Text.Json;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// The folder of the LAST run's blocks (<c>batch_NNNN_of_NNNN.json</c> and <c>prompt.txt</c>): every block, ok
/// or failed, with what was sent, what the model answered and what was kept, so the before/after view survives
/// a page reload and a run can be judged afterwards without asking the model again.
/// </summary>
public sealed class RefineBatchFiles
{
    private readonly string _directory;

    private RefineBatchFiles(string directory) => _directory = directory;

    /// <summary>The folder of a request, or null when the request keeps no blocks.</summary>
    public static RefineBatchFiles? For(RefineJobRequest request, string artifactRoot) =>
        request.SaveIntermediate && !string.IsNullOrEmpty(request.IntermediateDir)
            ? new RefineBatchFiles(Path.Combine(artifactRoot, RefineJobRequestMapper.RelativePath(request.IntermediateDir)))
            : null;

    /// <summary>Drops the blocks of the previous run and records the prompt of this one.</summary>
    public async Task StartAsync(string promptPreview, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        foreach (var old in Directory.EnumerateFiles(_directory, "batch_*.json"))
        {
            try { File.Delete(old); } catch { /* best effort */ }
        }

        await File.WriteAllTextAsync(Path.Combine(_directory, "prompt.txt"), promptPreview, Encoding.UTF8, ct);
    }

    public async Task WriteAsync(RefineBatchDelta delta, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new
        {
            index = delta.Index,
            total = delta.Total,
            startLine = delta.StartLine,
            endLine = delta.EndLine,
            contextText = delta.ContextText,
            originalText = delta.OriginalText,
            refinedText = delta.RefinedText,
            ok = delta.Ok,
            error = delta.Error,
            rawText = delta.RawText,
            editsLetThrough = delta.EditsLetThrough,
            editsPutBack = delta.EditsPutBack,
            promptTokens = delta.PromptTokens,
            completionTokens = delta.CompletionTokens
        });
        await File.WriteAllTextAsync(Path.Combine(_directory, $"batch_{delta.Index:D4}_of_{delta.Total:D4}.json"), json, Encoding.UTF8, ct);
    }
}
