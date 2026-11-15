using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// A refine run from its request to its output. The steps live in their own classes: the transcript and the form
/// of its text (<see cref="TranscriptText"/>), the blocks (<see cref="TranscriptParser"/>), the call of the model
/// (<see cref="IOpenAIRefineClient"/>), the check of what it changed (<see cref="RespellingGuard"/>), the record
/// of the run (<see cref="RefineRunRecord"/>) and of its blocks (<see cref="RefineBatchFiles"/>). This class
/// puts them in order and tells the job store and the node model how far the run is.
/// </summary>
public sealed class RefinePipeline : IRefinePipeline
{
    /// <summary>Fixed system prompt; the operator edits only the user template.</summary>
    public const string SystemPrompt = "You copy the lines of a bilingual (Russian and Spanish) lesson transcript exactly as they are. While copying you respell only Spanish words that were written in Cyrillic letters by ear. You keep the language and the alphabet of every other word.";

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

    /// <summary>What a run works with once its request is read: the lines, their blocks and the prompts.</summary>
    private sealed record RunPlan(
        IReadOnlyList<string> ContentLines,
        IReadOnlyList<BatchInfo> Batches,
        int BatchSize,
        int ContextLines,
        string SystemPrompt,
        string PromptTemplate)
    {
        public string PromptPreview => $"[system]\n{SystemPrompt.Trim()}\n\n[user template]\n{PromptTemplate.Trim()}";
    }

    /// <summary>One block after the call and the check: the lines to keep and what is to be recorded of it.</summary>
    private sealed record RefinedBlock(
        IReadOnlyList<string> Lines,
        bool Ok,
        string? Error,
        string RawText,
        int EditsLetThrough,
        int EditsPutBack,
        int PromptTokens,
        int CompletionTokens);

    public async Task RunAsync(string jobId, RefineJobRequest request, string workspaceRoot, string artifactRoot, CancellationToken cancellationToken = default)
    {
        var refineParent = jobId + ":refine";
        try
        {
            var source = await TranscriptText.ReadAsync(request, artifactRoot, cancellationToken);
            if (source.Content.Count == 0)
            {
                _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Completed, OutputFilePath = request.OutputFilePath });
                await FireCallbackAsync(jobId);
                return;
            }

            _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Running, CurrentPhase = "Starting" });
            _nodeModel?.EnsureNode(jobId, null, jobId, "job");
            _nodeModel?.StartNode(jobId);

            var plan = await PlanAsync(request, source, workspaceRoot, cancellationToken);
            _store.Update(jobId, new RefineJobStatusUpdate { TotalBatches = plan.Batches.Count });
            _nodeModel?.EnsureNode(refineParent, jobId, jobId, "phase");
            _nodeModel?.StartNode(refineParent);

            var record = new RefineRunRecord(request, artifactRoot, _logger);
            await record.WriteStartAsync(request, plan.SystemPrompt, plan.PromptTemplate, plan.BatchSize, plan.ContextLines, cancellationToken);
            _store.Update(jobId, new RefineJobStatusUpdate { PromptPreview = plan.PromptPreview });
            var batchFiles = RefineBatchFiles.For(request, artifactRoot);
            if (batchFiles != null)
                await batchFiles.StartAsync(plan.PromptPreview, cancellationToken);

            var (refined, totals) = await RefineBlocksAsync(jobId, request, plan, batchFiles, cancellationToken);
            _nodeModel?.CompleteNode(refineParent, RefineJobState.Completed, DateTimeOffset.UtcNow);

            if (!string.IsNullOrEmpty(request.OutputFilePath))
            {
                var outputPath = Path.Combine(artifactRoot, RefineJobRequestMapper.RelativePath(request.OutputFilePath));
                await source.WriteAsync(outputPath, refined, cancellationToken);
            }

            if (request.RespellingOnly)
                _logger?.LogInformation("Refine job {JobId}: check of edits let through {LetThrough}, put back {PutBack}", jobId, totals.EditsLetThrough, totals.EditsPutBack);
            if (totals.FailedBatches > 0)
                _logger?.LogWarning("Refine job {JobId}: {Failed} of {Total} blocks failed; their lines are in the output as they were", jobId, totals.FailedBatches, plan.Batches.Count);
            await record.AppendOutcomeAsync(plan.Batches.Count, totals, request.RespellingOnly, cancellationToken);

            _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Completed, ProgressPercent = 100, CurrentBatch = plan.Batches.Count, TotalBatches = plan.Batches.Count, OutputFilePath = request.OutputFilePath, Totals = totals });
            _nodeModel?.EnsureNode(jobId, null, jobId, "job", request.OutputFilePath != null ? new Dictionary<string, object?> { ["output_file_path"] = request.OutputFilePath } : null);
            _nodeModel?.CompleteNode(jobId, RefineJobState.Completed, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Cancelled, ErrorMessage = "Cancelled" });
            _nodeModel?.CompleteNode(refineParent, RefineJobState.Cancelled, DateTimeOffset.UtcNow, "Cancelled");
            _nodeModel?.CompleteNode(jobId, RefineJobState.Cancelled, DateTimeOffset.UtcNow, "Cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Refine pipeline failed for job {JobId}", jobId);
            _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Failed, ErrorMessage = ex.Message });
            _nodeModel?.CompleteNode(refineParent, RefineJobState.Failed, DateTimeOffset.UtcNow, ex.Message);
            _nodeModel?.CompleteNode(jobId, RefineJobState.Failed, DateTimeOffset.UtcNow, ex.Message);
        }

        await FireCallbackAsync(jobId);
    }

    private async Task<RunPlan> PlanAsync(RefineJobRequest request, TranscriptText source, string workspaceRoot, CancellationToken cancellationToken)
    {
        var batchSize = request.BatchSize > 0 ? request.BatchSize : RefineJobRequestMapper.DefaultBatchSize;
        var contextLines = request.ContextLines >= 0 ? request.ContextLines : RefineJobRequestMapper.DefaultContextLines;
        var promptTemplate = await _promptLoader.LoadAsync(request.PromptFile, workspaceRoot, cancellationToken);
        return new RunPlan(
            source.Content,
            TranscriptParser.CreateBatches(source.Content, batchSize),
            batchSize,
            contextLines,
            request.SystemPrompt ?? SystemPrompt, // operator override from the Refiner tab
            promptTemplate);
    }

    /// <summary>
    /// The blocks one after another: each is sent with the last lines of the block before it, as they came out of
    /// that block, for context. A block whose call failed keeps its lines and is counted, the run goes on.
    /// </summary>
    private async Task<(List<string> Lines, RefineRunTotals Totals)> RefineBlocksAsync(
        string jobId, RefineJobRequest request, RunPlan plan, RefineBatchFiles? batchFiles, CancellationToken cancellationToken)
    {
        var refineParent = jobId + ":refine";
        var count = plan.Batches.Count;
        var refined = new List<string>();
        IReadOnlyList<string>? previous = null;
        // The check of edits knows the Spanish of the transcript as it was before this run.
        var latinWords = request.RespellingOnly ? RespellingGuard.LatinWordsOf(plan.ContentLines) : null;
        var totals = new RefineRunTotals();

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = plan.Batches[i];
            if (previous != null && plan.ContextLines > 0)
                batch = new BatchInfo { Index = batch.Index, StartLine = batch.StartLine, EndLine = batch.EndLine, Lines = batch.Lines, Context = previous.TakeLast(plan.ContextLines).ToList() };

            var batchNodeId = refineParent + ":batch-" + i;
            _nodeModel?.EnsureNode(batchNodeId, refineParent, jobId, "batch");
            _nodeModel?.StartNode(batchNodeId);
            _store.Update(jobId, new RefineJobStatusUpdate { CurrentBatch = i + 1, CurrentPhase = $"Batch {i + 1}/{count}" });
            _nodeModel?.UpdateNodeProgress(jobId, (i + 1) * 100 / count, $"Batch {i + 1}/{count}");

            var block = await RefineBlockAsync(batch, request, plan, latinWords, cancellationToken);
            refined.AddRange(block.Lines);
            previous = block.Lines;
            if (!block.Ok)
                _logger?.LogWarning("Batch {Index} failed: {Error}", i, block.Error);

            totals = totals with
            {
                FailedBatches = totals.FailedBatches + (block.Ok ? 0 : 1),
                PromptTokens = totals.PromptTokens + block.PromptTokens,
                CompletionTokens = totals.CompletionTokens + block.CompletionTokens,
                EditsLetThrough = totals.EditsLetThrough + block.EditsLetThrough,
                EditsPutBack = totals.EditsPutBack + block.EditsPutBack
            };

            // Publish the before/after fragment so the UI can render this batch's improvement live.
            var delta = new RefineBatchDelta
            {
                Index = i + 1,
                Total = count,
                OriginalText = JoinLines(batch.Lines),
                RefinedText = JoinLines(block.Lines),
                Ok = block.Ok,
                Error = block.Error,
                ContextText = batch.Context is { Count: > 0 } context ? JoinLines(context) : "",
                StartLine = batch.StartLine + 1, // parser: 0-based start, exclusive end → 1-based inclusive
                EndLine = batch.EndLine,
                RawText = block.RawText,
                EditsLetThrough = block.EditsLetThrough,
                EditsPutBack = block.EditsPutBack,
                PromptTokens = block.PromptTokens,
                CompletionTokens = block.CompletionTokens
            };
            _store.Update(jobId, new RefineJobStatusUpdate { AppendBatch = delta, Totals = totals });
            if (batchFiles != null)
                await batchFiles.WriteAsync(delta, cancellationToken);

            _nodeModel?.CompleteNode(batchNodeId, block.Ok ? RefineJobState.Completed : RefineJobState.Failed, DateTimeOffset.UtcNow, block.Error);
        }

        return (refined, totals);
    }

    /// <summary>One block: the call of the model and, when the run asks for it, the check of what the model changed.</summary>
    private async Task<RefinedBlock> RefineBlockAsync(BatchInfo batch, RefineJobRequest request, RunPlan plan, IReadOnlySet<string>? latinWords, CancellationToken cancellationToken)
    {
        var result = await _client.RefineBatchAsync(batch, request.Model, request.Temperature, plan.SystemPrompt, plan.PromptTemplate, request.OpenAIBaseUrl, cancellationToken);
        if (!result.Success)
            return new RefinedBlock(batch.Lines, false, result.Error, result.RawAnswer ?? "", 0, 0, result.PromptTokens, result.CompletionTokens);

        var rawText = result.RawAnswer?.Trim() ?? JoinLines(result.FixedLines);
        if (latinWords == null)
            return new RefinedBlock(result.FixedLines, true, null, rawText, 0, 0, result.PromptTokens, result.CompletionTokens);

        var kept = RespellingGuard.Apply(batch.Lines, result.FixedLines, latinWords);
        return new RefinedBlock(kept.Lines, true, null, rawText, kept.LetThrough, kept.PutBack, result.PromptTokens, result.CompletionTokens);
    }

    private static string JoinLines(IReadOnlyList<string> lines) =>
        string.Join("\n", lines.Select(l => l.TrimEnd('\n', '\r'))).TrimEnd();

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
                job.ErrorMessage,
                failedBatches = job.Totals.FailedBatches
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
