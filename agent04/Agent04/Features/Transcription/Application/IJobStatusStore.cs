namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Store for transcription job status (for REST/gRPC monitoring).
/// </summary>
public interface IJobStatusStore
{
    string Create(IReadOnlyList<string>? tags = null, string? callbackUrl = null);
    void Update(string jobId, JobStatusUpdate update);
    JobStatus? Get(string jobId);
    IReadOnlyList<JobStatus> List(JobListFilter? filter = null);

    /// <summary>
    /// Ensures a Completed row exists for disk-only jobs (process restart): ChunkCommand retranscribe/split/etc.
    /// Does not replace an existing row; may set <see cref="JobStatus.TotalChunks"/> when it was 0.
    /// </summary>
    void EnsureDiskBackedCompletedJob(string jobId, int totalChunks);
}

public sealed class JobStatusUpdate
{
    public JobState? State { get; set; }
    public int? ProgressPercent { get; set; }
    public string? CurrentPhase { get; set; }
    public int? TotalChunks { get; set; }
    public int? ProcessedChunks { get; set; }
    public string? MdOutputPath { get; set; }
    public string? JsonOutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>When true, clears <see cref="JobStatus.SilenceSourceDurationSec"/> and regions.</summary>
    public bool? ClearSilenceTimeline { get; set; }
    /// <summary>Source file duration (seconds) for normalizing silence regions on the UI timeline.</summary>
    public double? SilenceSourceDurationSec { get; set; }
    /// <summary>Merged silence intervals to remove (seconds on source). Null = leave unchanged.</summary>
    public IReadOnlyList<SilenceTimelineRegionDto>? SilenceTimelineRegions { get; set; }
}

public enum JobState { Pending, Running, Completed, Failed, Cancelled }

public sealed class JobStatus
{
    public string JobId { get; set; } = "";
    public JobState State { get; set; }
    public int ProgressPercent { get; set; }
    public string? CurrentPhase { get; set; }
    public int TotalChunks { get; set; }
    public int ProcessedChunks { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? MdOutputPath { get; set; }
    public string? JsonOutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CallbackUrl { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    /// <summary>Duration of the WAV used for silence detect (seconds). 0 if unset.</summary>
    public double SilenceSourceDurationSec { get; set; }
    /// <summary>Silence runs to compress away; coordinates are seconds on <see cref="SilenceSourceDurationSec"/> timeline.</summary>
    public IReadOnlyList<SilenceTimelineRegionDto> SilenceTimelineRegions { get; set; } =
        Array.Empty<SilenceTimelineRegionDto>();
}

public sealed class JobListFilter
{
    public JobState? Status { get; set; }
    /// <summary>Filter jobs by semantic key (one of the job's Tags).</summary>
    public string? SemanticKey { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}
