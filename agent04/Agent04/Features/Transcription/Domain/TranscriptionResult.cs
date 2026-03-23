namespace Agent04.Features.Transcription.Domain;

/// <summary>
/// Result of a single chunk transcription.
/// </summary>
public sealed record TranscriptionResult(
    string ChunkBasename,
    double Offset,
    double EmitGuard,
    IReadOnlyList<ASRSegment> Segments,
    IReadOnlyDictionary<string, object?> RawResponse
);
