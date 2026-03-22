using System.Text.RegularExpressions;
using Agent04.Application;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class ProjectArtifactService : IProjectArtifactService
{
    private static readonly Regex SubIndexInFileName = new(
        @"_sub_0*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SafeJobIdChars = new(@"[^a-zA-Z0-9_\-\.]", RegexOptions.Compiled);

    private readonly IJobArtifactRootRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly ICancellationManagerFactory _cancellationFactory;
    private readonly IAudioUtils _audioUtils;
    private readonly ITranscriptionOutputWriter _outputWriter;
    private readonly WorkspaceRoot _workspaceRoot;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<ProjectArtifactService> _logger;

    public ProjectArtifactService(
        IJobArtifactRootRegistry registry,
        IConfiguration configuration,
        ICancellationManagerFactory cancellationFactory,
        IAudioUtils audioUtils,
        ITranscriptionOutputWriter outputWriter,
        WorkspaceRoot workspaceRoot,
        IHostEnvironment hostEnvironment,
        ILogger<ProjectArtifactService> logger)
    {
        _registry = registry;
        _configuration = configuration;
        _cancellationFactory = cancellationFactory;
        _audioUtils = audioUtils;
        _outputWriter = outputWriter;
        _workspaceRoot = workspaceRoot;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    /// <inheritdoc />
    public ArtifactRootResolutionResult ResolveJobArtifactRoot(
        string workspaceRootFull,
        string agent04JobId,
        string? jobDirectoryRelative)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootFull))
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRootFull));

        var root = Path.GetFullPath(workspaceRootFull.Trim());
        if (_registry.TryGet(agent04JobId, out var registered) && !string.IsNullOrEmpty(registered))
            return new ArtifactRootResolutionResult(registered, null, null);

        if (!string.IsNullOrWhiteSpace(jobDirectoryRelative))
        {
            var rel = jobDirectoryRelative.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (rel.Contains("..", StringComparison.Ordinal) ||
                rel.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            {
                return new ArtifactRootResolutionResult(
                    null,
                    ArtifactRootResolutionFailureCode.InvalidRelativePath,
                    "job_directory_relative must be a single path segment");
            }

            var combined = Path.GetFullPath(Path.Combine(root, rel));
            var back = Path.GetRelativePath(root, combined);
            if (back.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(back))
            {
                return new ArtifactRootResolutionResult(
                    null,
                    ArtifactRootResolutionFailureCode.OutsideWorkspace,
                    "job_directory_relative resolves outside workspace_root");
            }

            return new ArtifactRootResolutionResult(combined, null, null);
        }

        var strict = _configuration.GetValue("Agent04:StrictChunkCancelPath", false);
        if (strict)
        {
            return new ArtifactRootResolutionResult(
                null,
                ArtifactRootResolutionFailureCode.StrictRequiresJobDirectoryRelative,
                "Chunk cancel requires job_directory_relative after restart or unknown job; set Agent04:StrictChunkCancelPath=false to allow legacy workspace-root fallback.");
        }

        _logger.LogWarning(
            "ChunkCommand: cancel signals use workspace root for Agent04 job {JobId}; worker may not see them if artifacts are under a job subfolder. Pass job_directory_relative.",
            agent04JobId);
        return new ArtifactRootResolutionResult(root, null, null);
    }

    /// <inheritdoc />
    public ICancellationManager GetCancellationManager(string agent04JobId, string cancelBaseDirectoryFull) =>
        _cancellationFactory.Get(agent04JobId, cancelBaseDirectoryFull);

    /// <inheritdoc />
    public Task WritePendingChunkIndicesAsync(string artifactRoot, IReadOnlyList<int> chunkIndices, CancellationToken ct)
    {
        var path = Path.Combine(artifactRoot, PendingChunksReader.FileName);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var payload = new { chunk_indices = chunkIndices.ToList() };
        var json = System.Text.Json.JsonSerializer.Serialize(payload, TranscriptionJsonSerializerOptions.Compact);
        return File.WriteAllTextAsync(path, json, ct);
    }

    /// <inheritdoc />
    public Task<HashSet<int>?> TryLoadAndConsumePendingChunksAsync(string artifactRoot, CancellationToken ct) =>
        PendingChunksReader.TryLoadAndConsumeAsync(artifactRoot, ct);

    /// <inheritdoc />
    public Task<TranscriptionWorkStateDocument?> TryLoadWorkStateAsync(string artifactRoot, CancellationToken ct) =>
        TranscriptionWorkStateFile.TryLoadAsync(artifactRoot, ct);

    /// <inheritdoc />
    public Task SaveWorkStateAsync(string artifactRoot, TranscriptionWorkStateDocument doc, CancellationToken ct) =>
        TranscriptionWorkStateFile.SaveAsync(artifactRoot, doc, ct);

    /// <inheritdoc />
    public Task UpsertWorkStateChunkAsync(
        string artifactRoot,
        int schemaVersion,
        int totalChunks,
        int chunkIndex,
        JobState state,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? error,
        bool recoveredFromArtifacts,
        CancellationToken ct) =>
        TranscriptionWorkStateFile.UpsertChunkAsync(
            artifactRoot,
            schemaVersion,
            totalChunks,
            chunkIndex,
            state,
            startedAt,
            completedAt,
            error,
            recoveredFromArtifacts,
            ct);

    /// <inheritdoc />
    public Task UpsertWorkStateSubChunkAsync(
        string artifactRoot,
        int schemaVersion,
        int totalChunks,
        int parentChunkIndex,
        int subChunkIndex,
        JobState state,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? error,
        CancellationToken ct) =>
        TranscriptionWorkStateFile.UpsertSubChunkAsync(
            artifactRoot,
            schemaVersion,
            totalChunks,
            parentChunkIndex,
            subChunkIndex,
            state,
            startedAt,
            completedAt,
            error,
            ct);

    /// <inheritdoc />
    public async Task<int> ResolveTotalChunksHintAsync(string artifactRoot, CancellationToken ct)
    {
        var ws = await TryLoadWorkStateAsync(artifactRoot, ct).ConfigureAwait(false);
        if (ws?.TotalChunks > 0)
            return ws.TotalChunks;
        try
        {
            var map = TranscriptionChunkOnDiskReader.MapPartIndexToAudioPath(Path.Combine(artifactRoot, "chunks"));
            if (map.Count > 0)
                return map.Keys.Max() + 1;
        }
        catch
        {
            /* best-effort */
        }

        return 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChunkArtifactGroupResult>> GetChunkArtifactGroupsAsync(
        string artifactRoot,
        int totalChunksHint,
        CancellationToken ct)
    {
        var scan = JobArtifactDirectoryScanner.ScanForChunkGrouping(artifactRoot);
        var total = totalChunksHint;
        if (total <= 0)
        {
            var doc = await TryLoadWorkStateAsync(artifactRoot, ct).ConfigureAwait(false);
            total = doc?.TotalChunks ?? 0;
        }

        var indices = ChunkArtifactGrouping.ComputeChunkIndices(total, scan);
        return ChunkArtifactGrouping.BuildChunkGroups(scan, indices, total);
    }

    /// <inheritdoc />
    public void InitializeJobMarkdownOutput(string mdPath) => _outputWriter.InitializeMarkdown(mdPath);

    /// <inheritdoc />
    public void ResetJobTranscriptionSpeakerMap() => _outputWriter.ResetSpeakerMap();

    /// <inheritdoc />
    public void AppendJobMarkdownSegments(string mdPath, IReadOnlyList<ASRSegment> segments, double offset, double emitGuard) =>
        _outputWriter.AppendSegmentsToMarkdown(mdPath, segments, offset, emitGuard);

    /// <inheritdoc />
    public void FinalizeJobMarkdownOutput(string mdPath) => _outputWriter.FinalizeMarkdown(mdPath);

    /// <inheritdoc />
    public void SaveJobCombinedTranscriptionJson(string jsonPath, IReadOnlyList<TranscriptionResult> results) =>
        _outputWriter.SaveCombinedJson(jsonPath, results);

    /// <inheritdoc />
    public void SaveJobPerChunkTranscriptionJson(string chunkBasename, IReadOnlyDictionary<string, object?> response, string outputDir) =>
        _outputWriter.SavePerChunkJson(chunkBasename, response, outputDir);

    /// <inheritdoc />
    public void WriteSubChunkTranscriptionResult(string resultsDir, int subChunkIndex, TranscriptionResult result) =>
        SubChunkResultWriter.Save(resultsDir, subChunkIndex, result);

    /// <inheritdoc />
    public async Task<(bool Ok, string Message)> TryOperatorSplitAsync(
        string artifactRoot,
        int chunkIndex,
        int splitParts,
        CancellationToken ct)
    {
        if (splitParts < 2)
            return (false, "split_parts must be >= 2");

        var chunksDir = Path.Combine(artifactRoot, "chunks");
        if (!Directory.Exists(chunksDir))
            return (false, "chunks directory not found under job workspace");

        static bool IsAudioChunk(string p)
        {
            var e = Path.GetExtension(p);
            return e.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                   || e.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
                   || e.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                   || e.Equals(".flac", StringComparison.OrdinalIgnoreCase)
                   || e.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
        }

        var files = Directory.GetFiles(chunksDir)
            .Where(IsAudioChunk)
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (chunkIndex >= files.Count)
            return (false, $"chunk_index {chunkIndex} out of range (found {files.Count} chunk files)");

        var inputPath = files[chunkIndex];
        var ffmpeg = _audioUtils.WhichOr(_configuration["Agent04:FfmpegPath"], "ffmpeg") ?? "ffmpeg";
        var ffprobe = _audioUtils.WhichOr(_configuration["Agent04:FfprobePath"], "ffprobe") ?? "ffprobe";

        var (durSec, _) = _audioUtils.GetDurationAndSize(ffprobe, inputPath);
        if (durSec <= 0)
            return (false, "could not read audio duration (ffprobe)");

        var splitChunksDir = "split_chunks";
        var overlapSec = 1.0;
        var configRel = (_configuration["Agent04:ConfigPath"] ?? "config/default.json").Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPathFull = ResolveConfigFullPath(_workspaceRoot.RootPath, configRel);
        if (!string.IsNullOrEmpty(configPathFull))
        {
            try
            {
                var cfg = await TranscriptionConfig.FromFileAsync(configPathFull, ct).ConfigureAwait(false);
                splitChunksDir = cfg.SplitChunksDir;
                overlapSec = cfg.ChunkOverlapSec;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Operator split: optional transcription config not loaded from {Path}", configPathFull);
            }
        }

        var ext = Path.GetExtension(inputPath);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var outRoot = Path.Combine(artifactRoot, splitChunksDir, $"chunk_{chunkIndex}", "sub_chunks");
        Directory.CreateDirectory(outRoot);

        IReadOnlyList<OperatorChunkSplitPlanner.Segment> plan;
        try
        {
            plan = OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap(durSec, splitParts, overlapSec);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }

        try
        {
            await Task.Run(() =>
            {
                for (var k = 0; k < plan.Count; k++)
                {
                    var seg = plan[k];
                    var outName = $"{baseName}_sub_{k:D2}{ext}";
                    var outPath = Path.Combine(outRoot, outName);
                    _audioUtils.ExtractAudioSegmentCopyOrReencode(ffmpeg, inputPath, seg.StartSec, seg.DurationSec, outPath);
                }
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Operator split failed for chunk {Chunk}", chunkIndex);
            return (false, ex.Message);
        }

        _logger.LogInformation("Operator split ok: chunk {Chunk} -> {Parts} parts under {Dir}", chunkIndex, splitParts, outRoot);
        return (true, "split_ok");
    }

    /// <inheritdoc />
    public async Task<(bool Ok, string Message)> TryDeleteSubChunkArtifactsAsync(
        string artifactRoot,
        string agent04JobId,
        int parentChunkIndex,
        int subChunkIndex,
        string? splitChunksDir,
        CancellationToken ct,
        Func<ValueTask<bool>>? isSubChunkRunningAsync = null)
    {
        if (parentChunkIndex < 0 || subChunkIndex < 0)
            return (false, "invalid parameters");

        if (isSubChunkRunningAsync != null && await isSubChunkRunningAsync().ConfigureAwait(false))
            return (false, "sub_chunk_running");

        var root = Path.GetFullPath(artifactRoot);
        var dirRel = string.IsNullOrWhiteSpace(splitChunksDir)
            ? "split_chunks"
            : splitChunksDir.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var chunkDir = Path.Combine(root, dirRel, $"chunk_{parentChunkIndex}");
        var subChunksDirPath = Path.Combine(chunkDir, "sub_chunks");
        var resultsDir = Path.Combine(chunkDir, "results");

        if (Directory.Exists(subChunksDirPath))
        {
            foreach (var fi in new DirectoryInfo(subChunksDirPath).EnumerateFiles())
            {
                var m = SubIndexInFileName.Match(fi.Name);
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out var idx) || idx != subChunkIndex)
                    continue;
                try
                {
                    fi.Delete();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Delete sub-chunk audio {Path}", fi.FullName);
                    return (false, "failed to delete sub-chunk audio file");
                }
            }
        }

        var resultJson = Path.Combine(resultsDir, $"sub_chunk_{subChunkIndex:D2}_result.json");
        try
        {
            if (File.Exists(resultJson))
                File.Delete(resultJson);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Delete sub-chunk result {Path}", resultJson);
            return (false, "failed to delete sub-chunk result json");
        }

        foreach (var suffix in new[] { "json", "md" })
        {
            var mergedPath = Path.Combine(chunkDir, $"chunk_{parentChunkIndex}_merged.{suffix}");
            try
            {
                if (File.Exists(mergedPath))
                    File.Delete(mergedPath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Delete merged artifact {Path}", mergedPath);
                return (false, "failed to delete merged split artifact");
            }
        }

        var safe = string.IsNullOrEmpty(agent04JobId) ? "_unknown" : SafeJobIdChars.Replace(agent04JobId, "_");
        var cancelFlag = Path.Combine(root, ".agent04_chunk_cancel", safe, $"cancel_sub_{parentChunkIndex}_{subChunkIndex}.flag");
        try
        {
            if (File.Exists(cancelFlag))
                File.Delete(cancelFlag);
        }
        catch
        {
            /* best-effort */
        }

        try
        {
            await TranscriptionWorkStateFile.TryRemoveSubChunkRowAsync(root, parentChunkIndex, subChunkIndex, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update work state after sub-chunk delete");
            return (false, "could not update transcription_work_state.json");
        }

        return (true, "sub_chunk_deleted");
    }

    private string? ResolveConfigFullPath(string workspaceRoot, string configPathRel)
    {
        var underWorkspace = Path.GetFullPath(Path.Combine(workspaceRoot, configPathRel));
        if (File.Exists(underWorkspace))
            return underWorkspace;

        var contentRoot = _hostEnvironment.ContentRootPath;
        if (!string.IsNullOrEmpty(contentRoot))
        {
            var underApp = Path.GetFullPath(Path.Combine(contentRoot, configPathRel));
            if (File.Exists(underApp))
                return underApp;
        }

        return null;
    }
}
