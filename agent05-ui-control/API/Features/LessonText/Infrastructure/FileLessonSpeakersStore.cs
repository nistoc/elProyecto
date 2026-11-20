using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.LessonPackages.Infrastructure;
using XtractManager.Features.LessonText.Application;

namespace XtractManager.Features.LessonText.Infrastructure;

/// <summary>
/// The roles of the speakers of a lesson in <c>{packages}/{YYYY-MM-DD}/speakers.json</c>, next to the package
/// of the lesson: <c>{ "schema_version": 1, "recordings": { "&lt;recording id&gt;": { "&lt;speaker&gt;": "teacher" } } }</c>.
/// The file may be edited by hand. One that cannot be read is never written over, and what a write does
/// not know (other fields, a role it does not name) stays as it was.
/// </summary>
public sealed class FileLessonSpeakersStore : ILessonSpeakersStore
{
    public const string FileName = "speakers.json";
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _root;
    private readonly object _gate = new();

    public FileLessonSpeakersStore(IConfiguration configuration, IHostEnvironment environment, IJobWorkspace workspace) =>
        _root = FileLessonPackageStore.ResolveRoot(configuration, environment, workspace);

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Roles(string date)
    {
        lock (_gate)
        {
            var roles = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            if (Load(date)?["recordings"] is not JsonObject recordings)
                return roles;
            foreach (var (recording, value) in recordings)
            {
                if (value is not JsonObject speakers)
                    continue;
                var ofRecording = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (speaker, role) in speakers)
                {
                    if (role is JsonValue given && given.TryGetValue<string>(out var name) && LessonRole.IsRole(name))
                        ofRecording[speaker] = name;
                }

                if (ofRecording.Count > 0)
                    roles[recording] = ofRecording;
            }

            return roles;
        }
    }

    public void SetRole(string date, string recording, string speaker, string? role)
    {
        lock (_gate)
        {
            var doc = Load(date) ?? new JsonObject();
            doc["schema_version"] ??= SchemaVersion;
            if (doc["recordings"] is not JsonObject recordings)
                doc["recordings"] = recordings = new JsonObject();
            if (recordings[recording] is not JsonObject speakers)
            {
                if (role == null)
                    return;
                recordings[recording] = speakers = new JsonObject();
            }

            var known = speakers[speaker] is JsonValue was && was.TryGetValue<string>(out var before) ? before : null;
            if (known == role)
                return;
            if (role == null)
                speakers.Remove(speaker);
            else
                speakers[speaker] = role;
            if (speakers.Count == 0)
                recordings.Remove(recording);
            Write(date, doc);
        }
    }

    private string PathOf(string date) => Path.Combine(_root, date, FileName);

    private JsonObject? Load(string date)
    {
        var path = PathOf(date);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8), documentOptions: ReadOptions) as JsonObject
                   ?? throw new JsonException("the file holds no object");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{FileName} of the lesson of {date} cannot be read ({ex.Message}); it was left as it is.", ex);
        }
    }

    /// <summary>Written next to the file and moved over it, so a reader never meets half a file.</summary>
    private void Write(string date, JsonObject doc)
    {
        var path = PathOf(date);
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, doc.ToJsonString(WriteOptions), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The next write goes over it.
            }

            throw new IOException(
                $"{FileName} of the lesson of {date} could not be written ({ex.Message}); it holds what it held before.", ex);
        }
    }
}
