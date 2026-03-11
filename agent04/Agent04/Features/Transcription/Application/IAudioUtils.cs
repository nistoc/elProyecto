namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Utilities for working with audio files (ffmpeg/ffprobe).
/// </summary>
public interface IAudioUtils
{
    /// <summary>Resolve executable path from config or PATH.</summary>
    string? WhichOr(string? pathKey, string defaultName);

    /// <summary>Get audio duration (seconds) and file size (bytes) using ffprobe.</summary>
    (double DurationSeconds, long SizeBytes) GetDurationAndSize(string ffprobePath, string filePath);

    /// <summary>Calculate optimal segment duration to achieve target chunk size.</summary>
    int CalculateSegmentTime(long sizeBytes, double durationSec, double targetMb);

    /// <summary>Re-encode audio file if it exceeds target size. Returns path to (possibly new) file.</summary>
    string ReencodeIfNeeded(string ffmpegPath, string inputPath, double targetMb, int bitrateKbps);

    /// <summary>Format bytes as MB string.</summary>
    string FormatMb(long numBytes);

    /// <summary>Convert audio file to WAV (16kHz, mono, PCM). Returns path to WAV file.</summary>
    string ConvertToWav(string ffmpegPath, string inputPath, string? outputDir = null);
}
