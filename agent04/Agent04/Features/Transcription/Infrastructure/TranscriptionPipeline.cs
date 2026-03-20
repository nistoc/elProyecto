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
        CancellationToken cancellationToken = default)
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

        var transcribeParent = jobId != null ? jobId + ":transcribe" : "";
        if (jobId != null && nodeModel != null)
            EnsureAndStartTranscribePhase(nodeModel, jobId);

        var results = new List<(int Index, TranscriptionResult Result)>();
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
        var stepLock = new object();

        for (var batchStart = 0; batchStart < totalChunks; batchStart += parallel)
        {
            var batchEnd = Math.Min(batchStart + parallel, totalChunks);
            var indices = new List<int>();
            for (var i = batchStart; i < batchEnd; i++)
            {
                if (cancellation.IsCancelled(i))
                {
                    progress.MarkCancelled(i);
                    progress.Update();
                    continue;
                }
                indices.Add(i);
            }

            if (indices.Count == 0)
                continue;

            if (batchStart == 0)
                _logger?.LogInformation(
                    "Transcription chunk batch: count={Count} parallel={Parallel} firstIndex={First}",
                    indices.Count, parallel, indices[0]);

            if (batchStart == 0 && jobId != null && statusStore != null)
                UpdateProgress(JobState.Running, 8, "Transcribing", totalChunks, 0, null, null, null);

            async Task<(int Idx, TranscriptionResult? Result, Exception? Error)> RunChunkAsync(int idx)
            {
                lock (stepLock)
                {
                    StepStart(transcribeParent, "chunk-" + idx, "chunk");
                    progress.MarkStarted(idx);
                    progress.Update();
                }

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
                        cancellationToken);
                    return (idx, result, null);
                }
                catch (OperationCanceledException oce) when (!cancellationToken.IsCancellationRequested)
                {
                    lock (stepLock)
                    {
                        StepComplete(transcribeParent, "chunk-" + idx, JobState.Cancelled, "Cancelled");
                        progress.MarkCancelled(idx);
                        progress.Update();
                    }

                    _logger?.LogInformation("Chunk {Index} transcription cancelled (operator)", idx + 1);
                    return (idx, null, oce);
                }
                catch (Exception ex)
                {
                    lock (stepLock)
                    {
                        StepComplete(transcribeParent, "chunk-" + idx, JobState.Failed, ex.Message);
                        progress.MarkCompleted(idx);
                        progress.Update();
                    }

                    _logger?.LogWarning(ex, "Chunk {Index} failed", idx + 1);
                    return (idx, null, ex);
                }
            }

            var outcomes = await Task.WhenAll(indices.Select(RunChunkAsync));
            var failure = Array.Find(outcomes, o => o.Error != null && o.Error is not OperationCanceledException);
            if (failure.Error != null)
            {
                _logger?.LogWarning(
                    "Batch transcription failed: fatalChunkIndex0={Idx0} fatalChunkDisplay1={Idx1} errorType={ErrType} message={ErrMsg}",
                    failure.Idx,
                    failure.Idx + 1,
                    failure.Error.GetType().Name,
                    failure.Error.Message);
                foreach (var o in outcomes.Where(x => x.Error == null))
                {
                    lock (stepLock)
                    {
                        StepComplete(
                            transcribeParent,
                            "chunk-" + o.Idx,
                            JobState.Failed,
                            "Aborted: another chunk in the same parallel batch failed");
                        progress.MarkCompleted(o.Idx);
                        progress.Update();
                    }
                }

                throw failure.Error;
            }

            foreach (var o in outcomes.Where(x => x.Result != null).OrderBy(x => x.Idx))
            {
                var idx = o.Idx;
                var result = o.Result!;
                lock (stepLock)
                {
                    results.Add((idx, result));
                    progress.MarkCompleted(idx);
                    StepComplete(transcribeParent, "chunk-" + idx, JobState.Completed);
                    progress.Update();
                    var processedCount = results.Count;
                    var pct = totalChunks > 0 ? 10 + (70 * processedCount / totalChunks) : 80;
                    UpdateProgress(
                        JobState.Running,
                        pct,
                        $"Transcribing chunk {processedCount}/{totalChunks}",
                        totalChunks,
                        processedCount,
                        null,
                        null,
                        null);
                }

                if (config.Get<bool?>("save_per_chunk_json") == true)
                {
                    var perChunkRel = config.Get<string>("per_chunk_json_dir") ?? "chunks_json";
                    var perChunkDirFull = Path.Combine(artifactRoot, perChunkRel);
                    _output.SavePerChunkJson(Path.GetFileName(chunkInfos[idx].Path), result.RawResponse, perChunkDirFull);
                }

                _output.AppendSegmentsToMarkdown(mdPath, result.Segments, result.Offset, result.EmitGuard);
            }
        }

        if (jobId != null && nodeModel != null)
            CompleteTranscribePhase(nodeModel, jobId);
        progress.Complete();

        StepStart(jobId ?? "", "merge", "phase");
        UpdateProgress(JobState.Running, 90, "Merging", totalChunks, totalChunks, null, null, null);
        _output.FinalizeMarkdown(mdPath);
        var sortedResults = results.OrderBy(x => x.Index).Select(x => x.Result).ToList();
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
