namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Single owner of persistent job artifact layout on disk (resolve paths, read/write/delete, grouping).
/// </summary>
public interface IProjectArtifactService
{
    /// <summary>
    /// Resolves artifact root: <see cref="IJobArtifactRootRegistry"/> first, then validated
    /// <c>job_directory_relative</c> under workspace, then optional legacy workspace root (with warning) or strict failure.
    /// </summary>
    ArtifactRootResolutionResult ResolveJobArtifactRoot(
        string workspaceRootFull,
        string agent04JobId,
        string? jobDirectoryRelative);

    /// <summary>Per-job cancel flags under <c>.agent04_chunk_cancel</c> (second argument is artifact root used by pipeline/gRPC).</summary>
    ICancellationManager GetCancellationManager(string agent04JobId, string cancelBaseDirectoryFull);

    Task WritePendingChunkIndicesAsync(string artifactRoot, IReadOnlyList<int> chunkIndices, CancellationToken ct);

    Task<HashSet<int>?> TryLoadAndConsumePendingChunksAsync(string artifactRoot, CancellationToken ct);

    Task<TranscriptionWorkStateDocument?> TryLoadWorkStateAsync(string artifactRoot, CancellationToken ct);

    Task SaveWorkStateAsync(string artifactRoot, TranscriptionWorkStateDocument doc, CancellationToken ct);

    Task UpsertWorkStateChunkAsync(
        string artifactRoot,
        int schemaVersion,
        int totalChunks,
        int chunkIndex,
        JobState state,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? error,
        bool recoveredFromArtifacts,
        CancellationToken ct);

    Task UpsertWorkStateSubChunkAsync(
        string artifactRoot,
        int schemaVersion,
        int totalChunks,
        int parentChunkIndex,
        int subChunkIndex,
        JobState state,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? error,
        CancellationToken ct);

    /// <summary>Work state <see cref="TranscriptionWorkStateDocument.TotalChunks"/> or infer from <c>chunks/</c> part indices.</summary>
    Task<int> ResolveTotalChunksHintAsync(string artifactRoot, CancellationToken ct);

    /// <summary>
    /// Group chunk/split artifacts for Stats (same rules as agent05 TS). <paramref name="totalChunksHint"/> 0 loads total from work state, then falls back to file-only indices.
    /// </summary>
    Task<IReadOnlyList<ChunkArtifactGroupResult>> GetChunkArtifactGroupsAsync(
        string artifactRoot,
        int totalChunksHint,
        CancellationToken ct);
}
