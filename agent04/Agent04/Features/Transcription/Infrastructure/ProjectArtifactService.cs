using Agent04.Features.Transcription.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class ProjectArtifactService : IProjectArtifactService
{
    private readonly IJobArtifactRootRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly ICancellationManagerFactory _cancellationFactory;
    private readonly ILogger<ProjectArtifactService> _logger;

    public ProjectArtifactService(
        IJobArtifactRootRegistry registry,
        IConfiguration configuration,
        ICancellationManagerFactory cancellationFactory,
        ILogger<ProjectArtifactService> logger)
    {
        _registry = registry;
        _configuration = configuration;
        _cancellationFactory = cancellationFactory;
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
}
