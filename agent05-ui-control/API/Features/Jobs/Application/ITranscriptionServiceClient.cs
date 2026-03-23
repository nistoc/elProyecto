namespace XtractManager.Features.Jobs.Application;

public interface ITranscriptionServiceClient
{
    Task<SubmitJobResult> SubmitJobAsync(string configPath, string inputFilePath, IReadOnlyList<string>? tags, CancellationToken ct = default);
    IAsyncEnumerable<JobStatusUpdate> StreamJobStatusAsync(string jobId, CancellationToken ct = default);
    /// <param name="clientChunkVirtualModel">Last known VM from the job snapshot; Agent04 merges with Rentgen and returns the result.</param>
    IAsyncEnumerable<JobStatusUpdate> StreamJobStatusAsync(
        string jobId,
        IReadOnlyList<ChunkVirtualModelEntry>? clientChunkVirtualModel,
        CancellationToken ct);
    /// <summary>Live VM + footer from Agent04.</summary>
    Task<JobStatusUpdate?> GetJobStatusAsync(string agent04JobId, CancellationToken ct = default);
    /// <summary>Live VM merged on Agent04 when <paramref name="clientChunkVirtualModel"/> is provided.</summary>
    Task<JobStatusUpdate?> GetJobStatusAsync(
        string agent04JobId,
        IReadOnlyList<ChunkVirtualModelEntry>? clientChunkVirtualModel,
        CancellationToken ct);
    /// <param name="jobDirectoryRelative">Xtract job folder name under workspace (single segment); pass for chunk cancel paths under per-job artifacts.</param>
    Task<ChunkCommandResult> ChunkCommandAsync(
        string agent04JobId,
        TranscriptionChunkAction action,
        int chunkIndex,
        string? jobDirectoryRelative = null,
        int splitParts = 0,
        int subChunkIndex = 0,
        CancellationToken ct = default);

    /// <summary>Chunk/split artifact grouping from Agent04. VM is merged on Agent04 when <paramref name="clientChunkVirtualModel"/> is provided; UI still overlays weak rows from snapshot.</summary>
    Task<ChunkArtifactGroupsResult?> GetChunkArtifactGroupsAsync(
        string agent04JobId,
        string jobDirectoryRelative,
        int totalChunks,
        IReadOnlyList<ChunkVirtualModelEntry>? clientChunkVirtualModel = null,
        CancellationToken ct = default);

    /// <summary>Structured file tree from Agent04 (null if unavailable — caller may fall back to local scan).</summary>
    /// <param name="totalChunks">Pass job chunk total when known (same hint as GetChunkArtifactGroups); 0 lets Agent04 use work state.</param>
    Task<JobProjectFiles?> GetProjectFilesAsync(
        string agent04JobId,
        string jobDirectoryRelative,
        int totalChunks = 0,
        CancellationToken ct = default);
}

/// <summary>Aligns with agent04.proto ChunkCommandAction numeric values (2 reserved — removed SKIP).</summary>
public enum TranscriptionChunkAction
{
    Cancel = 1,
    Retranscribe = 3,
    Split = 4,
    TranscribeSub = 5,
    RebuildCombined = 6,
    DeleteSubChunk = 7,
    RebuildSplitMerged = 8,
    /// <summary>Write <c>chunks_md</c> for one chunk from <c>chunks_json</c> (Agent04).</summary>
    WriteChunkMd = 9,
}

public record ChunkCommandResult(bool Ok, string Message);

/// <summary>JSON shape matches UI <c>ChunkArtifactGroup</c> (camelCase); <c>vmRow</c> from Agent04 Rentgen when present.</summary>
public sealed class ChunkArtifactGroupsResult
{
    public IReadOnlyList<ChunkArtifactGroupJson> Groups { get; init; } = Array.Empty<ChunkArtifactGroupJson>();
}

public sealed class ChunkArtifactGroupJson
{
    public int Index { get; set; }
    public string DisplayStem { get; set; } = "";
    public IReadOnlyList<JobProjectFile> AudioFiles { get; set; } = Array.Empty<JobProjectFile>();
    public IReadOnlyList<JobProjectFile> JsonFiles { get; set; } = Array.Empty<JobProjectFile>();
    public IReadOnlyList<SubChunkArtifactGroupJson> SubChunks { get; set; } = Array.Empty<SubChunkArtifactGroupJson>();
    public IReadOnlyList<JobProjectFile> MergedSplitFiles { get; set; } = Array.Empty<JobProjectFile>();
    public ChunkVirtualModelEntry? VmRow { get; set; }
}

public sealed class SubChunkArtifactGroupJson
{
    public int? SubIndex { get; set; }
    public string DisplayStem { get; set; } = "";
    public IReadOnlyList<JobProjectFile> AudioFiles { get; set; } = Array.Empty<JobProjectFile>();
    public IReadOnlyList<JobProjectFile> JsonFiles { get; set; } = Array.Empty<JobProjectFile>();
    public ChunkVirtualModelEntry? VmRow { get; set; }
}

public record SubmitJobResult(string JobId);

public record JobStatusUpdate(
    string JobId,
    string State,
    int ProgressPercent,
    string? CurrentPhase,
    int TotalChunks,
    int ProcessedChunks,
    string? MdOutputPath,
    string? JsonOutputPath,
    string? ErrorMessage,
    IReadOnlyList<ChunkVirtualModelEntry>? ChunkVirtualModel = null,
    string? TranscriptionFooterHint = null);
