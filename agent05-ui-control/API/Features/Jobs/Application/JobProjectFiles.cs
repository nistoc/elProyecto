namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// Structured file list for the project-files UI (Transcriber tab: all sections; Refiner/Result: transcripts subset).
/// Mirrors agent-browser GET /api/jobs/:id/files response shape.
/// </summary>
public class JobProjectFiles
{
    public IReadOnlyList<JobProjectFile> Original { get; set; } = Array.Empty<JobProjectFile>();
    public IReadOnlyList<JobProjectFile> Chunks { get; set; } = Array.Empty<JobProjectFile>();
    public IReadOnlyList<JobProjectFile> ChunkJson { get; set; } = Array.Empty<JobProjectFile>();
    public IReadOnlyList<JobProjectFile> Transcripts { get; set; } = Array.Empty<JobProjectFile>();
    public IReadOnlyList<JobProjectFile> Intermediate { get; set; } = Array.Empty<JobProjectFile>();
    public IReadOnlyList<JobProjectFile> Converted { get; set; } = Array.Empty<JobProjectFile>();
    public IReadOnlyList<JobProjectFile> SplitChunks { get; set; } = Array.Empty<JobProjectFile>();
}

/// <summary>
/// One file entry with metadata for UI (play, line count, chunk index, etc.).
/// </summary>
public class JobProjectFile
{
    /// <summary>File name only (e.g. "2026-03-04 20.02.28 _part_000.wav").</summary>
    public string Name { get; set; } = "";

    /// <summary>Path relative to the job directory (e.g. "chunks/foo.wav"). Used for GET .../files/content?path=...</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>Absolute path on the server (optional; for logging or internal use).</summary>
    public string? FullPath { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>One of: "text", "audio", "other".</summary>
    public string Kind { get; set; } = "other";

    /// <summary>For text files: number of lines.</summary>
    public int? LineCount { get; set; }

    /// <summary>For audio files: duration in seconds.</summary>
    public double? DurationSeconds { get; set; }

    /// <summary>Chunk index for files under chunks/ or chunks_json/ (0-based).</summary>
    public int? Index { get; set; }

    /// <summary>Parent chunk index for split_chunks (0-based).</summary>
    public int? ParentIndex { get; set; }

    /// <summary>Sub-chunk index within parent (0-based).</summary>
    public int? SubIndex { get; set; }

    /// <summary>For split chunk audio: whether a transcript result exists for this sub-chunk.</summary>
    public bool? HasTranscript { get; set; }

    /// <summary>For split_chunks: true if this row is a transcript JSON, false if audio.</summary>
    public bool? IsTranscript { get; set; }
}
