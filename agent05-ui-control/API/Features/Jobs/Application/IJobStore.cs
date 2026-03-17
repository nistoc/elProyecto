namespace XtractManager.Features.Jobs.Application;

public interface IJobStore
{
    Task<JobSnapshot?> GetAsync(string jobId, CancellationToken ct = default);
    Task<IReadOnlyList<JobListItem>> ListAsync(JobListFilter filter, CancellationToken ct = default);
    Task<string> CreateAsync(JobCreateInput input, CancellationToken ct = default);
    Task<bool> UpdateAsync(string jobId, Action<JobSnapshot> update, CancellationToken ct = default);
    Task<bool> DeleteAsync(string jobId, CancellationToken ct = default);
}

public record JobCreateInput(string OriginalFilename, IReadOnlyList<string>? Tags = null);

public record JobListItem(
    string Id,
    string OriginalFilename,
    string Status,
    string Phase,
    string? CreatedAt,
    string? CompletedAt,
    IReadOnlyList<string>? Tags);

public record JobListFilter(
    string? SemanticKey = null,
    string? Status = null,
    string? From = null,
    string? To = null,
    int Limit = 50,
    int Offset = 0);

public class JobSnapshot
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "queued";
    public string Phase { get; set; } = "idle";
    public IReadOnlyList<LogEntry> Logs { get; set; } = Array.Empty<LogEntry>();
    public ChunkState? Chunks { get; set; }
    public JobResult? Result { get; set; }
    public string? OriginalFilename { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
    public string? CreatedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? AgentPaused { get; set; }
    /// <summary>Path to transcript.md from agent04 (relative to agent04 workspace).</summary>
    public string? MdOutputPath { get; set; }
    /// <summary>Full path to the job directory (workspace folder for this job). Used for debugging and UI to show where files are looked for.</summary>
    public string? JobDirectoryPath { get; set; }
    /// <summary>List of files in the job directory with display-friendly info (for UI). Populated for archive jobs and can be set when serving snapshot.</summary>
    public IReadOnlyList<JobFileInfo>? Files { get; set; }
}

/// <summary>Display info for a file in a job directory (name, size; for text: line count; for audio: duration).</summary>
public class JobFileInfo
{
    public string Name { get; set; } = "";
    /// <summary>One of: "text", "audio", "other".</summary>
    public string Kind { get; set; } = "other";
    public long SizeBytes { get; set; }
    /// <summary>For text files: number of lines.</summary>
    public int? LineCount { get; set; }
    /// <summary>For audio files: duration in seconds.</summary>
    public double? DurationSeconds { get; set; }
}

public class LogEntry
{
    public long Ts { get; set; }
    public string Level { get; set; } = "info";
    public string Message { get; set; } = "";
}

public class ChunkState
{
    public int Total { get; set; }
    public IReadOnlyList<int> Active { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> Completed { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> Cancelled { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> Failed { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int>? Skipped { get; set; }
    public IReadOnlyDictionary<int, SplitJob>? SplitJobs { get; set; }
}

public class SplitJob
{
    public int ParentIdx { get; set; }
    public int Parts { get; set; }
    public string Status { get; set; } = "";
    public IReadOnlyList<SubChunk> SubChunks { get; set; } = Array.Empty<SubChunk>();
    public string? MergedText { get; set; }
    public string? Error { get; set; }
}

public class SubChunk
{
    public int Idx { get; set; }
    public string Status { get; set; } = "";
    public string? AudioPath { get; set; }
}

public class JobResult
{
    public string? Transcript { get; set; }
    public string? TranscriptFixed { get; set; }
    public IReadOnlyList<string>? TranscriptFixedAll { get; set; }
    public string? RawJson { get; set; }
}
