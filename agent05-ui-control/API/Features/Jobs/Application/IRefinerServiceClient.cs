namespace XtractManager.Features.Jobs.Application;

public interface IRefinerServiceClient
{
    Task<SubmitRefineJobResult> SubmitRefineJobAsync(RefineJobInput input, CancellationToken ct = default);
    IAsyncEnumerable<RefineStatusUpdate> StreamRefineStatusAsync(string jobId, CancellationToken ct = default);
    Task<bool> CancelRefineJobAsync(string jobId, CancellationToken ct = default);
}

public record RefineJobInput(
    string? InputFilePath,
    string? InputContent,
    string? OutputFilePath,
    int BatchSize = 5,
    int ContextLines = 2,
    IReadOnlyList<string>? Tags = null);

public record SubmitRefineJobResult(string JobId);

public record RefineStatusUpdate(
    string JobId,
    string State,
    int ProgressPercent,
    int CurrentBatch,
    int TotalBatches,
    string? OutputFilePath,
    string? ErrorMessage);
