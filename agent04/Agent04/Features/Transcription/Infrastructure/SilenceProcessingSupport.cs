using System.Linq;
using System.Text.Json;
using Agent04.Features.Transcription.Application;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>Logging and JSON sidecars for silence detect / compression.</summary>
internal static class SilenceProcessingSupport
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    internal static void LogSkipped(ILogger? logger) =>
        logger?.LogInformation(
            "Silence processing skipped: silence_detect_enabled and silence_compress_enabled are both false in job config. " +
            "Set silence_compress_enabled to true to shorten long silences before chunking (see converted_wav/*_silc.wav and *.silence_report.json).");

    internal static void LogDetectSummary(
        ILogger? logger,
        string workingPath,
        IReadOnlyList<SilenceInterval> intervals,
        SilenceJobSettings settings)
    {
        if (logger == null || !settings.DetectOn) return;
        var total = intervals.Sum(i => i.DurationSec);
        logger.LogInformation(
            "Silence detect: file={Path} regions={Count} totalSilenceSec={Total:F2} (noiseDb={Noise} minDur={MinDur} detection={Det})",
            workingPath,
            intervals.Count,
            total,
            settings.DetectOptions.NoiseDb,
            settings.DetectOptions.MinDurationSec,
            settings.DetectionLabel);
        foreach (var iv in intervals)
        {
            logger.LogDebug(
                "Silence region: {Start:F3}s – {End:F3}s duration={Dur:F3}s",
                iv.StartSec,
                iv.EndSec,
                iv.DurationSec);
        }
    }

    internal static void TryWriteDetectReportJson(
        ILogger? logger,
        string workingPath,
        string artifactRoot,
        IReadOnlyList<SilenceInterval> intervals,
        SilenceJobSettings settings)
    {
        if (!settings.WriteReport || !settings.DetectOn || settings.CompressOn)
            return;

        var detectDir = Path.GetDirectoryName(workingPath) ?? artifactRoot;
        var detectPath = Path.Combine(detectDir, Path.GetFileNameWithoutExtension(workingPath) + ".silence_detect.json");
        try
        {
            var doc = new
            {
                inputPath = workingPath,
                noiseDb = settings.DetectOptions.NoiseDb,
                minDurationSec = settings.DetectOptions.MinDurationSec,
                silence_detection = settings.DetectionLabel,
                regionCount = intervals.Count,
                regions = intervals.Select(i => new { startSec = i.StartSec, endSec = i.EndSec, durationSec = i.DurationSec }).ToList()
            };
            File.WriteAllText(detectPath, JsonSerializer.Serialize(doc, IndentedJson));
            logger?.LogInformation("Silence detect report JSON written to {Path}", detectPath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not write silence detect report to {Path}", detectPath);
        }
    }

    internal static string ResolveCompressedWavPath(string workingPath, string artifactRoot, SilenceJobSettings settings)
    {
        var baseName = Path.GetFileNameWithoutExtension(workingPath);
        var ext = Path.GetExtension(workingPath);
        var dir = Path.GetDirectoryName(workingPath) ?? artifactRoot;
        var outName = string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase)
            ? baseName + settings.OutputSuffix + ext
            : baseName + settings.OutputSuffix + ".wav";
        return Path.Combine(dir, outName);
    }

    internal static void LogCompressionResult(
        ILogger? logger,
        SilenceCompressionReport report,
        double keepSilenceSec)
    {
        if (logger == null) return;
        logger.LogInformation(
            "Silence compression: applied={Applied} regions={Regions} inputDuration={In:F2}s outputDuration={Out:F2}s totalSilenceDetected={Sil:F2}s estimatedShortened={Short:F2}s keepSilenceSec={Keep:F2}s output={OutFile}",
            report.AppliedCompression,
            report.SilenceRegions.Count,
            report.InputDurationSec,
            report.OutputDurationSec,
            report.TotalSilenceDurationSec,
            report.EstimatedShortenedSec,
            report.KeepSilenceSec,
            report.OutputPath);
        foreach (var iv in report.SilenceRegions)
        {
            logger.LogInformation(
                "  Compressed silence: {Start:F3}s – {End:F3}s was {Dur:F3}s -> kept {Keep:F3}s",
                iv.StartSec,
                iv.EndSec,
                iv.DurationSec,
                keepSilenceSec);
        }

        if (!report.AppliedCompression)
        {
            logger.LogInformation(
                "Silence compression produced no regions above threshold; output is a copy of input at {Out}",
                report.OutputPath);
        }
    }

    internal static void TryWriteCompressionReportJson(
        ILogger? logger,
        string outPath,
        string artifactRoot,
        SilenceCompressionReport report,
        SilenceJobSettings settings)
    {
        if (!settings.WriteReport)
            return;
        var reportPath = Path.Combine(
            Path.GetDirectoryName(outPath) ?? artifactRoot,
            Path.GetFileNameWithoutExtension(outPath) + ".silence_report.json");
        try
        {
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, IndentedJson));
            logger?.LogInformation("Silence compression report JSON written to {Path}", reportPath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not write silence compression report to {Path}", reportPath);
        }
    }
}
