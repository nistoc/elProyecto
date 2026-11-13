using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// <c>dialogue.json</c> in a dialogue job folder: which jobs are its participant tracks and every merge made
/// from them. Its presence is what marks a job folder as a dialogue.
/// </summary>
public static class DialogueManifestFile
{
    public const string FileName = "dialogue.json";
    public const int SchemaVersionLatest = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ResolvePath(string jobDirectoryPath) => Path.Combine(jobDirectoryPath, FileName);

    /// <summary>Null when the folder is not a dialogue or the file cannot be read.</summary>
    public static DialogueManifest? TryLoad(string jobDirectoryPath)
    {
        try
        {
            var path = ResolvePath(jobDirectoryPath);
            if (!File.Exists(path))
                return null;
            var doc = JsonSerializer.Deserialize<DialogueManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            return doc?.Tracks is { Count: > 0 } ? doc : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Title of the dialogue for job lists; null when the folder is not a dialogue.</summary>
    public static string? TryReadTitle(string jobDirectoryPath)
    {
        var title = TryLoad(jobDirectoryPath)?.Title;
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    public static async Task SaveAsync(string jobDirectoryPath, DialogueManifest manifest, CancellationToken ct = default)
    {
        var path = ResolvePath(jobDirectoryPath);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false), ct)
            .ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class DialogueManifest
{
    public int SchemaVersion { get; set; } = DialogueManifestFile.SchemaVersionLatest;
    public string? Title { get; set; }
    public string? CreatedAt { get; set; }

    /// <summary>Participant tracks as requested; a null <see cref="DialogueManifestTrack.File"/> means "newest transcript at merge time".</summary>
    public List<DialogueManifestTrack> Tracks { get; set; } = new();

    /// <summary>One record per merge, oldest first.</summary>
    public List<DialogueManifestMerge> Merges { get; set; } = new();
}

public sealed class DialogueManifestTrack
{
    public string JobId { get; set; } = "";
    public string Speaker { get; set; } = "";
    public string? File { get; set; }
}

public sealed class DialogueManifestMerge
{
    public string File { get; set; } = "";
    public string? CreatedAt { get; set; }
    public int Entries { get; set; }
    public List<DialogueManifestMergeTrack> Tracks { get; set; } = new();
    public List<string>? Warnings { get; set; }
}

public sealed class DialogueManifestMergeTrack
{
    public string JobId { get; set; } = "";
    public string Speaker { get; set; } = "";

    /// <summary>Transcript file that was actually read from the track's job folder.</summary>
    public string File { get; set; } = "";
    public int Entries { get; set; }
    public int UntimedEntries { get; set; }
}
