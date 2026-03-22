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
    private readonly ITranscriptionMerger _merger;
    private readonly IProjectArtifactService _artifacts;
    private readonly INodeModel? _nodeModel;
    private readonly ILogger<TranscriptionPipeline>? _logger;

    public TranscriptionPipeline(
        IAudioUtils audioUtils,
        ITranscriptionCache cache,
        ITranscriptionClient client,
        ITranscriptionMerger merger,
        IProjectArtifactService projectArtifacts,
        INodeModel? nodeModel = null,
        ILogger<TranscriptionPipeline>? logger = null)
    {
        _audioUtils = audioUtils;
        _cache = cache;
        _client = client;
        _merger = merger;
        _artifacts = projectArtifacts;
        _nodeModel = nodeModel;
        _logger = logger;
    }

    private void AppendTranscriptActivityForChunk(string? agentJobId, int chunkIndex, int? subChunkIndex, string message)
    {
        if (_nodeModel == null || string.IsNullOrEmpty(agentJobId) || chunkIndex < 0)
            return;
        var line = $"{DateTimeOffset.UtcNow:o} {message}";
        var nodeId = subChunkIndex is >= 0
            ? $"{agentJobId}:transcribe:chunk-{chunkIndex}:sub-{subChunkIndex.Value}"
            : $"{agentJobId}:transcribe:chunk-{chunkIndex}";
        _nodeModel.AppendTranscriptActivityLog(nodeId, line);
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

        _artifacts.InitializeJobMarkdownOutput(mdPath);
        _artifacts.ResetJobTranscriptionSpeakerMap();

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

        await TranscriptionWorkStateBootstrapper.TryBootstrapAsync(config, artifactRoot, baseName, _artifacts, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlySet<int>? effectiveFilter = chunkIndicesFilter;
        var pending = await _artifacts.TryLoadAndConsumePendingChunksAsync(artifactRoot, cancellationToken).ConfigureAwait(false);
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
        var cancellation = _artifacts.GetCancellationManager(jobId ?? "_pipeline", artifactRoot);
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
                await _artifacts.UpsertWorkStateChunkAsync(
                    artifactRoot,
                    TranscriptionWorkStateFile.SchemaVersionLatest,
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
                        _artifacts.SaveJobPerChunkTranscriptionJson(Path.GetFileName(chunkInfos[idx].Path), r.RawResponse, perChunkDirFull);
                    }

                    _artifacts.AppendJobMarkdownSegments(mdPath, r.Segments, r.Offset, r.EmitGuard);
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
        _artifacts.FinalizeJobMarkdownOutput(mdPath);
        var sortedResults = new List<TranscriptionResult>();
        for (var i = 0; i < totalChunks; i++)
        {
            if (chunkResults[i] != null)
                sortedResults.Add(chunkResults[i]!);
        }

        _artifacts.SaveJobCombinedTranscriptionJson(jsonPath, sortedResults);
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
            if (cancellation.ClearChunkCancelFlag(index))
            {
                _logger?.LogInformation(
                    "Removed stale cancel_{ChunkIndex}.flag before HTTP transcribe (under job .agent04_chunk_cancel); a previous cancel left it on disk.",
                    index);
            }

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
                stopPolling.Token,
                _logger);
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
            AppendTranscriptActivityForChunk(agentJobId, index, null, "Transcribe HTTP OK");
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
        CancellationToken stopPollingToken,
        ILogger? pollLogger = null)
    {
        try
        {
            while (!stopPollingToken.IsCancellationRequested)
            {
                await Task.Delay(200, stopPollingToken).ConfigureAwait(false);
                if (cancellation.IsCancelled(chunkIndex))
                {
                    pollLogger?.LogWarning(
                        "Chunk {ChunkIndex}: cancel_N.flag is set under job .agent04_chunk_cancel — cancelling in-flight HTTP transcribe (operator Cancel or stale file).",
                        chunkIndex);
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

    internal static async Task PollSubChunkCancelWhileTranscribingAsync(
        ICancellationManager cancellation,
        int parentChunkIndex,
        int subChunkIndex,
        CancellationTokenSource cancelTranscription,
        CancellationToken stopPollingToken,
        ILogger? pollLogger = null)
    {
        try
        {
            while (!stopPollingToken.IsCancellationRequested)
            {
                await Task.Delay(200, stopPollingToken).ConfigureAwait(false);
                if (cancellation.IsSubChunkCancelled(parentChunkIndex, subChunkIndex))
                {
                    pollLogger?.LogWarning(
                        "Sub-chunk parent={Parent} sub={Sub}: cancel_sub flag set — cancelling in-flight HTTP transcribe.",
                        parentChunkIndex,
                        subChunkIndex);
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

    /// <inheritdoc />
    public async Task TranscribeSplitSubChunkAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int parentChunkIndex,
        int subChunkIndex,
        int totalChunks,
        INodeModel? nodeModel,
        CancellationToken ct)
    {
        var subChunksDir = Path.Combine(artifactRoot, config.SplitChunksDir, $"chunk_{parentChunkIndex}", "sub_chunks");
        var audioPath = SplitChunkPaths.FindSubChunkAudioFile(subChunksDir, subChunkIndex);
        if (audioPath == null)
            throw new FileNotFoundException(
                $"No sub-chunk audio for parent {parentChunkIndex} sub {subChunkIndex} under {subChunksDir}");

        var resultsDir = Path.Combine(artifactRoot, config.SplitChunksDir, $"chunk_{parentChunkIndex}", "results");
        var transcribeParent = agentJobId + ":transcribe";
        var localKey = "chunk-" + parentChunkIndex + ":sub-" + subChunkIndex;
        var parallelConfigured = config.Get<int?>("parallel_transcription_workers") ?? 4;

        if (nodeModel != null)
        {
            EnsureAndStartTranscribePhase(nodeModel, agentJobId);
            EnterStep(nodeModel, agentJobId, transcribeParent, localKey, "chunk");
        }

        var started = DateTimeOffset.UtcNow;
        await _artifacts.UpsertWorkStateSubChunkAsync(
            artifactRoot,
            TranscriptionWorkStateFile.SchemaVersionLatest,
            totalChunks,
            parentChunkIndex,
            subChunkIndex,
            JobState.Running,
            started,
            null,
            null,
            ct).ConfigureAwait(false);

        var cancelMgr = _artifacts.GetCancellationManager(agentJobId, artifactRoot);
        if (cancelMgr.ClearSubChunkCancelFlag(parentChunkIndex, subChunkIndex))
        {
            _logger?.LogInformation(
                "Removed stale cancel_sub_{Parent}_{Sub}.flag before sub-chunk HTTP transcribe.",
                parentChunkIndex,
                subChunkIndex);
        }

        using var userCancelCts = new CancellationTokenSource();
        using var transcribeCts = CancellationTokenSource.CreateLinkedTokenSource(ct, userCancelCts.Token);
        using var stopPolling = new CancellationTokenSource();
        var pollTask = Task.Run(
            () => PollSubChunkCancelWhileTranscribingAsync(
                cancelMgr,
                parentChunkIndex,
                subChunkIndex,
                userCancelCts,
                stopPolling.Token,
                _logger),
            CancellationToken.None);

        try
        {
            var options = new TranscriptionClientOptions
            {
                Language = config.Language,
                Prompt = config.Get<string>("prompt"),
                Temperature = config.Temperature,
                ResponseFormat = config.Get<string>("response_format"),
                ChunkingStrategy = config.Get<string>("chunking_strategy"),
                ChunkIndex = parentChunkIndex,
                SubChunkIndex = subChunkIndex,
                AgentJobId = agentJobId,
                ParallelWorkersConfigured = parallelConfigured
            };
            var clientResult = await _client.TranscribeAsync(audioPath, options, transcribeCts.Token).ConfigureAwait(false);
            AppendTranscriptActivityForChunk(
                agentJobId,
                parentChunkIndex,
                subChunkIndex,
                "Transcribe HTTP OK (sub-chunk)");
            var tr = new TranscriptionResult(
                Path.GetFileName(audioPath),
                0,
                0,
                clientResult.Segments,
                clientResult.RawResponse);
            _artifacts.WriteSubChunkTranscriptionResult(resultsDir, subChunkIndex, tr);
            var completed = DateTimeOffset.UtcNow;
            if (nodeModel != null)
                CompleteStep(nodeModel, agentJobId, transcribeParent, localKey, JobState.Completed);
            await _artifacts.UpsertWorkStateSubChunkAsync(
                artifactRoot,
                TranscriptionWorkStateFile.SchemaVersionLatest,
                totalChunks,
                parentChunkIndex,
                subChunkIndex,
                JobState.Completed,
                started,
                completed,
                null,
                ct).ConfigureAwait(false);

            await SplitChunkMergeIntegrator.TryAfterSubChunkCompletedAsync(
                    config,
                    artifactRoot,
                    agentJobId,
                    parentChunkIndex,
                    totalChunks,
                    _merger,
                    _logger,
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested)
                throw;
            if (nodeModel != null)
                CompleteStep(nodeModel, agentJobId, transcribeParent, localKey, JobState.Cancelled);
            await _artifacts.UpsertWorkStateSubChunkAsync(
                artifactRoot,
                TranscriptionWorkStateFile.SchemaVersionLatest,
                totalChunks,
                parentChunkIndex,
                subChunkIndex,
                JobState.Cancelled,
                started,
                DateTimeOffset.UtcNow,
                null,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (nodeModel != null)
                CompleteStep(nodeModel, agentJobId, transcribeParent, localKey, JobState.Failed, ex.Message);
            await _artifacts.UpsertWorkStateSubChunkAsync(
                artifactRoot,
                TranscriptionWorkStateFile.SchemaVersionLatest,
                totalChunks,
                parentChunkIndex,
                subChunkIndex,
                JobState.Failed,
                started,
                DateTimeOffset.UtcNow,
                ex.Message,
                ct).ConfigureAwait(false);
            throw;
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
                /* ignore */
            }
        }
    }

    /// <inheritdoc />
    public async Task RetranscribeMainChunkAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int chunkIndex,
        INodeModel? nodeModel,
        CancellationToken ct,
        IJobStatusStore? statusStore = null)
    {
        if (config.Get<bool?>("save_per_chunk_json") != true)
            throw new InvalidOperationException("retranscribe requires save_per_chunk_json=true");

        var cacheDirFull = Path.Combine(artifactRoot, config.CacheDir);
        EnsureDirectoryFor(cacheDirFull);

        var chunksDir = Path.Combine(artifactRoot, "chunks");
        var indexMap = TranscriptionChunkOnDiskReader.MapPartIndexToAudioPath(chunksDir);

        ChunkInfo targetChunk;
        int total;
        string baseName;
        string manifestPath;

        if (indexMap.Count > 0)
        {
            if (!indexMap.TryGetValue(chunkIndex, out var diskAudioPath) || !File.Exists(diskAudioPath))
            {
                throw new InvalidOperationException(
                    $"chunks/ has split audio but no file for main chunk index {chunkIndex} (expected name containing _part_{chunkIndex}).");
            }

            var inferredTotal = indexMap.Keys.Max() + 1;
            var workState = await _artifacts.TryLoadWorkStateAsync(artifactRoot, ct).ConfigureAwait(false);
            total = workState?.TotalChunks is { } ws && ws > 0
                ? Math.Max(ws, inferredTotal)
                : inferredTotal;
            if (chunkIndex < 0 || chunkIndex >= total)
                throw new ArgumentOutOfRangeException(nameof(chunkIndex));

            targetChunk = new ChunkInfo(diskAudioPath, 0, 0);
            var chunkFileName = Path.GetFileName(diskAudioPath);
            baseName = TranscriptionChunkOnDiskReader.ManifestStemFromChunkFileName(chunkFileName);
            var preferredManifest = Path.Combine(cacheDirFull, baseName + ".manifest.json");
            manifestPath = TranscriptionChunkOnDiskReader.TryResolveManifestJsonPath(artifactRoot, config.CacheDir, baseName)
                             ?? preferredManifest;
            if (Directory.Exists(cacheDirFull))
            {
                string[] manifests;
                try
                {
                    manifests = Directory.GetFiles(cacheDirFull, "*.manifest.json");
                }
                catch
                {
                    manifests = Array.Empty<string>();
                }

                if (manifests.Length > 1 && !File.Exists(preferredManifest))
                {
                    _logger?.LogWarning(
                        "Several .manifest.json files in cache and none named {Expected}; using {Chosen} for retranscribe.",
                        Path.GetFileName(preferredManifest),
                        Path.GetFileName(manifestPath));
                }
            }
        }
        else
        {
            var files = config.GetFiles();
            if (files.Count == 0)
                throw new InvalidOperationException("config has no input file entry and chunks/ has no split audio");
            var rel = files[0].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var inputPath = Path.Combine(artifactRoot, rel);
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input audio not found under artifact root", inputPath);

            var workingPath = await ConvertToWavIfNeededAsync(config, inputPath, artifactRoot, ct).ConfigureAwait(false);
            baseName = Path.GetFileNameWithoutExtension(Path.GetFileName(inputPath));
            var splitWorkdirFull = Path.Combine(artifactRoot, config.SplitWorkdir);
            var chunkInfos = await PrepareChunksAsync(config, workingPath, splitWorkdirFull, ct).ConfigureAwait(false);
            total = chunkInfos.Count;
            if (chunkIndex < 0 || chunkIndex >= total)
                throw new ArgumentOutOfRangeException(nameof(chunkIndex));
            targetChunk = chunkInfos[chunkIndex];
            manifestPath = Path.Combine(cacheDirFull, baseName + ".manifest.json");
        }

        statusStore?.Update(
            agentJobId,
            new JobStatusUpdate
            {
                State = JobState.Running,
                ProgressPercent = 0,
                CurrentPhase = "Retranscribing",
                TotalChunks = total,
                ProcessedChunks = 0
            });

        var manifest = await _cache.LoadManifestAsync(manifestPath, ct).ConfigureAwait(false);
        var targetBasename = Path.GetFileName(targetChunk.Path);
        manifest.Chunks.Remove(targetBasename);
        await _cache.SaveManifestAsync(manifestPath, manifest, ct).ConfigureAwait(false);

        var transcribeParent = agentJobId + ":transcribe";
        var localKey = "chunk-" + chunkIndex;
        if (nodeModel != null)
        {
            EnsureAndStartTranscribePhase(nodeModel, agentJobId);
            EnterStep(nodeModel, agentJobId, transcribeParent, localKey, "chunk");
        }

        var started = DateTimeOffset.UtcNow;
        await _artifacts.UpsertWorkStateChunkAsync(
            artifactRoot,
            TranscriptionWorkStateFile.SchemaVersionLatest,
            total,
            chunkIndex,
            JobState.Running,
            started,
            null,
            null,
            false,
            ct).ConfigureAwait(false);

        var cancellation = _artifacts.GetCancellationManager(agentJobId, artifactRoot);
        using var manifestGate = new SemaphoreSlim(1, 1);
        var parallelConfigured = config.Get<int?>("parallel_transcription_workers") ?? 4;

        TranscriptionResult newResult;
        try
        {
            newResult = await ProcessChunkAsync(
                config,
                targetChunk,
                chunkIndex,
                total,
                manifest,
                manifestPath,
                baseName,
                agentJobId,
                parallelConfigured,
                manifestGate,
                cancellation,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (nodeModel != null)
                CompleteStep(nodeModel, agentJobId, transcribeParent, localKey, JobState.Failed, ex.Message);
            await _artifacts.UpsertWorkStateChunkAsync(
                artifactRoot,
                TranscriptionWorkStateFile.SchemaVersionLatest,
                total,
                chunkIndex,
                JobState.Failed,
                started,
                DateTimeOffset.UtcNow,
                ex.Message,
                false,
                ct).ConfigureAwait(false);
            statusStore?.Update(
                agentJobId,
                new JobStatusUpdate
                {
                    State = JobState.Completed,
                    CurrentPhase = "",
                    TotalChunks = total,
                    ProcessedChunks = total
                });
            throw;
        }

        var completedAt = DateTimeOffset.UtcNow;
        if (nodeModel != null)
            CompleteStep(nodeModel, agentJobId, transcribeParent, localKey, JobState.Completed);
        await _artifacts.UpsertWorkStateChunkAsync(
            artifactRoot,
            TranscriptionWorkStateFile.SchemaVersionLatest,
            total,
            chunkIndex,
            JobState.Completed,
            started,
            completedAt,
            null,
            false,
            ct).ConfigureAwait(false);

        var perChunkRel = config.Get<string>("per_chunk_json_dir") ?? "chunks_json";
        var perChunkDirFull = Path.Combine(artifactRoot, perChunkRel);
        _artifacts.SaveJobPerChunkTranscriptionJson(Path.GetFileName(targetChunk.Path), newResult.RawResponse, perChunkDirFull);

        _logger?.LogInformation(
            "Retranscribed main chunk {Index}; updated per-chunk JSON under {Dir}",
            chunkIndex,
            perChunkDirFull);

        statusStore?.Update(
            agentJobId,
            new JobStatusUpdate
            {
                State = JobState.Completed,
                CurrentPhase = "",
                TotalChunks = total,
                ProcessedChunks = total
            });
    }

    /// <inheritdoc />
    public async Task RebuildCombinedOutputsFromPerChunkJsonAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        INodeModel? nodeModel,
        CancellationToken ct)
    {
        _ = nodeModel;
        if (config.Get<bool?>("save_per_chunk_json") != true)
            throw new InvalidOperationException("rebuild_combined requires save_per_chunk_json=true");

        var baseName = ResolveCombinedOutputBaseName(config, artifactRoot);
        var mdRel = ResolveOutputPattern(config.MdOutputPath, baseName, agentJobId);
        var jsonRel = ResolveOutputPattern(config.RawJsonOutputPath, baseName, agentJobId);
        var mdPath = Path.Combine(artifactRoot, mdRel);
        var jsonPath = Path.Combine(artifactRoot, jsonRel);
        EnsureDirectoryFor(mdPath);
        EnsureDirectoryFor(jsonPath);

        var chunksDir = Path.Combine(artifactRoot, "chunks");
        var indexMap = TranscriptionChunkOnDiskReader.MapPartIndexToAudioPath(chunksDir);
        IReadOnlyList<ChunkInfo> chunkInfos;
        int total;

        if (indexMap.Count > 0)
        {
            var inferredTotal = indexMap.Keys.Max() + 1;
            var workState = await _artifacts.TryLoadWorkStateAsync(artifactRoot, ct).ConfigureAwait(false);
            total = workState?.TotalChunks is int ws && ws > 0 ? Math.Max(ws, inferredTotal) : inferredTotal;
            var list = new List<ChunkInfo>(total);
            for (var i = 0; i < total; i++)
            {
                if (!indexMap.TryGetValue(i, out var p))
                    throw new InvalidOperationException($"rebuild_combined: missing audio under chunks/ for index {i}.");
                list.Add(new ChunkInfo(p, 0, 0));
            }

            chunkInfos = list;
        }
        else
        {
            var files = config.GetFiles();
            if (files.Count == 0)
                throw new InvalidOperationException("rebuild_combined: config has no input file and chunks/ has no split audio.");
            var rel = files[0].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var inputPath = Path.Combine(artifactRoot, rel);
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("rebuild_combined: input audio not found", inputPath);
            var workingPath = await ConvertToWavIfNeededAsync(config, inputPath, artifactRoot, ct).ConfigureAwait(false);
            var splitWorkdirFull = Path.Combine(artifactRoot, config.SplitWorkdir);
            var list = await PrepareChunksAsync(config, workingPath, splitWorkdirFull, ct).ConfigureAwait(false);
            chunkInfos = list;
            total = list.Count;
        }

        var merged = new List<TranscriptionResult>();
        for (var i = 0; i < total; i++)
        {
            var loaded = await TryLoadPerChunkTranscriptionResultAsync(config, artifactRoot, chunkInfos[i], ct)
                .ConfigureAwait(false);
            if (loaded == null)
                throw new InvalidOperationException($"rebuild_combined: missing or invalid chunks_json for chunk {i}.");
            merged.Add(loaded);
        }

        _artifacts.InitializeJobMarkdownOutput(mdPath);
        _artifacts.ResetJobTranscriptionSpeakerMap();
        foreach (var r in merged)
            _artifacts.AppendJobMarkdownSegments(mdPath, r.Segments, r.Offset, r.EmitGuard);
        _artifacts.FinalizeJobMarkdownOutput(mdPath);
        _artifacts.SaveJobCombinedTranscriptionJson(jsonPath, merged);
        _logger?.LogInformation("rebuild_combined: wrote {Md} and {Json}", mdPath, jsonPath);
    }

    private static string ResolveCombinedOutputBaseName(TranscriptionConfig config, string artifactRoot)
    {
        var files = config.GetFiles();
        if (files.Count > 0)
        {
            var rel = files[0].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var inputPath = Path.Combine(artifactRoot, rel);
            if (File.Exists(inputPath))
                return Path.GetFileNameWithoutExtension(Path.GetFileName(inputPath));
        }

        var map = TranscriptionChunkOnDiskReader.MapPartIndexToAudioPath(Path.Combine(artifactRoot, "chunks"));
        if (map.TryGetValue(0, out var p0))
            return TranscriptionChunkOnDiskReader.ManifestStemFromChunkFileName(Path.GetFileName(p0));

        throw new InvalidOperationException(
            "rebuild_combined: cannot infer output base name (config input missing under job root and no chunks/_part_* audio).");
    }

    private async Task<TranscriptionResult?> TryLoadPerChunkTranscriptionResultAsync(
        TranscriptionConfig config,
        string artifactRoot,
        ChunkInfo chunkInfo,
        CancellationToken ct)
    {
        var perChunkRel = config.Get<string>("per_chunk_json_dir") ?? "chunks_json";
        var jsonPath = Path.Combine(
            artifactRoot,
            perChunkRel,
            Path.GetFileNameWithoutExtension(chunkInfo.Path) + ".json");
        if (!File.Exists(jsonPath))
            return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(jsonPath, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(bytes);
            var dict = ToDictionary(doc.RootElement);
            var segments = OpenAITranscriptionClient.ParseSegments(dict);
            return new TranscriptionResult(
                Path.GetFileName(chunkInfo.Path),
                chunkInfo.Offset,
                chunkInfo.EmitGuard,
                segments,
                dict);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not load per-chunk JSON for {Path}", jsonPath);
            return null;
        }
    }
}
