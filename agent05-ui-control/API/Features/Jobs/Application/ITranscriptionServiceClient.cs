namespace XtractManager.Features.Jobs.Application;

public interface ITranscriptionServiceClient
{
    Task<SubmitJobResult> SubmitJobAsync(string configPath, string inputFilePath, IReadOnlyList<string>? tags, CancellationToken ct = default);
    IAsyncEnumerable<JobStatusUpdate> StreamJobStatusAsync(string jobId, CancellationToken ct = default);
    /// <param name="jobDirectoryRelative">Xtract job folder name under workspace (single segment); pass for chunk cancel paths under per-job artifacts.</param>
    Task<ChunkCommandResult> ChunkCommandAsync(
        string agent04JobId,
        TranscriptionChunkAction action,
        int chunkIndex,
        string? jobDirectoryRelative = null,
        int splitParts = 0,
        int subChunkIndex = 0,
        CancellationToken ct = default);
}

/// <summary>Aligns with agent04.proto ChunkCommandAction numeric values.</summary>
public enum TranscriptionChunkAction
{
    Cancel = 1,
    Skip = 2,
    Retranscribe = 3,
    Split = 4,
    TranscribeSub = 5,
}

public record ChunkCommandResult(bool Ok, string Message);

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
    IReadOnlyList<ChunkVirtualModelEntry>? ChunkVirtualModel = null);
