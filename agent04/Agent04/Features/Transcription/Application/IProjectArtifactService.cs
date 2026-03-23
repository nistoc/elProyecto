using Agent04.Features.Transcription.Domain;

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

    /// <summary>
    /// Full project file catalog under <paramref name="artifactRoot"/> (UI parity with Xtract GET .../files).
    /// Entries that appear in chunk artifact groups are omitted from <c>chunks</c>, <c>chunk_json</c>, <c>intermediate</c>, and <c>split_chunks</c>.
    /// </summary>
    Task<ProjectFilesCatalogResult> GetProjectFilesCatalogAsync(string artifactRoot, int totalChunksHint, CancellationToken ct);

    // --- Phase 4: transcription outputs + operator split (facade over existing writers / ffmpeg) ---

    void InitializeJobMarkdownOutput(string mdPath);

    void ResetJobTranscriptionSpeakerMap();

    void AppendJobMarkdownSegments(string mdPath, IReadOnlyList<ASRSegment> segments, double offset, double emitGuard);

    void FinalizeJobMarkdownOutput(string mdPath);

    void SaveJobPerChunkTranscriptionJson(string chunkBasename, IReadOnlyDictionary<string, object?> response, string outputDir);

    /// <summary>
    /// Writes one standalone markdown transcript for a main pipeline chunk (speaker map reset per file).
    /// </summary>
    void SaveJobPerChunkMarkdown(string outputDir, string chunkAudioBasename, IReadOnlyList<ASRSegment> segments, double offset, double emitGuard);

    /// <summary>
    /// Overwrites <paramref name="finalMdPath"/> with stitched chunk markdown (merged split preferred per index).
    /// </summary>
    void StitchChunkMarkdownFiles(
        TranscriptionConfig config,
        string artifactRoot,
        IReadOnlyList<ChunkInfo> chunkInfos,
        string finalMdPath);

    void WriteSubChunkTranscriptionResult(string resultsDir, int subChunkIndex, TranscriptionResult result);

    /// <summary>Operator split: writes <c>split_chunks/chunk_N/sub_chunks/*_sub_XX.*</c> from main chunk audio.</summary>
    Task<(bool Ok, string Message)> TryOperatorSplitAsync(
        string artifactRoot,
        int chunkIndex,
        int splitParts,
        CancellationToken ct);

    // --- Phase 5: delete sub-chunk bundle ---

    /// <summary>
    /// Deletes sub-chunk audio under <c>sub_chunks/</c>, <c>results/sub_chunk_XX_result.json</c>, stale <c>chunk_N_merged.*</c>,
    /// cancel flag under <c>.agent04_chunk_cancel</c>, and the sub-chunk row in <c>transcription_work_state.json</c>.
    /// Edge cases: if <paramref name="isSubChunkRunningAsync"/> returns true, returns <c>(false, "sub_chunk_running")</c> and does not delete.
    /// If work state is missing or the row is absent, disk files are still removed (idempotent). Merged files are always removed so UI does not keep a stale merged view.
    /// </summary>
    Task<(bool Ok, string Message)> TryDeleteSubChunkArtifactsAsync(
        string artifactRoot,
        string agent04JobId,
        int parentChunkIndex,
        int subChunkIndex,
        string? splitChunksDir,
        CancellationToken ct,
        Func<ValueTask<bool>>? isSubChunkRunningAsync = null);
}
