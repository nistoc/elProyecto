using System.Text.Json;
using System.Threading;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class TranscriptionPipeline : ITranscriptionPipeline
{
    [XRayNode(XRayNodeOperation.Ensure)]
    [XRayNode(XRayNodeOperation.Start)]
    private static void EnterStep(INodeModel nodeModel, string jobId, string parentNodeId, string localKey, string kind)
    {
        var nodeId = string.IsNullOrEmpty(parentNodeId) ? jobId : parentNodeId + ":" + localKey;
        nodeModel.EnsureNode(nodeId, string.IsNullOrEmpty(parentNodeId) ? null : parentNodeId, jobId, kind);
        nodeModel.StartNode(nodeId);
    }

    [XRayNode(XRayNodeOperation.Complete)]
    private static void CompleteStep(INodeModel nodeModel, string jobId, string parentNodeId, string localKey, JobState status, string? error = null)
    {
        var nodeId = string.IsNullOrEmpty(parentNodeId) ? jobId : parentNodeId + ":" + localKey;
        nodeModel.CompleteNode(nodeId, status, DateTimeOffset.UtcNow, error);
    }

    [XRayNode(XRayNodeOperation.Ensure)]
    [XRayNode(XRayNodeOperation.Start)]
    private static void EnsureAndStartJobRoot(INodeModel nodeModel, string jobId)
    {
        nodeModel.EnsureNode(jobId, null, jobId, "job");
        nodeModel.StartNode(jobId);
    }

    [XRayNode(XRayNodeOperation.Ensure)]
    [XRayNode(XRayNodeOperation.Start)]
    private static void EnsureAndStartTranscribePhase(INodeModel nodeModel, string jobId)
    {
        var transcribeParent = jobId + ":transcribe";
        nodeModel.EnsureNode(transcribeParent, jobId, jobId, "phase");
        nodeModel.StartNode(transcribeParent);
    }

    [XRayNode(XRayNodeOperation.Complete)]
    private static void CompleteTranscribePhase(INodeModel nodeModel, string jobId)
    {
        nodeModel.CompleteNode(jobId + ":transcribe", JobState.Completed, DateTimeOffset.UtcNow);
    }

    [XRayNode(XRayNodeOperation.Ensure)]
    [XRayNode(XRayNodeOperation.Complete)]
    private static void EnsureJobRootWithMetadataAndComplete(INodeModel nodeModel, string jobId, string mdPath, string jsonPath)
    {
        nodeModel.EnsureNode(jobId, null, jobId, "job", new Dictionary<string, object?>
        {
            ["md_output_path"] = mdPath,
            ["json_output_path"] = jsonPath
        });
        nodeModel.CompleteNode(jobId, JobState.Completed, DateTimeOffset.UtcNow);
    }

    [XRayNode(XRayNodeOperation.Complete)]
    private static void CompleteJobFailed(INodeModel nodeModel, string jobId, string? errorMessage)
    {
        nodeModel.CompleteNode(jobId, JobState.Failed, DateTimeOffset.UtcNow, errorMessage);
    }

    private readonly IAudioUtils _audioUtils;
    private readonly ITranscriptionCache _cache;
    private readonly ITranscriptionClient _client;
    private readonly ITranscriptionOutputWriter _output;
    private readonly ITranscriptionMerger _merger;
    private readonly ICancellationManagerFactory _cancellationFactory;
    private readonly ILogger<TranscriptionPipeline>? _logger;

    public TranscriptionPipeline(
        IAudioUtils audioUtils,
        ITranscriptionCache cache,
        ITranscriptionClient client,
        ITranscriptionOutputWriter output,
        ITranscriptionMerger merger,
        ICancellationManagerFactory cancellationFactory,
        ILogger<TranscriptionPipeline>? logger = null)
    {
        _audioUtils = audioUtils;
        _cache = cache;
        _client = client;
        _output = output;
        _merger = merger;
        _cancellationFactory = cancellationFactory;
        _logger = logger;
    }

    public async Task<(string MdPath, string JsonPath)> ProcessFileAsync(
        TranscriptionConfig config,
        string inputFilePath,
        string workspaceRoot,
        string? jobId = null,
        IJobStatusStore? statusStore = null,
        INodeModel? nodeModel = null,
        CancellationToken cancellationToken = default,
        IReadOnlySet<int>? chunkIndicesFilter = null)
    {
        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException("Audio file not found", inputFilePath);
        var workspaceNorm = Path.GetFullPath(workspaceRoot);
        var artifactRoot = TranscriptionPaths.ResolveArtifactRoot(workspaceNorm, inputFilePath);

        void UpdateProgress(JobState state, int percent, string? phase, int? totalChunks = null, int? processedChunks = null, string? mdPath = null, string? jsonPath = null, string? error = null)
        {
            if (jobId == null || statusStore == null) return;
            statusStore.Update(jobId, new JobStatusUpdate
            {
                State = state,
                ProgressPercent = percent,
                CurrentPhase = phase,
                TotalChunks = totalChunks,
                ProcessedChunks = processedChunks,
                MdOutputPath = mdPath,
                JsonOutputPath = jsonPath,
                ErrorMessage = error
            });
            if (jobId != null && nodeModel != null)
                nodeModel.UpdateNodeProgress(jobId, percent, phase);
        }

        void StepStart(string parentNodeId, string localKey, string kind)
        {
            if (jobId != null && nodeModel != null)
                EnterStep(nodeModel, jobId, parentNodeId, localKey, kind);
        }

        void StepComplete(string parentNodeId, string localKey, JobState status, string? error = null)
        {
            if (jobId != null && nodeModel != null)
                CompleteStep(nodeModel, jobId, parentNodeId, localKey, status, error);
        }

        _logger?.LogInformation("[FILE] {Path}", inputFilePath);
        UpdateProgress(JobState.Running, 0, "Starting", null, null);
        if (jobId != null && nodeModel != null)
            EnsureAndStartJobRoot(nodeModel, jobId);

        try
        {
        var workingPath = await ConvertToWavIfNeededAsync(config, inputFilePath, artifactRoot, cancellationToken);
        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName(inputFilePath));
        var mdRel = ResolveOutputPattern(config.MdOutputPath, baseName, jobId);
        var jsonRel = ResolveOutputPattern(config.RawJsonOutputPath, baseName, jobId);
        var mdPath = Path.Combine(artifactRoot, mdRel);
        var jsonPath = Path.Combine(artifactRoot, jsonRel);
        EnsureDirectoryFor(mdPath);
        EnsureDirectoryFor(jsonPath);

        _output.InitializeMarkdown(mdPath);
        _output.ResetSpeakerMap();

        var cacheDirFull = Path.Combine(artifactRoot, config.CacheDir);
        EnsureDirectoryFor(cacheDirFull);
        var manifestPath = Path.Combine(cacheDirFull, baseName + ".manifest.json");
        var manifest = await _cache.LoadManifestAsync(manifestPath, cancellationToken);

        StepStart(jobId ?? "", "chunking", "phase");
        var splitWorkdirFull = Path.Combine(artifactRoot, config.SplitWorkdir);
        var chunkInfos = await PrepareChunksAsync(config, workingPath, splitWorkdirFull, cancellationToken);
        StepComplete(jobId ?? "", "chunking", JobState.Completed);
        _logger?.LogInformation("Processing {Count} chunk(s)", chunkInfos.Count);
        UpdateProgress(JobState.Running, 5, "Chunking", chunkInfos.Count, 0, null, null, null);

        await TranscriptionWorkStateBootstrapper.TryBootstrapAsync(config, artifactRoot, baseName, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<int>? effectiveFilter = chunkIndicesFilter;
        var pending = await PendingChunksReader.TryLoadAndConsumeAsync(artifactRoot, cancellationToken).ConfigureAwait(false);
        if (pending != null && pending.Count > 0)
        {
            if (effectiveFilter == null)
                effectiveFilter = pending;
            else
                effectiveFilter = new HashSet<int>(effectiveFilter.Where(pending.Contains));
        }

        var transcribeParent = jobId != null ? jobId + ":transcribe" : "";
        if (jobId != null && nodeModel != null)
            EnsureAndStartTranscribePhase(nodeModel, jobId);

        var progress = new ChunkProgress(chunkInfos.Count, config.Get<string>("progress_time_format") ?? "HH:MM:SS.M");
        var totalChunks = chunkInfos.Count;
        var cancellation = _cancellationFactory.Get(jobId ?? "_pipeline", artifactRoot);
        var parallelConfigured = config.Get<int?>("parallel_transcription_workers") ?? 4;
        var parallel = Math.Clamp(parallelConfigured, 1, 64);
        if (parallel != parallelConfigured)
            _logger?.LogWarning("parallel_transcription_workers={Raw} clamped to {Clamped}", parallelConfigured, parallel);
        if (parallel > 32)
            _logger?.LogWarning("parallel_transcription_workers={W} is high; expect more 429/rate-limit risk", parallel);

        using var manifestGate = new SemaphoreSlim(1, 1);
        using var stateGate = new SemaphoreSlim(1, 1);
        var stepLock = new object();
        var mdLock = new object();

        var chunkResolved = new bool[totalChunks];
        var chunkResults = new TranscriptionResult?[totalChunks];
        var nextAppendIndex = 0;
        var failedLocalCount = 0;
        var successCount = 0;

        async Task PersistChunkAsync(int idx, JobState state, DateTimeOffset? started, DateTimeOffset? completed, string? err)
        {
            await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await TranscriptionWorkStateFile.UpsertChunkAsync(
                    artifactRoot,
                    1,
                    totalChunks,
                    idx,
                    state,
                    started,
                    completed,
                    err,
                    false,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                stateGate.Release();
            }
        }

        void TryAppendMarkdownInOrder()
        {
            while (nextAppendIndex < totalChunks && chunkResolved[nextAppendIndex])
            {
                var r = chunkResults[nextAppendIndex];
                if (r != null)
                {
                    var idx = nextAppendIndex;
                    if (config.Get<bool?>("save_per_chunk_json") == true)
                    {
                        var perChunkRel = config.Get<string>("per_chunk_json_dir") ?? "chunks_json";
                        var perChunkDirFull = Path.Combine(artifactRoot, perChunkRel);
                        _output.SavePerChunkJson(Path.GetFileName(chunkInfos[idx].Path), r.RawResponse, perChunkDirFull);
                    }

                    _output.AppendSegmentsToMarkdown(mdPath, r.Segments, r.Offset, r.EmitGuard);
                }

                nextAppendIndex++;
            }
        }

        void MarkChunkResolved(int idx, TranscriptionResult? result, bool success)
        {
            lock (mdLock)
            {
                chunkResolved[idx] = true;
                if (success && result != null)
                    chunkResults[idx] = result;
                TryAppendMarkdownInOrder();
            }
        }

        var workIndices = new List<int>();
        for (var i = 0; i < totalChunks; i++)
        {
            if (effectiveFilter != null && !effectiveFilter.Contains(i))
                continue;
            if (cancellation.IsCancelled(i))
            {
                lock (stepLock)
                {
                    StepStart(transcribeParent, "chunk-" + i, "chunk");
                    StepComplete(transcribeParent, "chunk-" + i, JobState.Cancelled, "Cancelled");
                    progress.MarkCancelled(i);
                    progress.Update();
                }

                await PersistChunkAsync(i, JobState.Cancelled, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null).ConfigureAwait(false);
                MarkChunkResolved(i, null, false);
                continue;
            }

            workIndices.Add(i);
        }

        if (workIndices.Count > 0)
            _logger?.LogInformation(
                "Transcription sliding window: parallel={Parallel} firstIndex={First} workCount={Count}",
                parallel,
                workIndices[0],
                workIndices.Count);

        if (totalChunks > 0 && jobId != null && statusStore != null)
            UpdateProgress(JobState.Running, 8, "Transcribing", totalChunks, 0, null, null, null);

        using var globalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await Parallel.ForEachAsync(
            workIndices,
            new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = globalCts.Token },
            async (idx, _) =>
            {
                DateTimeOffset? chunkStartedAt = null;
                lock (stepLock)
                {
                    StepStart(transcribeParent, "chunk-" + idx, "chunk");
                    progress.MarkStarted(idx);
                    progress.Update();
                    chunkStartedAt = DateTimeOffset.UtcNow;
                }

                await PersistChunkAsync(idx, JobState.Running, chunkStartedAt, null, null).ConfigureAwait(false);

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, globalCts.Token);
                try
                {
                    var result = await ProcessChunkAsync(
                        config,
                        chunkInfos[idx],
                        idx,
                        totalChunks,
                        manifest,
                        manifestPath,
                        baseName,
                        jobId,
                        parallel,
                        manifestGate,
                        cancellation,
                        linked.Token).ConfigureAwait(false);

                    lock (stepLock)
                    {
                        StepComplete(transcribeParent, "chunk-" + idx, JobState.Completed);
                        progress.MarkCompleted(idx);
                        progress.Update();
                        var sc = Interlocked.Increment(ref successCount);
                        var pct = totalChunks > 0 ? 10 + (70 * sc / totalChunks) : 80;
                        UpdateProgress(
                            JobState.Running,
                            pct,
                            $"Transcribing chunk {sc}/{totalChunks}",
                            totalChunks,
                            sc,
                            null,
                            null,
                            null);
                    }

                    var completedAt = DateTimeOffset.UtcNow;
                    await PersistChunkAsync(idx, JobState.Completed, chunkStartedAt, completedAt, null).ConfigureAwait(false);
                    MarkChunkResolved(idx, result, true);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw;
                    if (globalCts.Token.IsCancellationRequested)
                        throw;

                    lock (stepLock)
                    {
                        StepComplete(transcribeParent, "chunk-" + idx, JobState.Cancelled, "Cancelled");
                        progress.MarkCancelled(idx);
                        progress.Update();
                    }

                    await PersistChunkAsync(idx, JobState.Cancelled, chunkStartedAt, DateTimeOffset.UtcNow, null).ConfigureAwait(false);
                    MarkChunkResolved(idx, null, false);
                }
                catch (Exception ex)
                {
                    if (TranscriptionWorkflowAbort.ShouldAbortWholeJob(ex))
                    {
                        globalCts.Cancel();
                        lock (stepLock)
                        {
                            StepComplete(transcribeParent, "chunk-" + idx, JobState.Failed, ex.Message);
                            progress.MarkCompleted(idx);
                            progress.Update();
                        }

                        await PersistChunkAsync(idx, JobState.Failed, chunkStartedAt, DateTimeOffset.UtcNow, ex.Message).ConfigureAwait(false);
                        throw;
                    }

                    lock (stepLock)
                    {
                        StepComplete(transcribeParent, "chunk-" + idx, JobState.Failed, ex.Message);
                        progress.MarkCompleted(idx);
                        progress.Update();
                        Interlocked.Increment(ref failedLocalCount);
                    }

                    _logger?.LogWarning(ex, "Chunk {Index} failed (other chunks continue)", idx + 1);
                    await PersistChunkAsync(idx, JobState.Failed, chunkStartedAt, DateTimeOffset.UtcNow, ex.Message).ConfigureAwait(false);
                    MarkChunkResolved(idx, null, false);
                }
            }).ConfigureAwait(false);

        if (failedLocalCount > 0)
            throw new InvalidOperationException($"{failedLocalCount} chunk(s) failed (see per-chunk status / logs).");

        if (jobId != null && nodeModel != null)
            CompleteTranscribePhase(nodeModel, jobId);
        progress.Complete();

        StepStart(jobId ?? "", "merge", "phase");
        UpdateProgress(JobState.Running, 90, "Merging", totalChunks, totalChunks, null, null, null);
        _output.FinalizeMarkdown(mdPath);
        var sortedResults = new List<TranscriptionResult>();
        for (var i = 0; i < totalChunks; i++)
        {
            if (chunkResults[i] != null)
                sortedResults.Add(chunkResults[i]!);
        }

        _output.SaveCombinedJson(jsonPath, sortedResults);
        StepComplete(jobId ?? "", "merge", JobState.Completed);

        UpdateProgress(JobState.Completed, 100, "Completed", totalChunks, totalChunks, mdPath, jsonPath, null);
        if (jobId != null && nodeModel != null)
            EnsureJobRootWithMetadataAndComplete(nodeModel, jobId, mdPath, jsonPath);
        _logger?.LogInformation("Done. Markdown: {Md}, JSON: {Json}", mdPath, jsonPath);
        return (mdPath, jsonPath);
        }
        catch (Exception ex)
        {
            if (jobId != null && nodeModel != null)
                CompleteJobFailed(nodeModel, jobId, ex.Message);
            UpdateProgress(JobState.Failed, 0, "Failed", null, null, null, null, ex.Message);
            throw;
        }
    }

    private static string ResolveOutputPattern(string pattern, string baseName, string? jobId)
    {
        var s = pattern.Replace("{base}", baseName, StringComparison.OrdinalIgnoreCase)
            .Replace("{jobId}", jobId ?? "", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(jobId) && !pattern.Contains("{jobId}", StringComparison.OrdinalIgnoreCase))
            s = Path.Combine(Path.GetDirectoryName(s) ?? ".", Path.GetFileNameWithoutExtension(s) + "_" + jobId + Path.GetExtension(s));
        return s;
    }

    private static void EnsureDirectoryFor(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private async Task<string> ConvertToWavIfNeededAsync(TranscriptionConfig config, string filePath, string workspaceRoot, CancellationToken ct)
    {
        if (config.Get<bool?>("convert_to_wav") != true)
            return filePath;
        if (!filePath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
            return filePath;
        var ffmpeg = _audioUtils.WhichOr(config.FfmpegPath, "ffmpeg") ?? "ffmpeg";
        var wavRel = config.Get<string>("wav_output_dir") ?? "converted_wav";
        var wavDir = Path.Combine(workspaceRoot, wavRel);
        return await Task.Run(() => _audioUtils.ConvertToWav(ffmpeg, filePath, wavDir), ct);
    }

    private async Task<IReadOnlyList<ChunkInfo>> PrepareChunksAsync(TranscriptionConfig config, string filePath, string splitWorkdirFull, CancellationToken ct)
    {
        if (config.Get<bool?>("pre_split") != true)
            return new[] { new ChunkInfo(filePath, 0.0, 0.0) };

        var ffmpeg = _audioUtils.WhichOr(config.FfmpegPath, "ffmpeg") ?? "ffmpeg";
        var ffprobe = _audioUtils.WhichOr(config.FfprobePath, "ffprobe") ?? "ffprobe";
        var chunker = new AudioChunker(_audioUtils, ffmpeg, ffprobe);

        return await chunker.ProcessChunksForFileAsync(
            filePath,
            (float)(config.Get<double?>("target_chunk_mb") ?? 5),
            splitWorkdirFull,
            config.Get<string>("chunk_naming") ?? "{base}_part_%03d.m4a",
            (float)(config.Get<double?>("chunk_overlap_sec") ?? 1),
            config.Get<bool?>("reencode_if_needed") ?? true,
            config.Get<int?>("reencode_bitrate_kbps") ?? 256,
            (float)(config.Get<double?>("max_duration_minutes") ?? 0),
            ct);
    }

    private async Task<TranscriptionResult> ProcessChunkAsync(
        TranscriptionConfig config,
        ChunkInfo chunkInfo,
        int index,
        int total,
        TranscriptionManifest manifest,
        string manifestPath,
        string baseName,
        string? agentJobId,
        int parallelWorkersConfigured,
        SemaphoreSlim? manifestGate,
        ICancellationManager cancellation,
        CancellationToken ct)
    {
        var chunkBasename = Path.GetFileName(chunkInfo.Path);
        if (index == 0)
            _logger?.LogInformation(
                "ProcessChunkAsync first chunk index=0/{Total} file={File} parallelWorkers={Workers}",
                total,
                chunkBasename,
                parallelWorkersConfigured);

        var fingerprint = await _cache.GetFileFingerprintAsync(chunkInfo.Path, ct);

        object? rawResponse;
        if (manifestGate != null)
            await manifestGate.WaitAsync(ct);
        try
        {
            rawResponse = _cache.GetCachedResponse(manifest, chunkBasename, fingerprint);
        }
        finally
        {
            manifestGate?.Release();
        }

        if (rawResponse == null)
        {
            var options = new TranscriptionClientOptions
            {
                Language = config.Language,
                Prompt = config.Get<string>("prompt"),
                Temperature = config.Temperature,
                ResponseFormat = config.Get<string>("response_format"),
                ChunkingStrategy = config.Get<string>("chunking_strategy"),
                ChunkIndex = index,
                AgentJobId = agentJobId,
                ParallelWorkersConfigured = parallelWorkersConfigured
            };
            using var transcribeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var stopPolling = new CancellationTokenSource();
            var pollTask = PollChunkCancelWhileTranscribingAsync(
                cancellation,
                index,
                transcribeCts,
                stopPolling.Token);
            TranscriptionClientResult result;
            try
            {
                result = await _client.TranscribeAsync(chunkInfo.Path, options, transcribeCts.Token);
            }
            finally
            {
                stopPolling.Cancel();
                try
                {
                    await pollTask.ConfigureAwait(false);
                }
                catch
                {
                    /* poll loop ended with OCE */
                }
            }

            rawResponse = result.RawResponse;
            if (manifestGate != null)
                await manifestGate.WaitAsync(ct);
            try
            {
                await _cache.CacheResponseAsync(manifestPath, manifest, chunkBasename, fingerprint, rawResponse, ct);
            }
            finally
            {
                manifestGate?.Release();
            }
        }
        else
        {
            _logger?.LogInformation("[CACHE] Using cached response for chunk {Index}", index);
        }

        var rawDict = ToDictionary(rawResponse);
        var segments = OpenAITranscriptionClient.ParseSegments(rawDict);
        return new TranscriptionResult(
            chunkBasename,
            chunkInfo.Offset,
            chunkInfo.EmitGuard,
            segments,
            rawDict);
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary(object? value)
    {
        if (value == null) return new Dictionary<string, object?>();
        if (value is IReadOnlyDictionary<string, object?> d) return d;
        if (value is JsonElement je)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var p in je.EnumerateObject())
                dict[p.Name] = ToObject(p.Value);
            return dict;
        }
        var json = JsonSerializer.Serialize(value, TranscriptionJsonSerializerOptions.Compact);
        using var doc = JsonDocument.Parse(json);
        return ToDictionary(doc.RootElement);
    }

    private static object? ToObject(JsonElement e)
    {
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetDouble(out var d) ? d : e.GetInt32(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => e.EnumerateArray().Select(ToObject).ToArray(),
            JsonValueKind.Object => e.EnumerateObject().ToDictionary(x => x.Name, x => ToObject(x.Value)),
            _ => null
        };
    }

    /// <summary>
    /// While transcription HTTP is in flight, poll chunk cancel flags and cancel the linked CTS so <see cref="HttpClient"/> aborts the request.
    /// </summary>
    internal static async Task PollChunkCancelWhileTranscribingAsync(
        ICancellationManager cancellation,
        int chunkIndex,
        CancellationTokenSource cancelTranscription,
        CancellationToken stopPollingToken)
    {
        try
        {
            while (!stopPollingToken.IsCancellationRequested)
            {
                await Task.Delay(200, stopPollingToken).ConfigureAwait(false);
                if (cancellation.IsCancelled(chunkIndex))
                {
                    cancelTranscription.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // stopPollingToken fired after transcription finished or job cancelled
        }
    }
}
