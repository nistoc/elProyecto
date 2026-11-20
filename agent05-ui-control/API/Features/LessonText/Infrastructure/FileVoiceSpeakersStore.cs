using System.Text;
using System.Text.Json;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.LessonText.Application;

namespace XtractManager.Features.LessonText.Infrastructure;

/// <summary>
/// The answers of the voice tool (<c>voice-speakers/</c> of the repository) as <c>{root}/{jobId}.json</c>, the job
/// being the one whose audio file the tool read. Only read here. The root is <c>VoiceSpeakers:RootPath</c>; without
/// the key it is the folder <c>voice-speakers</c> next to the jobs folder — next to it and not inside, because
/// every folder inside the jobs folder is listed as a job.
/// </summary>
public sealed class FileVoiceSpeakersStore : IVoiceSpeakersStore
{
    public const string RootPathKey = "VoiceSpeakers:RootPath";
    public const string DefaultFolderName = "voice-speakers";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _root;

    public FileVoiceSpeakersStore(IConfiguration configuration, IHostEnvironment environment, IJobWorkspace workspace)
    {
        var raw = configuration[RootPathKey]?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            var jobs = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.WorkspaceRootPath));
            _root = Path.Combine(Path.GetDirectoryName(jobs) ?? jobs, DefaultFolderName);
        }
        else
        {
            _root = Path.IsPathRooted(raw)
                ? Path.GetFullPath(raw)
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath ?? Directory.GetCurrentDirectory(), raw));
        }
    }

    public string RootPath => _root;

    /// <summary>The file of a job's answer, whether it exists or not.</summary>
    public static string FileName(string jobId) => jobId + ".json";

    public IReadOnlyList<VoiceSegment>? For(string jobId)
    {
        if (string.IsNullOrEmpty(jobId) || jobId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || jobId.Contains(".."))
            return null;
        var path = Path.Combine(_root, FileName(jobId));
        try
        {
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8), ReadOptions);
            if (!doc.RootElement.TryGetProperty("segments", out var list) || list.ValueKind != JsonValueKind.Array)
                return null;
            var segments = new List<VoiceSegment>();
            foreach (var s in list.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object
                    || !s.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.Number
                    || !s.TryGetProperty("end", out var end) || end.ValueKind != JsonValueKind.Number
                    || !s.TryGetProperty("speaker", out var speaker) || speaker.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(speaker.GetString()))
                    continue;
                if (end.GetDouble() > start.GetDouble())
                    segments.Add(new VoiceSegment(start.GetDouble(), end.GetDouble(), speaker.GetString()!));
            }

            return segments.Count == 0 ? null : segments.OrderBy(s => s.Start).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
