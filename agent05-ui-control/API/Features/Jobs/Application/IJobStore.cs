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
    public ChunkState? Chunks { get; set; }
    public JobResult? Result { get; set; }
    public string? OriginalFilename { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
    public string? CreatedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? AgentPaused { get; set; }
    /// <summary>Agent04 transcription job id (gRPC SubmitJob). Used for chunk commands.</summary>
    public string? Agent04JobId { get; set; }

    /// <summary>Latest automatic-retry / telemetry line from Agent04 (not persisted; refreshed via GetJobStatus).</summary>
    public string? TranscriptionFooterHint { get; set; }

    /// <summary>
    /// One-line debug from last live VM merge (GET / merge Agent04). Not persisted; copy into bug reports.
    /// </summary>
    public string? TranscriptionSyncDebug { get; set; }

    /// <summary>Last Agent04 error text from gRPC status stream (Failed/Cancelled) or local submit failure.</summary>
    public string? TranscriptionError { get; set; }

    /// <summary>Path to transcript.md from agent04 (relative to agent04 workspace).</summary>
    public string? MdOutputPath { get; set; }
    /// <summary>Full path to the job directory (workspace folder for this job). Used for debugging and UI to show where files are looked for.</summary>
    public string? JobDirectoryPath { get; set; }

    /// <summary>
    /// What the job was transcribed with, read from its folder on every request (not kept in the store);
    /// null when the folder does not tell.
    /// </summary>
    public JobRunSettings? RunSettings { get; set; }

    /// <summary>
    /// The job holds a lesson: it was started from the lessons list (<c>track_source.json</c>) or is a dialogue
    /// merged from such jobs (<c>dialogue.json</c>). Read from the folder on every request. The UI offers the
    /// lesson prompt of the refiner first for such a job.
    /// </summary>
    public bool Lesson { get; set; }

    /// <summary>
    /// Deprecated: no longer populated. Use <c>GET /api/jobs/{id}/files</c> for structured project files. Kept for JSON backward compatibility with older clients.
    /// </summary>
    public IReadOnlyList<JobFileInfo>? Files { get; set; }

    /// <summary>Deep copy so readers can serialize/mutate their view while writers mutate the stored instance.</summary>
    public JobSnapshot Clone() => new()
    {
        Id = Id,
        Status = Status,
        Phase = Phase,
        Chunks = Chunks?.Clone(),
        Result = Result?.Clone(),
        OriginalFilename = OriginalFilename,
        Tags = Tags?.ToList(),
        CreatedAt = CreatedAt,
        CompletedAt = CompletedAt,
        AgentPaused = AgentPaused,
        Agent04JobId = Agent04JobId,
        TranscriptionFooterHint = TranscriptionFooterHint,
        TranscriptionSyncDebug = TranscriptionSyncDebug,
        TranscriptionError = TranscriptionError,
        MdOutputPath = MdOutputPath,
        JobDirectoryPath = JobDirectoryPath,
        RunSettings = RunSettings,
        Lesson = Lesson,
        Files = Files?.Select(f => new JobFileInfo
        {
            Name = f.Name,
            Kind = f.Kind,
            SizeBytes = f.SizeBytes,
            LineCount = f.LineCount,
            DurationSeconds = f.DurationSeconds,
        }).ToList(),
    };
}

/// <summary>Legacy flat file row (root-only scan). Prefer <see cref="JobProjectFiles"/> via GET /api/jobs/:id/files.</summary>
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

public class ChunkState
{
    public int Total { get; set; }
    public IReadOnlyList<int> Active { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> Completed { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> Cancelled { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> Failed { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int>? Skipped { get; set; }
    public IReadOnlyDictionary<int, SplitJob>? SplitJobs { get; set; }
    /// <summary>Per-chunk virtual model from Agent04 (ISO8601 timestamps); survives page refresh.</summary>
    public IReadOnlyList<ChunkVirtualModelEntry>? ChunkVirtualModel { get; set; }

    public ChunkState Clone() => new()
    {
        Total = Total,
        Active = Active.ToList(),
        Completed = Completed.ToList(),
        Cancelled = Cancelled.ToList(),
        Failed = Failed.ToList(),
        Skipped = Skipped?.ToList(),
        SplitJobs = SplitJobs?.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
        ChunkVirtualModel = ChunkVirtualModel?.Select(e => e.Clone()).ToList(),
    };
}

public sealed class ChunkVirtualModelEntry
{
    public int Index { get; set; }
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string State { get; set; } = "Pending";
    /// <summary>Agent04 step error (optional); from proto chunk_virtual_model.error_message.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>True for split sub-chunk rows (see proto <c>is_sub_chunk</c>).</summary>
    public bool IsSubChunk { get; set; }
    /// <summary>Parent chunk index when <see cref="IsSubChunk"/>; otherwise 0.</summary>
    public int ParentChunkIndex { get; set; }
    /// <summary>Sub index within parent when <see cref="IsSubChunk"/>; otherwise 0.</summary>
    public int SubChunkIndex { get; set; }

    /// <summary>Per-chunk transcription diagnostics from Agent04 (retries, HTTP OK, etc.).</summary>
    public string? TranscriptActivityLog { get; set; }

    public ChunkVirtualModelEntry Clone() => new()
    {
        Index = Index,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        State = State,
        ErrorMessage = ErrorMessage,
        IsSubChunk = IsSubChunk,
        ParentChunkIndex = ParentChunkIndex,
        SubChunkIndex = SubChunkIndex,
        TranscriptActivityLog = TranscriptActivityLog,
    };
}

public class SplitJob
{
    public int ParentIdx { get; set; }
    public int Parts { get; set; }
    public string Status { get; set; } = "";
    public IReadOnlyList<SubChunk> SubChunks { get; set; } = Array.Empty<SubChunk>();
    public string? MergedText { get; set; }
    public string? Error { get; set; }

    public SplitJob Clone() => new()
    {
        ParentIdx = ParentIdx,
        Parts = Parts,
        Status = Status,
        SubChunks = SubChunks.Select(s => new SubChunk { Idx = s.Idx, Status = s.Status, AudioPath = s.AudioPath }).ToList(),
        MergedText = MergedText,
        Error = Error,
    };
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

    public JobResult Clone() => new()
    {
        Transcript = Transcript,
        TranscriptFixed = TranscriptFixed,
        TranscriptFixedAll = TranscriptFixedAll?.ToList(),
        RawJson = RawJson,
    };
}
