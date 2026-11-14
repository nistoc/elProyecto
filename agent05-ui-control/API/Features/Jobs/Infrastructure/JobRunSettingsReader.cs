using System.Text.Json;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Reads what a job was transcribed with from its folder. The folder is only read.
/// <list type="number">
/// <item><c>run_settings.json</c> — written by the transcriber when it accepts the job; exact.</item>
/// <item>A job from before that record: <c>timeline/speech_map.json</c> or
/// <c>converted_wav/*.silence_report.json</c> say that pauses were cut out and how much of a pause was kept;
/// a <c>converted_wav/*.silence_detect.json</c> alone says they were looked for and not cut out. The model is
/// known only when the operator set it in <c>job_settings.json</c>.</item>
/// <item>A job that has not run yet: the operator's <c>job_settings.json</c>.</item>
/// </list>
/// </summary>
public static class JobRunSettingsReader
{
    public const string RunSettingsFileName = "run_settings.json";

    private static readonly string[] SettingKeys = { "silence_compress_enabled", "silence_keep_sec", "model" };

    /// <summary>Null when the folder tells nothing (or is not a transcription job at all).</summary>
    public static JobRunSettings? Read(string jobDirectoryPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jobDirectoryPath) || !Directory.Exists(jobDirectoryPath))
                return null;

            return FromRunRecord(jobDirectoryPath) ?? FromWhatTheRunLeft(jobDirectoryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JobRunSettings? FromRunRecord(string dir)
    {
        using var doc = TryParse(Path.Combine(dir, RunSettingsFileName));
        if (doc == null)
            return null;

        var root = doc.RootElement;
        var compress = Bool(root, "silence_compress_enabled");
        var keys = root.TryGetProperty("from_job_settings", out var from) && from.ValueKind == JsonValueKind.Array
            ? from.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(k => SettingKeys.Contains(k, StringComparer.Ordinal))
                .ToList()
            : new List<string>();

        return new JobRunSettings(
            JobRunSettings.SourceRun,
            Text(root, "model"),
            compress,
            compress == true ? Number(root, "silence_keep_sec") : null,
            keys);
    }

    private static JobRunSettings? FromWhatTheRunLeft(string dir)
    {
        // The operator's settings: what the transcriber is told to do.
        string? model = null;
        bool? compress = null;
        double? keep = null;
        var keys = new List<string>();
        using (var jobSettings = TryParse(Path.Combine(dir, JobSettingsDocument.FileName)))
        {
            if (jobSettings != null
                && jobSettings.RootElement.TryGetProperty("transcription", out var t)
                && t.ValueKind == JsonValueKind.Object)
            {
                keys = t.EnumerateObject()
                    .Select(p => p.Name)
                    .Where(k => SettingKeys.Contains(k, StringComparer.Ordinal))
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();
                model = Text(t, "model");
                compress = Bool(t, "silence_compress_enabled");
                keep = Number(t, "silence_keep_sec");
            }
        }

        // What the run left: stronger than the intent, it is what happened.
        var ran = false;
        var keptPause = KeptPauseOfTheRun(dir);
        if (keptPause != null)
        {
            ran = true;
            compress = true;
            keep = keptPause;
        }
        else if (HasConvertedFile(dir, ".silence_detect.json"))
        {
            ran = true;
            compress = false;
        }

        // Without compression there is no kept pause. When compression itself is not known (a job that has
        // not run and takes it from the shared config), the pause the operator set is still shown.
        if (compress == false)
            keep = null;
        if (!ran && keys.Count == 0)
            return null;

        return new JobRunSettings(
            ran ? JobRunSettings.SourceArtifacts : JobRunSettings.SourceJobSettings,
            model,
            compress,
            keep,
            keys);
    }

    /// <summary>The pause kept by the silence compression of this job; null when nothing was compressed.</summary>
    private static double? KeptPauseOfTheRun(string dir)
    {
        using (var map = TryParse(Path.Combine(dir, "timeline", "speech_map.json")))
        {
            if (map != null && Number(map.RootElement, "keep_silence_sec") is { } fromMap)
                return fromMap;
        }

        var converted = new DirectoryInfo(Path.Combine(dir, "converted_wav"));
        if (!converted.Exists)
            return null;

        foreach (var report in converted.EnumerateFiles("*.silence_report.json").OrderByDescending(f => f.LastWriteTimeUtc))
        {
            using var doc = TryParse(report.FullName);
            if (doc != null && Number(doc.RootElement, "KeepSilenceSec") is { } fromReport)
                return fromReport;
        }

        return null;
    }

    private static bool HasConvertedFile(string dir, string suffix)
    {
        var converted = new DirectoryInfo(Path.Combine(dir, "converted_wav"));
        return converted.Exists
               && converted.EnumerateFiles("*" + suffix).Any();
    }

    private static JsonDocument? TryParse(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return doc;
            doc.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A file that is being written right now, or is not JSON: the same as no file.
            return null;
        }
    }

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static bool? Bool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    private static double? Number(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                                             && v.TryGetDouble(out var d) && !double.IsNaN(d) && d >= 0
            ? d
            : null;
}
