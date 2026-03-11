namespace Agent04.Features.Transcription.Domain;

/// <summary>
/// Represents a single transcription segment with timing and speaker info.
/// </summary>
public sealed record ASRSegment(
    double Start,
    double End,
    string Text,
    string? Speaker = null
);
