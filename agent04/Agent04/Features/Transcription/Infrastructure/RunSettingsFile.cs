using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// <c>run_settings.json</c> in the job folder: the settings the job actually started with — the shared
/// config with the job's own settings on top. Written when the job is accepted, so the orchestrator can show
/// them later even after the shared config has changed.
/// <code>{ "schema_version": 1, "model": "gpt-4o-transcribe-diarize", "silence_compress_enabled": true,
/// "silence_keep_sec": 1.0, "from_job_settings": ["silence_keep_sec"] }</code>
/// </summary>
public static class RunSettingsFile
{
    public const string FileName = "run_settings.json";
    public const int SchemaVersionLatest = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ResolvePath(string artifactRoot) => Path.Combine(artifactRoot, FileName);

    /// <param name="config">The config the job runs with (job settings already applied).</param>
    /// <param name="fromJobSettings">Keys that came from <c>job_settings.json</c> of this job.</param>
    public static RunSettingsDocument Build(
        TranscriptionConfig config,
        IEnumerable<string> fromJobSettings,
        DateTimeOffset writtenAt)
    {
        var silence = SilenceJobSettings.FromConfig(config);
        return new RunSettingsDocument
        {
            SchemaVersion = SchemaVersionLatest,
            WrittenAt = writtenAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            Model = config.Model,
            SilenceCompressEnabled = silence.CompressOn,
            SilenceKeepSec = silence.KeepSilenceSec,
            FromJobSettings = fromJobSettings.OrderBy(k => k, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>
    /// Writes the file. A failure is logged and swallowed: the record is for the reader, a job must not fail
    /// because it could not be written.
    /// </summary>
    public static void TrySave(string artifactRoot, TranscriptionConfig config, ILogger? logger = null)
    {
        try
        {
            var doc = Build(config, JobSettingsFile.ReadKeys(artifactRoot), DateTimeOffset.UtcNow);
            var path = ResolvePath(artifactRoot);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger?.LogWarning(ex, "Could not write {File} to {Root}", FileName, artifactRoot);
        }
    }
}

public sealed class RunSettingsDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("written_at")]
    public string? WrittenAt { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("silence_compress_enabled")]
    public bool SilenceCompressEnabled { get; set; }

    /// <summary>The pause that is kept; it applies only when <see cref="SilenceCompressEnabled"/> is true.</summary>
    [JsonPropertyName("silence_keep_sec")]
    public double SilenceKeepSec { get; set; }

    [JsonPropertyName("from_job_settings")]
    public List<string> FromJobSettings { get; set; } = new();
}
