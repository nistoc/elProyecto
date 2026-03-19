namespace XtractManager.Features.Jobs.Application;

public interface ITranscriptionServiceClient
{
    Task<SubmitJobResult> SubmitJobAsync(string configPath, string inputFilePath, IReadOnlyList<string>? tags, CancellationToken ct = default);
    IAsyncEnumerable<JobStatusUpdate> StreamJobStatusAsync(string jobId, CancellationToken ct = default);
    Task<ChunkCommandResult> ChunkCommandAsync(string agent04JobId, TranscriptionChunkAction action, int chunkIndex, CancellationToken ct = default);
}

/// <summary>Aligns with agent04.proto ChunkCommandAction numeric values.</summary>
public enum TranscriptionChunkAction
{
    Cancel = 1,
    Skip = 2,
    Retranscribe = 3,
    Split = 4,
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
    string? ErrorMessage);
