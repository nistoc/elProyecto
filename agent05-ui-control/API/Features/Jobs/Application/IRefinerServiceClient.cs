namespace XtractManager.Features.Jobs.Application;

public interface IRefinerServiceClient
{
    Task<SubmitRefineJobResult> SubmitRefineJobAsync(RefineJobInput input, CancellationToken ct = default);
    IAsyncEnumerable<RefineStatusUpdate> StreamRefineStatusAsync(string jobId, CancellationToken ct = default);
    Task<bool> CancelRefineJobAsync(string jobId, CancellationToken ct = default);

    /// <summary>Built-in prompts of the refiner, used as the starting point for the operator's own template.</summary>
    Task<RefineDefaults> GetDefaultsAsync(CancellationToken ct = default);
}

public record RefineDefaults(string SystemPrompt, string UserPromptTemplate, string Model);

public record RefineJobInput(
    string? InputFilePath,
    string? InputContent,
    string? OutputFilePath,
    int BatchSize = 5,
    int ContextLines = 2,
    IReadOnlyList<string>? Tags = null,
    string? JobDirectoryRelative = null,
    string? PromptFile = null,
    string? Model = null,
    string? IntermediateDir = null,
    string? SystemPrompt = null,
    string? SourceName = null,
    bool RespellingOnly = false);

public record SubmitRefineJobResult(string JobId);

public record RefineStatusUpdate(
    string JobId,
    string State,
    int ProgressPercent,
    int CurrentBatch,
    int TotalBatches,
    string? OutputFilePath,
    string? ErrorMessage,
    string? PromptPreview = null,
    IReadOnlyList<RefineBatchDelta>? Batches = null);

public record RefineBatchDelta(
    int Index,
    int Total,
    string OriginalText,
    string RefinedText,
    bool Ok,
    string? Error,
    string ContextText = "",
    int StartLine = 0,
    int EndLine = 0);
