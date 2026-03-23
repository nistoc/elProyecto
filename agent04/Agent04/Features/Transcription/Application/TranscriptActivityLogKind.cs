namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Severity for chunk VM <c>transcript_activity_log</c> lines (optional prefix after ISO timestamp).
/// </summary>
public enum TranscriptActivityLogKind
{
    Information = 0,
    Warning = 1,
    Error = 2
}
