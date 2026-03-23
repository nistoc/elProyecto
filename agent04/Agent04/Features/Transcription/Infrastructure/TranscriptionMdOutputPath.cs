using System.Globalization;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Resolves the job-level markdown path from <c>md_output_path</c> in transcription config.
/// When <paramref name="jobId"/> is set and the pattern does not contain <c>{jobId}</c>, a UTC suffix
/// <c>_yyyyMMdd_HHmmss_fff</c> is appended to the file stem so each completed write produces a new file
/// (older final transcripts remain on disk). This replaces the previous behaviour of appending the raw job id (often a GUID).
/// Placeholders: <c>{base}</c> (audio stem), <c>{jobId}</c>, <c>{timestamp}</c> (same UTC stamp as the auto suffix).
/// </summary>
public static class TranscriptionMdOutputPath
{
    public static string ResolveRelative(string pattern, string baseName, string? jobId)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        var s = pattern
            .Replace("{base}", baseName, StringComparison.OrdinalIgnoreCase)
            .Replace("{jobId}", jobId ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", stamp, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(jobId) && !pattern.Contains("{jobId}", StringComparison.OrdinalIgnoreCase))
        {
            if (!pattern.Contains("{timestamp}", StringComparison.OrdinalIgnoreCase))
            {
                s = Path.Combine(
                    Path.GetDirectoryName(s) ?? ".",
                    Path.GetFileNameWithoutExtension(s) + "_" + stamp + Path.GetExtension(s));
            }
        }

        return s;
    }
}
