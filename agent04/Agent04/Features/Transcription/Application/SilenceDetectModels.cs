namespace Agent04.Features.Transcription.Application;

/// <summary>One contiguous silence region reported by ffmpeg silencedetect.</summary>
public sealed record SilenceInterval(double StartSec, double EndSec, double DurationSec);

/// <summary>Parameters for ffmpeg <c>silencedetect</c> (noise in dB, minimum run length, peak vs RMS).</summary>
public sealed record SilenceDetectOptions(double NoiseDb = -40, double MinDurationSec = 0.3, bool UseRms = true);

/// <summary>Result of <see cref="IAudioUtils.WriteWavWithCompressedSilence"/> for logging and sidecar JSON.</summary>
public sealed class SilenceCompressionReport
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public double InputDurationSec { get; init; }
    public double OutputDurationSec { get; init; }
    public double KeepSilenceSec { get; init; }
    /// <summary>Merged silence intervals that were replaced (each shortened to <see cref="KeepSilenceSec"/>).</summary>
    public IReadOnlyList<SilenceInterval> SilenceRegions { get; init; } = Array.Empty<SilenceInterval>();
    public double TotalSilenceDurationSec { get; init; }
    /// <summary>Approximate time cut: sum of silence lengths minus <c>regions × keep</c>.</summary>
    public double EstimatedShortenedSec { get; init; }
    public bool AppliedCompression { get; init; }
}
