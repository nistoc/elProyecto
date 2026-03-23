namespace Agent04.Features.Transcription.Domain;

/// <summary>
/// Information about a single audio chunk.
/// </summary>
public sealed record ChunkInfo(
    string Path,
    double Offset,
    double EmitGuard,
    string? Fingerprint = null
);
