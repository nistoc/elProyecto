using System.Text.Json;
using System.Text.Json.Serialization;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// <c>timeline/speech_map.json</c> in the job artifact root: the pieces of the source audio that silence
/// compression kept and where each stands in the compressed file. Chunks are cut from the compressed file,
/// so every writer of transcript times reads this file to print times on the recording's timeline.
/// Absent when the job was transcribed without compression (times need no translation).
/// </summary>
public static class SpeechTimeMapFile
{
    public const string FileName = "speech_map.json";
    public const int SchemaVersionLatest = 1;

    public static string ResolvePath(string artifactRoot) =>
        Path.Combine(artifactRoot, ChunkTimelineFile.FolderName, FileName);

    public static async Task SaveAsync(string artifactRoot, SilenceCompressionReport report, CancellationToken ct = default)
    {
        var doc = new SpeechTimeMapDocument
        {
            SchemaVersion = SchemaVersionLatest,
            SourceDurationSec = report.InputDurationSec,
            OutputDurationSec = report.OutputDurationSec,
            KeepSilenceSec = report.KeepSilenceSec,
            Pieces = report.SpeechPieces
                .Select(p => new SpeechTimeMapPiece
                {
                    SourceStartSec = p.SourceStartSec,
                    SourceEndSec = p.SourceEndSec,
                    OutputStartSec = p.OutputStartSec
                })
                .ToList()
        };

        var path = ResolvePath(artifactRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(doc, TranscriptionJsonSerializerOptions.Indented), ct)
            .ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Null when the job has no map (no compression, or a job older than the map) or it cannot be read.</summary>
    public static async Task<SpeechTimeMap?> TryLoadAsync(string artifactRoot, CancellationToken ct = default)
    {
        var path = ResolvePath(artifactRoot);
        if (!File.Exists(path))
            return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var doc = JsonSerializer.Deserialize<SpeechTimeMapDocument>(json);
            if (doc?.Pieces is not { Count: > 0 })
                return null;
            return new SpeechTimeMap(doc.Pieces.Select(p => new SpeechPiece(p.SourceStartSec, p.SourceEndSec, p.OutputStartSec)));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes the map of an earlier run. A run that sends the audio uncompressed must not leave a stale
    /// map behind: its times are already on the recording's timeline.
    /// </summary>
    public static void Delete(string artifactRoot)
    {
        var path = ResolvePath(artifactRoot);
        if (File.Exists(path))
            File.Delete(path);
    }
}

public sealed class SpeechTimeMapDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("source_duration_sec")]
    public double SourceDurationSec { get; set; }

    [JsonPropertyName("output_duration_sec")]
    public double OutputDurationSec { get; set; }

    [JsonPropertyName("keep_silence_sec")]
    public double KeepSilenceSec { get; set; }

    [JsonPropertyName("pieces")]
    public List<SpeechTimeMapPiece> Pieces { get; set; } = new();
}

public sealed class SpeechTimeMapPiece
{
    [JsonPropertyName("source_start_sec")]
    public double SourceStartSec { get; set; }

    [JsonPropertyName("source_end_sec")]
    public double SourceEndSec { get; set; }

    [JsonPropertyName("output_start_sec")]
    public double OutputStartSec { get; set; }
}
