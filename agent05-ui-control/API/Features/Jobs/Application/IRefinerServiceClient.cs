namespace XtractManager.Features.Jobs.Application;

public interface IRefinerServiceClient
{
    Task<SubmitRefineJobResult> SubmitRefineJobAsync(RefineJobInput input, CancellationToken ct = default);
    Task<RefineStatusUpdate> GetRefineStatusAsync(string jobId, CancellationToken ct = default);
    /// <summary>Server stream of status updates until Paused/Completed/Failed/Cancelled or job missing.</summary>
    IAsyncEnumerable<RefineStatusUpdate> StreamRefineStatusAsync(string jobId, CancellationToken ct = default);
    Task<bool> CancelRefineJobAsync(string jobId, CancellationToken ct = default);
    Task<bool> PauseRefineJobAsync(string jobId, CancellationToken ct = default);
    Task<bool> ResumeRefineJobAsync(string jobId, CancellationToken ct = default);
    /// <summary>Resume from disk when Agent06 in-memory job is gone; returns new Agent06 job id.</summary>
    Task<SubmitRefineJobResult> ResumeRefineFromCheckpointAsync(string jobDirectoryRelative, string workspaceRootOverride, CancellationToken ct = default);
}

/// <param name="WorkspaceRootOverride">Absolute jobs workspace root (Agent05); sent to Agent06 so artifacts match the UI job folder.</param>
public record RefineJobInput(
    string? InputFilePath,
    string? InputContent,
    string? OutputFilePath,
    int BatchSize = 15,
    int ContextLines = 5,
    IReadOnlyList<string>? Tags = null,
    string? JobDirectoryRelative = null,
    string? WorkspaceRootOverride = null);

public record SubmitRefineJobResult(string JobId);

public record RefineStatusUpdate(
    string JobId,
    string State,
    int ProgressPercent,
    string? CurrentPhase,
    int CurrentBatch,
    int TotalBatches,
    string? OutputFilePath,
    string? ErrorMessage,
    string? UpdatedAt,
    long StreamSequence,
    string? BatchEventKind,
    int BatchEventIndex0,
    string? BatchThreadsRelativePath,
    string? OpenAiRequestPreview = null,
    string? BatchBeforeText = null,
    string? BatchAfterText = null,
    string? RefinerLogLine = null);
