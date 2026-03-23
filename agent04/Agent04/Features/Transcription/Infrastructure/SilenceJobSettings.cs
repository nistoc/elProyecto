using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>Silence detect/compress flags and parameters read from <see cref="TranscriptionConfig"/>.</summary>
internal readonly record struct SilenceJobSettings(
    bool DetectOn,
    bool CompressOn,
    bool WriteReport,
    SilenceDetectOptions DetectOptions,
    string DetectionLabel,
    double KeepSilenceSec,
    string OutputSuffix)
{
    public bool ShouldRun => DetectOn || CompressOn;

    public static SilenceJobSettings FromConfig(TranscriptionConfig config)
    {
        var noiseDb = config.Get<double?>("silence_noise_db") ?? -40;
        var minDur = config.Get<double?>("silence_min_duration_sec") ?? 0.3;
        var det = (config.Get<string>("silence_detection") ?? "rms").Trim();
        var useRms = !det.Equals("peak", StringComparison.OrdinalIgnoreCase);
        var keep = config.Get<double?>("silence_keep_sec") ?? 0.2;
        if (keep < 0)
            keep = 0;

        return new SilenceJobSettings(
            config.Get<bool?>("silence_detect_enabled") == true,
            config.Get<bool?>("silence_compress_enabled") == true,
            config.Get<bool?>("silence_write_report_json") != false,
            new SilenceDetectOptions(noiseDb, minDur, useRms),
            det,
            keep,
            config.Get<string>("silence_output_suffix") ?? "_silc");
    }
}
