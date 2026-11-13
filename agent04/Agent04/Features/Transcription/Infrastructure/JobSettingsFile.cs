using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Settings the operator chose for one job: <c>job_settings.json</c> in the job folder, written by the
/// orchestrator at upload. They go on top of the shared job config (<c>config/default.json</c>), so one
/// recording can be transcribed with another pause length or model without editing the shared file.
/// <code>{ "schema_version": 1, "transcription": { "silence_keep_sec": 1.0 } }</code>
/// Only the keys of <see cref="AllowedKeys"/> are accepted. A file that cannot be read as such is an error,
/// not "no settings": a job must not silently run with other settings than the operator asked for.
/// </summary>
public static class JobSettingsFile
{
    public const string FileName = "job_settings.json";
    public const int SchemaVersion = 1;

    public const double MinSilenceKeepSec = 0.05;
    public const double MaxSilenceKeepSec = 5.0;

    public static readonly IReadOnlyList<string> AllowedKeys = new[]
    {
        "silence_compress_enabled",
        "silence_keep_sec",
        "model",
    };

    private static readonly Regex ModelId = new(@"^[A-Za-z0-9][A-Za-z0-9._:\-]{0,79}$", RegexOptions.CultureInvariant);

    public static string ResolvePath(string artifactRoot) => Path.Combine(artifactRoot, FileName);

    /// <summary>
    /// The shared config with the job's own settings on top. Without the file the shared config is returned as is.
    /// </summary>
    /// <exception cref="InvalidDataException">The file exists and is not a valid settings file.</exception>
    public static TranscriptionConfig Apply(TranscriptionConfig shared, string artifactRoot, ILogger? logger = null)
    {
        var path = ResolvePath(artifactRoot);
        if (!File.Exists(path))
            return shared;

        string json;
        try
        {
            json = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{FileName}: cannot be read ({ex.Message})", ex);
        }

        var overrides = ParseOverrides(json);
        if (overrides.Count == 0)
            return shared;

        var applied = WithModelDefaults(shared, overrides);
        logger?.LogInformation(
            "Job settings from {File}: {Settings}",
            path,
            string.Join(", ", applied.Select(kv => kv.Key + "=" + FormatValue(kv.Value))));
        return shared.WithOverrides(applied);
    }

    /// <summary>
    /// Reads the <c>transcription</c> object of a settings file into config keys.
    /// </summary>
    /// <exception cref="InvalidDataException">Not JSON, an unknown key, or a value out of range.</exception>
    public static IReadOnlyDictionary<string, object?> ParseOverrides(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{FileName}: not valid JSON ({ex.Message})", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{FileName}: the root must be an object");

            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (!root.TryGetProperty("transcription", out var transcription) || transcription.ValueKind == JsonValueKind.Null)
                return result;
            if (transcription.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{FileName}: 'transcription' must be an object");

            foreach (var p in transcription.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "silence_compress_enabled":
                        if (p.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            throw new InvalidDataException($"{FileName}: 'silence_compress_enabled' must be true or false");
                        result[p.Name] = p.Value.GetBoolean();
                        break;

                    case "silence_keep_sec":
                        if (p.Value.ValueKind != JsonValueKind.Number
                            || !p.Value.TryGetDouble(out var keep)
                            || double.IsNaN(keep)
                            || keep < MinSilenceKeepSec
                            || keep > MaxSilenceKeepSec)
                        {
                            throw new InvalidDataException(string.Create(
                                CultureInfo.InvariantCulture,
                                $"{FileName}: 'silence_keep_sec' must be a number from {MinSilenceKeepSec} to {MaxSilenceKeepSec}"));
                        }

                        result[p.Name] = keep;
                        break;

                    case "model":
                        var model = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "").Trim() : "";
                        if (!ModelId.IsMatch(model))
                            throw new InvalidDataException($"{FileName}: 'model' must be a model id (letters, digits, '.', '_', ':', '-')");
                        result[p.Name] = model;
                        break;

                    default:
                        throw new InvalidDataException(
                            $"{FileName}: unknown setting '{p.Name}' (allowed: {string.Join(", ", AllowedKeys)})");
                }
            }

            return result;
        }
    }

    /// <summary>
    /// The answer format a model can give with time marks, and the chunking it needs:
    /// the diarize model answers <c>diarized_json</c> and wants <c>chunking_strategy</c>;
    /// whisper answers <c>verbose_json</c> (segments with times, no speakers); the others answer plain <c>json</c>.
    /// </summary>
    public static (string ResponseFormat, string? ChunkingStrategy) FormatFor(string model)
    {
        if (model.Contains("diarize", StringComparison.OrdinalIgnoreCase))
            return ("diarized_json", "auto");
        if (model.Contains("whisper", StringComparison.OrdinalIgnoreCase))
            return ("verbose_json", null);
        return ("json", null);
    }

    /// <summary>
    /// A job that names another model than the shared config also gets that model's answer format:
    /// the format of the shared config belongs to the shared model and another model rejects it.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> WithModelDefaults(
        TranscriptionConfig shared,
        IReadOnlyDictionary<string, object?> overrides)
    {
        if (!overrides.TryGetValue("model", out var m) || m is not string model
            || string.Equals(model, shared.Model, StringComparison.OrdinalIgnoreCase))
            return overrides;

        var (format, chunking) = FormatFor(model);
        return new Dictionary<string, object?>(overrides, StringComparer.Ordinal)
        {
            ["response_format"] = format,
            ["chunking_strategy"] = chunking,
        };
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "(none)",
        bool b => b ? "true" : "false",
        double d => d.ToString("G", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
