namespace XtractManager.Features.Jobs.Application;

public interface ITranscriptionServiceClient
{
    Task<SubmitJobResult> SubmitJobAsync(string configPath, string inputFilePath, IReadOnlyList<string>? tags, CancellationToken ct = default);
    IAsyncEnumerable<JobStatusUpdate> StreamJobStatusAsync(string jobId, CancellationToken ct = default);
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
    string? ErrorMessage);
