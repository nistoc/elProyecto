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

    /// <summary>Extract a segment with stream copy (<c>-c copy</c>). Creates parent directory for <paramref name="outputPath"/>.</summary>
    void ExtractAudioSegmentCopy(string ffmpegPath, string inputPath, double startSeconds, double durationSeconds, string outputPath);

    /// <summary>Like <see cref="ExtractAudioSegmentCopy"/>; on ffmpeg failure, re-encodes to PCM s16le 16 kHz (agent01 split fallback).</summary>
    void ExtractAudioSegmentCopyOrReencode(string ffmpegPath, string inputPath, double startSeconds, double durationSeconds, string outputPath);

    /// <summary>Run ffmpeg <c>silencedetect</c> and return closed intervals <c>[start,end]</c>.</summary>
    IReadOnlyList<SilenceInterval> DetectSilence(string ffmpegPath, string inputPath, SilenceDetectOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replace each detected silence run with a short gap (<paramref name="keepSilenceSec"/>); non-silent audio is kept in order.
    /// Requires PCM-friendly segments; re-encodes slices to 16 kHz mono WAV. Output is WAV (pcm_s16le).
    /// If <paramref name="precomputedSilence"/> is set, skips an extra ffmpeg silencedetect pass.
    /// </summary>
    SilenceCompressionReport WriteWavWithCompressedSilence(
        string ffmpegPath,
        string ffprobePath,
        string inputPath,
        string outputWavPath,
        SilenceDetectOptions detectOptions,
        double keepSilenceSec,
        IReadOnlyList<SilenceInterval>? precomputedSilence = null,
        CancellationToken cancellationToken = default);
}
