using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TranslationImprover.Application;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class RefinePipeline : IRefinePipeline
{
    private const string SystemPrompt = "You are a precise transcript editor. You fix Cyrillic transliterations of Spanish words back to Latin script. You preserve everything else exactly as is.";

    private readonly IRefineJobStore _store;
    private readonly IOpenAIRefineClient _client;
    private readonly IPromptLoader _promptLoader;
    private readonly IRefineJobPause _pause;
    private readonly WorkspaceRoot _serviceWorkspaceRoot;
    private readonly INodeModel? _nodeModel;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<RefinePipeline>? _logger;

    public RefinePipeline(
        IRefineJobStore store,
        IOpenAIRefineClient client,
        IPromptLoader promptLoader,
        IRefineJobPause pause,
        WorkspaceRoot serviceWorkspaceRoot,
        INodeModel? nodeModel = null,
        IHttpClientFactory? httpClientFactory = null,
        ILogger<RefinePipeline>? logger = null)
    {
        _store = store;
        _client = client;
        _promptLoader = promptLoader;
        _pause = pause;
        _serviceWorkspaceRoot = serviceWorkspaceRoot;
        _nodeModel = nodeModel;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task RunAsync(string jobId, RefineJobRequest request, string artifactRoot, CancellationToken cancellationToken = default)
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

        var batchSize = request.BatchSize > 0 ? request.BatchSize : 10;
        var batches = TranscriptParser.CreateBatches(contentLines.ToList(), batchSize);
        await RunBatchLoopAsync(
            jobId,
            request,
            artifactRoot,
            batches,
            headerLines.ToList(),
            footerLines.ToList(),
            contentLines.ToList(),
            startBatchIndex: 0,
            new List<string>(),
            previousContext: null,
            cancellationToken);
    }

    public async Task ResumeAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var job = _store.Get(jobId);
        if (job == null)
            throw new InvalidOperationException("Job not found");
        if (job.State != RefineJobState.Paused)
            throw new InvalidOperationException("Job is not paused");

        var rootForResolve = string.IsNullOrWhiteSpace(job.WorkspaceRootOverride)
            ? _serviceWorkspaceRoot.RootPath
            : Path.GetFullPath(job.WorkspaceRootOverride.Trim());
        var artifactRoot = RefineWorkspacePaths.ResolveEffectiveArtifactRoot(rootForResolve, job.JobDirectoryRelative);
        var checkpointPath = RefinePaths.CheckpointFile(artifactRoot);
        var ck = RefineCheckpoint.TryLoad(checkpointPath);
        if (ck == null || ck.JobId != jobId)
            throw new InvalidOperationException("Checkpoint missing or invalid");

        var request = ck.ToRequest();
        var batches = TranscriptParser.CreateBatches(ck.ContentLines, request.BatchSize > 0 ? request.BatchSize : 10);
        if (batches.Count == 0 || ck.NextBatchIndex >= batches.Count)
        {
            _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Completed, ProgressPercent = 100, ErrorMessage = "Nothing to resume" });
            return;
        }

        _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Running, CurrentPhase = "Resuming" });

        await RunBatchLoopAsync(
            jobId,
            request,
            artifactRoot,
            batches,
            ck.HeaderLines,
            ck.FooterLines,
            ck.ContentLines,
            startBatchIndex: ck.NextBatchIndex,
            ck.FixedLines.ToList(),
            ck.PreviousContextLines,
            cancellationToken);
    }

    public async Task ResumeFromCheckpointAsync(
        string jobId,
        string artifactRoot,
        RefineCheckpoint ck,
        CancellationToken cancellationToken = default)
    {
        var request = ck.ToRequest();
        var batches = TranscriptParser.CreateBatches(ck.ContentLines, request.BatchSize > 0 ? request.BatchSize : 10);
        if (batches.Count == 0 || ck.NextBatchIndex >= batches.Count)
        {
            _store.Update(jobId, new RefineJobStatusUpdate
            {
                State = RefineJobState.Failed,
                ErrorMessage = "Nothing to resume from checkpoint"
            });
            return;
        }

        _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Running, CurrentPhase = "Resuming" });

        await RunBatchLoopAsync(
            jobId,
            request,
            artifactRoot,
            batches,
            ck.HeaderLines,
            ck.FooterLines,
            ck.ContentLines,
            startBatchIndex: ck.NextBatchIndex,
            ck.FixedLines.ToList(),
            ck.PreviousContextLines,
            cancellationToken);
    }

    private async Task RunBatchLoopAsync(
        string jobId,
        RefineJobRequest request,
        string artifactRoot,
        IReadOnlyList<BatchInfo> batches,
        List<string> headerLines,
        List<string> footerLines,
        List<string> contentLines,
        int startBatchIndex,
        List<string> fixedContent,
        IReadOnlyList<string>? previousContext,
        CancellationToken cancellationToken)
    {
        var refineParent = jobId + ":refine";
        var contextLines = request.ContextLines >= 0 ? request.ContextLines : 3;

        _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Running, CurrentPhase = startBatchIndex > 0 ? "Resuming" : "Starting", TotalBatches = batches.Count });
        _nodeModel?.EnsureNode(jobId, null, jobId, "job");
        if (startBatchIndex == 0)
            _nodeModel?.StartNode(jobId);

        _nodeModel?.EnsureNode(refineParent, jobId, jobId, "phase");
        if (startBatchIndex == 0)
            _nodeModel?.StartNode(refineParent);

        var promptTemplate = await _promptLoader.LoadAsync(request.PromptFile, _serviceWorkspaceRoot.RootPath, cancellationToken);
        var intermediateDir = request.SaveIntermediate && !string.IsNullOrEmpty(request.IntermediateDir)
            ? Path.Combine(artifactRoot, request.IntermediateDir.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : null;
        if (intermediateDir != null)
            Directory.CreateDirectory(intermediateDir);

        var threadsDir = RefinePaths.RefinerThreadsDir(artifactRoot);
        Directory.CreateDirectory(threadsDir);

        RefineDebugLog.Append(
            artifactRoot,
            $"Refine run jobId={jobId} batches={batches.Count} startBatch={startBatchIndex} resume={startBatchIndex > 0}");

        string? outputPathFull = null;
        IReadOnlyList<string>? prevCtx = previousContext;

        try
        {
            for (var i = startBatchIndex; i < batches.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batchInfo = batches[i];
                if (prevCtx != null && contextLines > 0)
                    batchInfo = new BatchInfo
                    {
                        Index = batchInfo.Index,
                        StartLine = batchInfo.StartLine,
                        EndLine = batchInfo.EndLine,
                        Lines = batchInfo.Lines,
                        Context = prevCtx.TakeLast(contextLines).ToList()
                    };

                var beforeText = string.Concat(batchInfo.Lines);
                var userMsg = RefinePromptComposer.BuildUserMessageContent(batchInfo, promptTemplate);
                var openAiPreview = RefinePromptComposer.BuildOpenAiRequestPreview(SystemPrompt, userMsg);
                const int maxOpenAiPreviewChars = 120_000;
                if (openAiPreview.Length > maxOpenAiPreviewChars)
                    openAiPreview = openAiPreview[..maxOpenAiPreviewChars] + "\n… [truncated]";
                _store.Update(jobId, new RefineJobStatusUpdate
                {
                    CurrentBatch = i + 1,
                    CurrentPhase = $"Batch {i + 1}/{batches.Count}",
                    BatchEventKind = "input_ready",
                    BatchEventIndex0 = i,
                    BatchThreadsRelativePath = RefineThreadBatchFile.RelativePath(i, batches.Count),
                    OpenAiRequestPreview = openAiPreview,
                    HasBatchBeforeText = true,
                    BatchBeforeText = beforeText,
                    HasBatchAfterText = true,
                    BatchAfterText = null,
                    RefinerLogLine = $"batch {i + 1}/{batches.Count} input_ready"
                });
                RefineThreadBatchFile.Write(threadsDir, i, batches.Count, beforeText, afterText: null);
                RefineDebugLog.Append(artifactRoot, $"Batch {i + 1}/{batches.Count}: input_ready — calling OpenAI");

                var batchNodeId = refineParent + ":batch-" + i;
                _nodeModel?.EnsureNode(batchNodeId, refineParent, jobId, "batch");
                _nodeModel?.StartNode(batchNodeId);

                var percent = batches.Count > 0 ? (i + 1) * 100 / batches.Count : 100;
                _nodeModel?.UpdateNodeProgress(jobId, percent, $"Batch {i + 1}/{batches.Count}");

                var result = await _client.RefineBatchAsync(batchInfo, request.Model, request.Temperature, SystemPrompt, promptTemplate, request.OpenAIBaseUrl, artifactRoot, cancellationToken);

                string afterText;
                if (result.Success)
                {
                    fixedContent.AddRange(result.FixedLines);
                    prevCtx = result.FixedLines;
                    afterText = string.Concat(result.FixedLines);
                    if (intermediateDir != null)
                    {
                        var json = JsonSerializer.Serialize(new { batchInfo.Index, result.Success, result.Error, fixed_lines = result.FixedLines.Select(l => l.TrimEnd()).ToList() });
                        await File.WriteAllTextAsync(Path.Combine(intermediateDir, $"batch_{i + 1:D4}_of_{batches.Count:D4}.json"), json, Encoding.UTF8, cancellationToken);
                    }
                }
                else
                {
                    afterText = string.Concat(batchInfo.Lines);
                    _logger?.LogWarning("Batch {Index} failed: {Error}", i, result.Error);
                }

                RefineThreadBatchFile.Write(threadsDir, i, batches.Count, beforeText, afterText);
                var outLineCount = result.Success ? result.FixedLines.Count : 0;
                RefineDebugLog.Append(
                    artifactRoot,
                    $"Batch {i + 1}/{batches.Count}: output_ready — success={result.Success}, outLines={outLineCount}");
                var relPath = RefineThreadBatchFile.RelativePath(i, batches.Count);
                _store.Update(jobId, new RefineJobStatusUpdate
                {
                    BatchEventKind = "output_ready",
                    BatchEventIndex0 = i,
                    BatchThreadsRelativePath = relPath,
                    HasBatchBeforeText = true,
                    BatchBeforeText = beforeText,
                    HasBatchAfterText = true,
                    BatchAfterText = afterText,
                    RefinerLogLine = $"batch {i + 1}/{batches.Count} output_ready success={result.Success}"
                });

                _nodeModel?.CompleteNode(batchNodeId, result.Success ? RefineJobState.Completed : RefineJobState.Failed, DateTimeOffset.UtcNow, result.Error);

                if (!result.Success)
                {
                    var err = string.IsNullOrEmpty(result.Error) ? "OpenAI batch failed" : result.Error;
                    _store.Update(jobId, new RefineJobStatusUpdate
                    {
                        State = RefineJobState.Failed,
                        ErrorMessage = err,
                        ProgressPercent = percent,
                        CurrentPhase = $"Failed at batch {i + 1}/{batches.Count}",
                        CurrentBatch = i + 1,
                        TotalBatches = batches.Count
                    });
                    _nodeModel?.CompleteNode(refineParent, RefineJobState.Failed, DateTimeOffset.UtcNow, err);
                    _nodeModel?.CompleteNode(jobId, RefineJobState.Failed, DateTimeOffset.UtcNow, err);
                    await FireCallbackAsync(jobId);
                    return;
                }

                if (_pause.IsPauseRequested(jobId))
                {
                    _pause.ClearPauseRequest(jobId);
                    SaveCheckpoint(jobId, artifactRoot, i + 1, batches.Count, fixedContent, prevCtx, headerLines, footerLines, contentLines, request);
                    _store.Update(jobId, new RefineJobStatusUpdate
                    {
                        State = RefineJobState.Paused,
                        CurrentPhase = $"Paused after batch {i + 1}/{batches.Count}",
                        ProgressPercent = percent
                    });
                    await FireCallbackAsync(jobId);
                    return;
                }
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

            TryDeleteCheckpoint(artifactRoot);

            _store.Update(jobId, new RefineJobStatusUpdate
            {
                State = RefineJobState.Completed,
                ProgressPercent = 100,
                CurrentBatch = batches.Count,
                TotalBatches = batches.Count,
                OutputFilePath = request.OutputFilePath
            });
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

    private static void SaveCheckpoint(
        string jobId,
        string artifactRoot,
        int nextBatchIndex,
        int totalBatches,
        List<string> fixedContent,
        IReadOnlyList<string>? previousContext,
        List<string> headerLines,
        List<string> footerLines,
        List<string> contentLines,
        RefineJobRequest request)
    {
        var ck = new RefineCheckpoint
        {
            JobId = jobId,
            NextBatchIndex = nextBatchIndex,
            TotalBatches = totalBatches,
            FixedLines = fixedContent.ToList(),
            PreviousContextLines = previousContext?.ToList(),
            HeaderLines = headerLines,
            FooterLines = footerLines,
            ContentLines = contentLines,
            Request = RefineJobRequestDto.FromModel(request)
        };
        ck.Save(RefinePaths.CheckpointFile(artifactRoot));
    }

    private static void TryDeleteCheckpoint(string artifactRoot)
    {
        try
        {
            var p = RefinePaths.CheckpointFile(artifactRoot);
            if (File.Exists(p))
                File.Delete(p);
        }
        catch
        {
            /* ignore */
        }
    }

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

internal static class RefinePaths
{
    public static string RefinerThreadsDir(string artifactRoot) => Path.Combine(artifactRoot, "refiner_threads");
    public static string CheckpointFile(string artifactRoot) => Path.Combine(RefinerThreadsDir(artifactRoot), "checkpoint.json");
}
