using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class RefinePipeline : IRefinePipeline
{
    /// <summary>Fixed system prompt; the operator edits only the user template.</summary>
    public const string SystemPrompt = "You are a precise transcript editor. You fix Cyrillic transliterations of Spanish words back to Latin script. You preserve everything else exactly as is.";

    private readonly IRefineJobStore _store;
    private readonly IOpenAIRefineClient _client;
    private readonly IPromptLoader _promptLoader;
    private readonly INodeModel? _nodeModel;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<RefinePipeline>? _logger;

    public RefinePipeline(
        IRefineJobStore store,
        IOpenAIRefineClient client,
        IPromptLoader promptLoader,
        INodeModel? nodeModel = null,
        IHttpClientFactory? httpClientFactory = null,
        ILogger<RefinePipeline>? logger = null)
    {
        _store = store;
        _client = client;
        _promptLoader = promptLoader;
        _nodeModel = nodeModel;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task RunAsync(string jobId, RefineJobRequest request, string workspaceRoot, string artifactRoot, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> contentLines;
        IReadOnlyList<string> headerLines;
        IReadOnlyList<string> footerLines;

        if (!string.IsNullOrEmpty(request.InputContent))
        {
            var lines = request.InputContent!.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').Select(l => l + "\n").ToList();
            TranscriptParser.ParseStructure(lines, out headerLines, out contentLines, out footerLines);
        }
        else
        {
            var inputPath = Path.Combine(artifactRoot, request.InputFilePath!.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var allLines = (await File.ReadAllLinesAsync(inputPath, Encoding.UTF8, cancellationToken)).Select(l => l + "\n").ToList();
            TranscriptParser.ParseStructure(allLines, out headerLines, out contentLines, out footerLines);
        }

        if (contentLines.Count == 0)
        {
            _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Completed, OutputFilePath = request.OutputFilePath });
            await FireCallbackAsync(jobId);
            return;
        }

        _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Running, CurrentPhase = "Starting" });
        _nodeModel?.EnsureNode(jobId, null, jobId, "job");
        _nodeModel?.StartNode(jobId);

        var batchSize = request.BatchSize > 0 ? request.BatchSize : 10;
        var contextLines = request.ContextLines >= 0 ? request.ContextLines : 3;
        var batches = TranscriptParser.CreateBatches(contentLines, batchSize);
        _store.Update(jobId, new RefineJobStatusUpdate { TotalBatches = batches.Count });

        var refineParent = jobId + ":refine";
        _nodeModel?.EnsureNode(refineParent, jobId, jobId, "phase");
        _nodeModel?.StartNode(refineParent);

        var promptTemplate = await _promptLoader.LoadAsync(request.PromptFile, workspaceRoot, cancellationToken);
        var systemPrompt = request.SystemPrompt ?? SystemPrompt; // operator override from the Refiner tab
        await WriteRunRecordAsync(request, artifactRoot, systemPrompt, promptTemplate, batchSize, contextLines, cancellationToken);
        _store.Update(jobId, new RefineJobStatusUpdate { PromptPreview = BuildPromptPreview(systemPrompt, promptTemplate) });
        var intermediateDir = request.SaveIntermediate && !string.IsNullOrEmpty(request.IntermediateDir)
            ? Path.Combine(artifactRoot, request.IntermediateDir.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : null;
        if (intermediateDir != null)
        {
            Directory.CreateDirectory(intermediateDir);
            // The folder holds the LAST run only: drop the previous run's batches, then record the prompt used.
            foreach (var old in Directory.EnumerateFiles(intermediateDir, "batch_*.json"))
            {
                try { File.Delete(old); } catch { /* best effort */ }
            }
            await File.WriteAllTextAsync(Path.Combine(intermediateDir, "prompt.txt"), BuildPromptPreview(systemPrompt, promptTemplate), Encoding.UTF8, cancellationToken);
        }

        var fixedContent = new List<string>();
        IReadOnlyList<string>? previousContext = null;
        string? outputPathFull = null;

        try
        {
            for (var i = 0; i < batches.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batchInfo = batches[i];
                if (previousContext != null && contextLines > 0)
                    batchInfo = new BatchInfo { Index = batchInfo.Index, StartLine = batchInfo.StartLine, EndLine = batchInfo.EndLine, Lines = batchInfo.Lines, Context = previousContext.TakeLast(contextLines).ToList() };

                var batchNodeId = refineParent + ":batch-" + i;
                _nodeModel?.EnsureNode(batchNodeId, refineParent, jobId, "batch");
                _nodeModel?.StartNode(batchNodeId);

                _store.Update(jobId, new RefineJobStatusUpdate { CurrentBatch = i + 1, CurrentPhase = $"Batch {i + 1}/{batches.Count}" });
                var percent = batches.Count > 0 ? (i + 1) * 100 / batches.Count : 100;
                _nodeModel?.UpdateNodeProgress(jobId, percent, $"Batch {i + 1}/{batches.Count}");

                var result = await _client.RefineBatchAsync(batchInfo, request.Model, request.Temperature, systemPrompt, promptTemplate, request.OpenAIBaseUrl, cancellationToken);

                if (result.Success)
                {
                    fixedContent.AddRange(result.FixedLines);
                    previousContext = result.FixedLines;
                }
                else
                {
                    fixedContent.AddRange(batchInfo.Lines);
                    previousContext = batchInfo.Lines;
                    _logger?.LogWarning("Batch {Index} failed: {Error}", i, result.Error);
                }

                // Publish the before/after fragment so the UI can render this batch's improvement live.
                var refinedForDelta = result.Success ? result.FixedLines : batchInfo.Lines;
                var delta = new RefineBatchDelta
                {
                    Index = i + 1,
                    Total = batches.Count,
                    OriginalText = JoinLines(batchInfo.Lines),
                    RefinedText = JoinLines(refinedForDelta),
                    Ok = result.Success,
                    Error = result.Error,
                    ContextText = batchInfo.Context is { Count: > 0 } ctx ? JoinLines(ctx) : "",
                    StartLine = batchInfo.StartLine + 1, // parser: 0-based start, exclusive end → 1-based inclusive
                    EndLine = batchInfo.EndLine
                };
                _store.Update(jobId, new RefineJobStatusUpdate { AppendBatch = delta });

                // Persist every batch (ok or failed) so the before/after view survives a page reload.
                if (intermediateDir != null)
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
                        error = delta.Error
                    });
                    await File.WriteAllTextAsync(Path.Combine(intermediateDir, $"batch_{i + 1:D4}_of_{batches.Count:D4}.json"), json, Encoding.UTF8, cancellationToken);
                }

                _nodeModel?.CompleteNode(batchNodeId, result.Success ? RefineJobState.Completed : RefineJobState.Failed, DateTimeOffset.UtcNow, result.Error);
            }

            _nodeModel?.CompleteNode(refineParent, RefineJobState.Completed, DateTimeOffset.UtcNow);

            var fullContent = new List<string>();
            fullContent.AddRange(headerLines);
            fullContent.AddRange(fixedContent);
            fullContent.AddRange(footerLines);

            if (!string.IsNullOrEmpty(request.OutputFilePath))
            {
                var rel = request.OutputFilePath.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                outputPathFull = Path.Combine(artifactRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPathFull)!);
                await File.WriteAllLinesAsync(outputPathFull, fullContent.Select(l => l.TrimEnd('\n')), Encoding.UTF8, cancellationToken);
            }

            _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Completed, ProgressPercent = 100, CurrentBatch = batches.Count, TotalBatches = batches.Count, OutputFilePath = request.OutputFilePath });
            _nodeModel?.EnsureNode(jobId, null, jobId, "job", request.OutputFilePath != null ? new Dictionary<string, object?> { ["output_file_path"] = request.OutputFilePath } : null);
            _nodeModel?.CompleteNode(jobId, RefineJobState.Completed, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Cancelled, ErrorMessage = "Cancelled" });
            _nodeModel?.CompleteNode(refineParent, RefineJobState.Cancelled, DateTimeOffset.UtcNow, "Cancelled");
            _nodeModel?.CompleteNode(jobId, RefineJobState.Cancelled, DateTimeOffset.UtcNow, "Cancelled");
            await FireCallbackAsync(jobId);
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Refine pipeline failed for job {JobId}", jobId);
            _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Failed, ErrorMessage = ex.Message });
            _nodeModel?.CompleteNode(refineParent, RefineJobState.Failed, DateTimeOffset.UtcNow, ex.Message);
            _nodeModel?.CompleteNode(jobId, RefineJobState.Failed, DateTimeOffset.UtcNow, ex.Message);
            await FireCallbackAsync(jobId);
            return;
        }

        await FireCallbackAsync(jobId);
    }

    private static string JoinLines(IReadOnlyList<string> lines) =>
        string.Join("\n", lines.Select(l => l.TrimEnd('\n', '\r'))).TrimEnd();

    /// <summary>
    /// Writes <c>&lt;output&gt;.prompt.md</c> next to the refined file at the START of the run: which prompts,
    /// model and batch geometry produced that output (kept even if the run fails), so runs can be compared.
    /// </summary>
    private async Task WriteRunRecordAsync(RefineJobRequest request, string artifactRoot, string systemPrompt, string promptTemplate,
        int batchSize, int contextLines, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.OutputFilePath)) return;
        try
        {
            var rel = request.OutputFilePath.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var outFull = Path.Combine(artifactRoot, rel);
            var recordPath = Path.Combine(Path.GetDirectoryName(outFull)!, Path.GetFileNameWithoutExtension(outFull) + ".prompt.md");
            Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var lines = new[]
            {
                "# Refiner run",
                "",
                $"- Started: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", inv)} UTC",
                $"- Source: {request.SourceName ?? "(inline content)"}",
                $"- Output: {Path.GetFileName(outFull)}",
                $"- Model: {request.Model}, temperature {request.Temperature.ToString(inv)}",
                $"- Lines per block: {batchSize}, context lines: {contextLines}",
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
            await File.WriteAllTextAsync(recordPath, string.Join("\n", lines), new UTF8Encoding(false), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Could not write refiner run record for {Output}", request.OutputFilePath);
        }
    }

    private static string BuildPromptPreview(string systemPrompt, string userPromptTemplate) =>
        $"[system]\n{systemPrompt.Trim()}\n\n[user template]\n{userPromptTemplate.Trim()}";

    private async Task FireCallbackAsync(string jobId)
    {
        var job = _store.Get(jobId);
        if (job?.CallbackUrl == null || _httpClientFactory == null) return;
        try
        {
            var client = _httpClientFactory.CreateClient();
            var json = JsonSerializer.Serialize(new
            {
                job.JobId,
                state = job.State.ToString(),
                job.ProgressPercent,
                job.CurrentPhase,
                job.CurrentBatch,
                job.TotalBatches,
                job.OutputFilePath,
                job.ErrorMessage
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await client.PostAsync(job.CallbackUrl, content);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Callback POST failed for job {JobId}", jobId);
        }
    }
}
