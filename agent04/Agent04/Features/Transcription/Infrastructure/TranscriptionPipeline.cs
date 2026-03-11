using System.Text.Json;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class TranscriptionPipeline : ITranscriptionPipeline
{
    private readonly IAudioUtils _audioUtils;
    private readonly ITranscriptionCache _cache;
    private readonly ITranscriptionClient _client;
    private readonly ITranscriptionOutputWriter _output;
    private readonly ITranscriptionMerger _merger;
    private readonly ICancellationManager _cancellation;
    private readonly ILogger<TranscriptionPipeline>? _logger;

    public TranscriptionPipeline(
        IAudioUtils audioUtils,
        ITranscriptionCache cache,
        ITranscriptionClient client,
        ITranscriptionOutputWriter output,
        ITranscriptionMerger merger,
        ICancellationManager cancellation,
        ILogger<TranscriptionPipeline>? logger = null)
    {
        _audioUtils = audioUtils;
        _cache = cache;
        _client = client;
        _output = output;
        _merger = merger;
        _cancellation = cancellation;
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
        var root = Path.GetFullPath(workspaceRoot);

        void UpdateProgress(JobState state, int percent, string? phase, string? mdPath = null, string? jsonPath = null, string? error = null)
        {
            if (jobId == null || statusStore == null) return;
            statusStore.Update(jobId, new JobStatusUpdate
            {
                State = state,
                ProgressPercent = percent,
                CurrentPhase = phase,
                MdOutputPath = mdPath,
                JsonOutputPath = jsonPath,
                ErrorMessage = error
            });
        }

        void StepStart(string parentNodeId, string localKey, string kind)
        {
            if (jobId == null || nodeModel == null) return;
            var nodeId = string.IsNullOrEmpty(parentNodeId) ? jobId : parentNodeId + ":" + localKey;
            nodeModel.EnsureNode(nodeId, string.IsNullOrEmpty(parentNodeId) ? null : parentNodeId, jobId, kind);
            nodeModel.StartNode(nodeId);
        }

        void StepComplete(string parentNodeId, string localKey, JobState status, string? error = null)
        {
            if (jobId == null || nodeModel == null) return;
            var nodeId = string.IsNullOrEmpty(parentNodeId) ? jobId : parentNodeId + ":" + localKey;
            nodeModel.CompleteNode(nodeId, status, DateTimeOffset.UtcNow, error);
        }

        _logger?.LogInformation("[FILE] {Path}", inputFilePath);
        UpdateProgress(JobState.Running, 0, "Starting");
        if (jobId != null && nodeModel != null)
        {
            nodeModel.EnsureNode(jobId, null, jobId, "job");
            nodeModel.StartNode(jobId);
        }

        try
        {
        var workingPath = await ConvertToWavIfNeededAsync(config, inputFilePath, cancellationToken);
        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName(inputFilePath));
        var mdPath = ResolvePath(config.MdOutputPath, baseName);
        var jsonPath = ResolvePath(config.RawJsonOutputPath, baseName);

        _output.InitializeMarkdown(mdPath);
        _output.ResetSpeakerMap();

        var manifestPath = Path.Combine(config.CacheDir, baseName + ".manifest.json");
        var manifest = await _cache.LoadManifestAsync(manifestPath, cancellationToken);

        StepStart(jobId ?? "", "chunking", "phase");
        var chunkInfos = await PrepareChunksAsync(config, workingPath, cancellationToken);
        StepComplete(jobId ?? "", "chunking", JobState.Completed);
        _logger?.LogInformation("Processing {Count} chunk(s)", chunkInfos.Count);
        UpdateProgress(JobState.Running, 5, "Chunking", null, null, null);

        var transcribeParent = jobId != null ? jobId + ":transcribe" : "";
        if (jobId != null && nodeModel != null)
        {
            nodeModel.EnsureNode(transcribeParent, jobId, jobId, "phase");
            nodeModel.StartNode(transcribeParent);
        }

        var results = new List<(int Index, TranscriptionResult Result)>();
        var progress = new ChunkProgress(chunkInfos.Count, config.Get<string>("progress_time_format") ?? "HH:MM:SS.M");
        var totalChunks = chunkInfos.Count;

        for (var i = 0; i < chunkInfos.Count; i++)
        {
            if (_cancellation.IsCancelled(i))
            {
                progress.MarkCancelled(i);
                continue;
            }
            StepStart(transcribeParent, "chunk-" + i, "chunk");
            progress.MarkStarted(i);
            progress.Update();
            var pct = totalChunks > 0 ? 10 + (70 * (i + 1) / totalChunks) : 80;
            UpdateProgress(JobState.Running, pct, $"Transcribing chunk {i + 1}/{totalChunks}");

            try
            {
                var result = await ProcessChunkAsync(config, chunkInfos[i], i, chunkInfos.Count, manifest, manifestPath, baseName, cancellationToken);
                results.Add((i, result));
                progress.MarkCompleted(i);
                StepComplete(transcribeParent, "chunk-" + i, JobState.Completed);

                if (config.Get<bool?>("save_per_chunk_json") == true)
                {
                    var perChunkDir = config.Get<string>("per_chunk_json_dir") ?? "chunks_json";
                    _output.SavePerChunkJson(Path.GetFileName(chunkInfos[i].Path), result.RawResponse, perChunkDir);
                }

                _output.AppendSegmentsToMarkdown(mdPath, result.Segments, result.Offset, result.EmitGuard);
            }
            catch (Exception ex)
            {
                StepComplete(transcribeParent, "chunk-" + i, JobState.Failed, ex.Message);
                _logger?.LogWarning(ex, "Chunk {Index} failed", i + 1);
                progress.MarkCompleted(i);
                throw;
            }
        }

        if (jobId != null && nodeModel != null)
            nodeModel.CompleteNode(transcribeParent, JobState.Completed, DateTimeOffset.UtcNow);
        progress.Complete();

        StepStart(jobId ?? "", "merge", "phase");
        UpdateProgress(JobState.Running, 90, "Merging");
        _output.FinalizeMarkdown(mdPath);
        var sortedResults = results.OrderBy(x => x.Index).Select(x => x.Result).ToList();
        _output.SaveCombinedJson(jsonPath, sortedResults);
        StepComplete(jobId ?? "", "merge", JobState.Completed);

        UpdateProgress(JobState.Completed, 100, "Completed", mdPath, jsonPath);
        if (jobId != null && nodeModel != null)
            nodeModel.CompleteNode(jobId, JobState.Completed, DateTimeOffset.UtcNow);
        _logger?.LogInformation("Done. Markdown: {Md}, JSON: {Json}", mdPath, jsonPath);
        return (mdPath, jsonPath);
        }
        catch (Exception ex)
        {
            if (jobId != null && nodeModel != null)
                nodeModel.CompleteNode(jobId, JobState.Failed, DateTimeOffset.UtcNow, ex.Message);
            UpdateProgress(JobState.Failed, 0, null, null, null, ex.Message);
            throw;
        }
    }

    private static string ResolvePath(string pattern, string baseName)
    {
        return pattern.Replace("{base}", baseName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ConvertToWavIfNeededAsync(TranscriptionConfig config, string filePath, CancellationToken ct)
    {
        if (config.Get<bool?>("convert_to_wav") != true)
            return filePath;
        if (!filePath.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
            return filePath;
        var ffmpeg = _audioUtils.WhichOr(config.FfmpegPath, "ffmpeg") ?? "ffmpeg";
        var wavDir = config.Get<string>("wav_output_dir") ?? "converted_wav";
        return await Task.Run(() => _audioUtils.ConvertToWav(ffmpeg, filePath, wavDir), ct);
    }

    private async Task<IReadOnlyList<ChunkInfo>> PrepareChunksAsync(TranscriptionConfig config, string filePath, CancellationToken ct)
    {
        if (config.Get<bool?>("pre_split") != true)
            return new[] { new ChunkInfo(filePath, 0.0, 0.0) };

        var ffmpeg = _audioUtils.WhichOr(config.FfmpegPath, "ffmpeg") ?? "ffmpeg";
        var ffprobe = _audioUtils.WhichOr(config.FfprobePath, "ffprobe") ?? "ffprobe";
        var chunker = new AudioChunker(_audioUtils, ffmpeg, ffprobe);

        return await chunker.ProcessChunksForFileAsync(
            filePath,
            (float)(config.Get<double?>("target_chunk_mb") ?? 5),
            config.Get<string>("split_workdir") ?? "chunks",
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
        CancellationToken ct)
    {
        var chunkBasename = Path.GetFileName(chunkInfo.Path);
        var fingerprint = await _cache.GetFileFingerprintAsync(chunkInfo.Path, ct);

        var rawResponse = _cache.GetCachedResponse(manifest, chunkBasename, fingerprint);
        if (rawResponse == null)
        {
            var options = new TranscriptionClientOptions
            {
                Language = config.Language,
                Prompt = config.Get<string>("prompt"),
                Temperature = config.Temperature,
                ResponseFormat = config.Get<string>("response_format"),
                ChunkingStrategy = config.Get<string>("chunking_strategy")
            };
            var result = await _client.TranscribeAsync(chunkInfo.Path, options, ct);
            rawResponse = result.RawResponse;
            await _cache.CacheResponseAsync(manifestPath, manifest, chunkBasename, fingerprint, rawResponse, ct);
        }
        else
        {
            _logger?.LogInformation("[CACHE] Using cached response for chunk");
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
        var json = JsonSerializer.Serialize(value);
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
}
