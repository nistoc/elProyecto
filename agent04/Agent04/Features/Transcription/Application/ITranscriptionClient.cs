using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Client for transcription API (OpenAI-compatible) with retry and fallback model support.
/// </summary>
public interface ITranscriptionClient
{
    /// <summary>
    /// Transcribe audio file. Returns raw API response (e.g. dict-like) and parsed segments.
    /// </summary>
    Task<TranscriptionClientResult> TranscribeAsync(
        string audioPath,
        TranscriptionClientOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class TranscriptionClientOptions
{
    /// <summary>Optional. Model for this call (the job's own setting); without it the client's model is used.</summary>
    public string? Model { get; set; }
    public string? Language { get; set; }
    public string? Prompt { get; set; }
    public double? Temperature { get; set; }
    public string? ResponseFormat { get; set; }
    public string? ChunkingStrategy { get; set; }
    /// <summary>Optional. For structured logs (chunk index in parallel transcription).</summary>
    public int? ChunkIndex { get; set; }
    /// <summary>Optional. Sub-chunk index when transcribing split_chunks sub-file (parent index is <see cref="ChunkIndex"/>).</summary>
    public int? SubChunkIndex { get; set; }
    /// <summary>Optional. Agent04 job id for logs.</summary>
    public string? AgentJobId { get; set; }
    /// <summary>Optional. Configured <c>parallel_transcription_workers</c> for logs.</summary>
    public int? ParallelWorkersConfigured { get; set; }
    /// <summary>
    /// After three server errors (HTTP 5xx) on the first request — the caller's own model and parameters —
    /// throw <see cref="TranscriptionServerErrorException"/> instead of going down the fallback list
    /// (whose next request has no <c>response_format</c>: text without time marks from a diarize model).
    /// </summary>
    public bool StopAtServerError { get; set; }

    public TranscriptionClientOptions Clone() => (TranscriptionClientOptions)MemberwiseClone();
}

public sealed class TranscriptionClientResult
{
    public IReadOnlyDictionary<string, object?> RawResponse { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyList<ASRSegment> Segments { get; init; } = Array.Empty<ASRSegment>();
}
