using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Application;

/// <summary>
/// Store for refine job status (REST/gRPC monitoring).
/// </summary>
public interface IRefineJobStore
{
    string Create(IReadOnlyList<string>? tags = null, string? callbackUrl = null);
    void Update(string jobId, RefineJobStatusUpdate update);
    RefineJobStatus? Get(string jobId);
    IReadOnlyList<RefineJobStatus> List(RefineJobListFilter? filter = null);
}

public sealed class RefineJobStatusUpdate
{
    public RefineJobState? State { get; set; }
    public int? ProgressPercent { get; set; }
    public string? CurrentPhase { get; set; }
    public int? CurrentBatch { get; set; }
    public int? TotalBatches { get; set; }
    public string? OutputFilePath { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>Set once at start: the resolved prompt shown to the operator.</summary>
    public string? PromptPreview { get; set; }
    /// <summary>When set, appended to the job's accumulated batch list (before/after progress).</summary>
    public RefineBatchDelta? AppendBatch { get; set; }
}

public sealed class RefineJobStatus
{
    public string JobId { get; set; } = "";
    public RefineJobState State { get; set; }
    public int ProgressPercent { get; set; }
    public string? CurrentPhase { get; set; }
    public int CurrentBatch { get; set; }
    public int TotalBatches { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? OutputFilePath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CallbackUrl { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    /// <summary>The resolved prompt shown to the operator (set once).</summary>
    public string? PromptPreview { get; set; }
    /// <summary>Accumulated before/after batch deltas. Replaced (copy-on-write) on each append so stream readers see a consistent immutable snapshot.</summary>
    public IReadOnlyList<RefineBatchDelta> Batches { get; set; } = Array.Empty<RefineBatchDelta>();
}

public sealed class RefineJobListFilter
{
    public RefineJobState? Status { get; set; }
    /// <summary>Filter jobs by semantic key (one of the job's Tags).</summary>
    public string? SemanticKey { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}
