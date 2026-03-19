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
    public string? Language { get; set; }
    public string? Prompt { get; set; }
    public double? Temperature { get; set; }
    public string? ResponseFormat { get; set; }
    public string? ChunkingStrategy { get; set; }
    /// <summary>Optional. For structured logs (chunk index in parallel transcription).</summary>
    public int? ChunkIndex { get; set; }
    /// <summary>Optional. Agent04 job id for logs.</summary>
    public string? AgentJobId { get; set; }
    /// <summary>Optional. Configured <c>parallel_transcription_workers</c> for logs.</summary>
    public int? ParallelWorkersConfigured { get; set; }
}

public sealed class TranscriptionClientResult
{
    public IReadOnlyDictionary<string, object?> RawResponse { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyList<ASRSegment> Segments { get; init; } = Array.Empty<ASRSegment>();
}
