using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XtractManager.Features.Sessions.Infrastructure;

/// <summary>
/// <c>track_source.json</c> in a job folder: which file of the recordings folder this job transcribes.
/// Written when a run is started from the lessons list; the audio in the job folder is a copy of that file.
/// </summary>
public static class TrackSourceFile
{
    public const string FileName = "track_source.json";
    public const int SchemaVersionLatest = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ResolvePath(string jobDirectoryPath) => Path.Combine(jobDirectoryPath, FileName);

    /// <summary>Null when the job was not started from the lessons list or the file cannot be read.</summary>
    public static TrackSource? TryLoad(string jobDirectoryPath)
    {
        try
        {
            var path = ResolvePath(jobDirectoryPath);
            if (!File.Exists(path))
                return null;
            var doc = JsonSerializer.Deserialize<TrackSource>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            return string.IsNullOrWhiteSpace(doc?.Track) ? null : doc;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task SaveAsync(string jobDirectoryPath, TrackSource source, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(
                ResolvePath(jobDirectoryPath),
                JsonSerializer.Serialize(source, JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                ct)
            .ConfigureAwait(false);
    }
}

public sealed class TrackSource
{
    public int SchemaVersion { get; set; } = TrackSourceFile.SchemaVersionLatest;

    /// <summary>Path of the track from the recordings root, with '/'.</summary>
    public string Track { get; set; } = "";

    /// <summary>Name and size of the source file: they find the track again after its folder was moved.</summary>
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }

    public string? Session { get; set; }
    public string? Period { get; set; }
    public string? Kind { get; set; }
    public string? Participant { get; set; }
    public string? CreatedAt { get; set; }
}
