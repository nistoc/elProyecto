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
    /// <summary>The sums of the run so far; replaces the stored ones (<see cref="RefineJobStatus.Totals"/>).</summary>
    public RefineRunTotals? Totals { get; set; }
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
    /// <summary>What the run came to: blocks that failed, tokens billed, what the check of edits did.</summary>
    public RefineRunTotals Totals { get; set; } = new();
}

/// <summary>
/// The sums of a run. A block whose call failed keeps its lines as they were and the job still ends Completed,
/// so <see cref="FailedBatches"/> is where a caller sees that a part of the transcript was not refined.
/// </summary>
public sealed record RefineRunTotals
{
    public int FailedBatches { get; init; }
    /// <summary>Tokens of all calls of the run as the API reports them; 0 when it reports none.</summary>
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    /// <summary>Changes of the model the check of edits kept; 0 without the check.</summary>
    public int EditsLetThrough { get; init; }
    /// <summary>Changes of the model the check of edits put back; 0 without the check.</summary>
    public int EditsPutBack { get; init; }
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
