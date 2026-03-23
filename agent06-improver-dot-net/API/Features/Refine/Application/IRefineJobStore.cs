using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Application;

/// <summary>
/// Store for refine job status (REST/gRPC monitoring).
/// </summary>
public interface IRefineJobStore
{
    string Create(IReadOnlyList<string>? tags = null, string? callbackUrl = null, string? jobDirectoryRelative = null, string? workspaceRootOverride = null);
    void Update(string jobId, RefineJobStatusUpdate update);
    RefineJobStatus? Get(string jobId);
    IReadOnlyList<RefineJobStatus> List(RefineJobListFilter? filter = null);
    /// <summary>Dequeue one snapshot published after <see cref="Update"/> (for gRPC StreamRefineStatus). In-memory impl only; others return false.</summary>
    bool TryDequeueStreamSnapshot(string jobId, out RefineJobStatus? snapshot);
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
    /// <summary>Latest batch streaming hint for Refiner Threads (e.g. input_ready, output_ready).</summary>
    public string? BatchEventKind { get; set; }
    public int? BatchEventIndex0 { get; set; }
    public string? BatchThreadsRelativePath { get; set; }
    /// <summary>When true, clears batch event fields after merge.</summary>
    public bool ClearBatchEvent { get; set; }
    /// <summary>Full prompt text (system + user) about to be sent to OpenAI for the current batch.</summary>
    public string? OpenAiRequestPreview { get; set; }
    /// <summary>When true, sets <see cref="BatchBeforeText"/> on the job.</summary>
    public bool HasBatchBeforeText { get; set; }
    public string? BatchBeforeText { get; set; }
    /// <summary>When true, sets job batch-after text (<c>null</c> = pending in UI).</summary>
    public bool HasBatchAfterText { get; set; }
    public string? BatchAfterText { get; set; }
    /// <summary>When non-null, replaces job refiner log line for gRPC/UI.</summary>
    public string? RefinerLogLine { get; set; }
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
    /// <summary>Optional Xtract job folder segment for resolving artifact paths on resume.</summary>
    public string? JobDirectoryRelative { get; set; }
    /// <summary>When set, artifact paths resolve under this root instead of the server's default WorkspaceRoot.</summary>
    public string? WorkspaceRootOverride { get; set; }
    public long StreamSequence { get; set; }
    public string? BatchEventKind { get; set; }
    public int BatchEventIndex0 { get; set; } = -1;
    public string? BatchThreadsRelativePath { get; set; }
    public string? OpenAiRequestPreview { get; set; }
    public string? BatchBeforeText { get; set; }
    /// <summary>Null while model response not yet applied for current batch event.</summary>
    public string? BatchAfterText { get; set; }
    public string? RefinerLogLine { get; set; }
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
