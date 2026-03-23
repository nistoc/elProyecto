using System.Globalization;

namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Single place to build human-readable lines for <c>transcript_activity_log</c> (Agent04 → gRPC → UI string field).
/// </summary>
public static class TranscriptActivityLogFormatter
{
    public static string FormatLine(string messageBodyWithoutTimestamp, TranscriptActivityLogKind kind = TranscriptActivityLogKind.Information)
    {
        var sev = kind switch
        {
            TranscriptActivityLogKind.Warning => "[warn] ",
            TranscriptActivityLogKind.Error => "[err] ",
            _ => ""
        };
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:o} {1}{2}",
            DateTimeOffset.UtcNow,
            sev,
            messageBodyWithoutTimestamp.Trim());
    }

    /// <summary>
    /// Pipeline-only: optional trailing checkmark when the body indicates a successful HTTP transcribe line.
    /// </summary>
    public static string FormatPipelineChunkLine(string messageBody, bool appendOkEmoji = true)
    {
        var suffix = appendOkEmoji && messageBody.Contains("Transcribe HTTP OK", StringComparison.Ordinal)
            ? " ✅"
            : "";
        return FormatLine(messageBody + suffix, TranscriptActivityLogKind.Information);
    }
}
