using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.LessonPackages.Application;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Features.LessonPackages.Infrastructure;

/// <summary>
/// The packages of the lessons on disk: <c>{root}/{YYYY-MM-DD}/package.json</c> (written by the builder,
/// only read here) and <c>marks.json</c> next to it (the owner's edits, written here). The root is
/// <c>LessonPackages:RootPath</c>; without the key it is the folder <c>lesson-packages</c> next to the jobs
/// folder — next to it and not inside, because every folder inside the jobs folder is listed as a job.
/// </summary>
public sealed class FileLessonPackageStore : ILessonPackageStore
{
    public const string RootPathKey = "LessonPackages:RootPath";
    public const string DefaultFolderName = "lesson-packages";
    public const string PackageFileName = "package.json";
    public const string MarksFileName = "marks.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _root;
    private readonly ILogger<FileLessonPackageStore> _logger;
    private readonly object _gate = new();

    public FileLessonPackageStore(
        IConfiguration configuration,
        IHostEnvironment environment,
        IJobWorkspace workspace,
        ILogger<FileLessonPackageStore> logger)
    {
        _logger = logger;
        _root = ResolveRoot(configuration, environment, workspace);
        _logger.LogInformation("Lesson packages folder ({Key}): {Path}", RootPathKey, _root);
    }

    /// <summary>The packages folder: <see cref="RootPathKey"/>, or <see cref="DefaultFolderName"/> next to the jobs folder.</summary>
    public static string ResolveRoot(IConfiguration configuration, IHostEnvironment environment, IJobWorkspace workspace)
    {
        var raw = configuration[RootPathKey]?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            var jobs = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.WorkspaceRootPath));
            return Path.Combine(Path.GetDirectoryName(jobs) ?? jobs, DefaultFolderName);
        }

        return Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath ?? Directory.GetCurrentDirectory(), raw));
    }

    public string RootPath => _root;

    public IReadOnlyDictionary<string, LessonPackageInfo> List()
    {
        var found = new Dictionary<string, LessonPackageInfo>(StringComparer.Ordinal);
        try
        {
            if (!Directory.Exists(_root))
                return found;
            foreach (var dir in new DirectoryInfo(_root).EnumerateDirectories())
            {
                if (!LessonPageRules.TryDate(dir.Name, out var date))
                    continue;
                var file = new FileInfo(Path.Combine(dir.FullName, PackageFileName));
                if (file.Exists)
                    found[date] = new LessonPackageInfo(Stamp(file.LastWriteTimeUtc));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The lesson packages folder {Path} cannot be listed; the lessons show no package", _root);
        }

        return found;
    }

    public LessonPackageDocument? Read(string date)
    {
        var path = Path.Combine(_root, date, PackageFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            var doc = JsonSerializer.Deserialize<LessonPackageDocument>(File.ReadAllText(path, Encoding.UTF8), ReadOptions)
                      ?? throw new JsonException("the file holds no object");
            if (doc.SchemaVersion > LessonPackageDocument.SchemaVersionLatest)
                throw new JsonException($"schema_version {doc.SchemaVersion} is newer than this service reads ({LessonPackageDocument.SchemaVersionLatest})");
            doc.Lesson ??= date;
            doc.BuiltAt ??= Stamp(File.GetLastWriteTimeUtc(path));
            // A list written as null reads as an empty one.
            doc.Periods ??= new();
            doc.Topics ??= new();
            doc.Grammar ??= new();
            doc.WordNotes ??= new();
            doc.Examples ??= new();
            doc.NewWords ??= new();
            doc.Vocabulary ??= new();
            doc.OtherWords ??= new();
            doc.Periods.RemoveAll(x => x == null);
            doc.Topics.RemoveAll(x => x == null);
            doc.Examples.RemoveAll(x => x == null);
            doc.NewWords.RemoveAll(x => x == null);
            doc.Vocabulary.RemoveAll(x => x == null);
            doc.OtherWords.RemoveAll(x => x == null);
            foreach (var theme in Clean(doc.Grammar).Concat(Clean(doc.WordNotes)))
            {
                theme.Examples ??= new();
                theme.Examples.RemoveAll(x => x == null);
                theme.Parts ??= new();
                theme.Parts.RemoveAll(x => x == null);
                foreach (var part in theme.Parts)
                {
                    part.Examples ??= new();
                    part.Examples.RemoveAll(x => x == null);
                }
            }

            return doc;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"{PackageFileName} of the lesson of {date} cannot be read ({ex.Message}).", ex);
        }
    }

    public IReadOnlyDictionary<string, LessonMark> Marks(string date)
    {
        lock (_gate)
        {
            return ReadMarks(date)
                .Where(m => m.Value != null && LessonPackageViewBuilder.TryMarkId(m.Key, out _))
                .ToDictionary(m => m.Key, m => m.Value!, StringComparer.Ordinal);
        }
    }

    public LessonMark? ChangeMark(string date, string id, Func<LessonMark?, LessonMark?> change)
    {
        lock (_gate)
        {
            var marks = ReadMarks(date);
            marks.TryGetValue(id, out var known);
            var next = change(known);
            // The very entry given back: nothing changed, the file is not written.
            if (ReferenceEquals(next, known))
                return known;
            if (next is { IsEmpty: true })
                next = null;
            if (next == null && known == null)
                return null;
            if (next == null)
                marks.Remove(id);
            else
                marks[id] = next;
            WriteMarks(date, marks);
            return next;
        }
    }

    private static List<LessonGrammar> Clean(List<LessonGrammar> themes)
    {
        themes.RemoveAll(x => x == null);
        return themes;
    }

    private string MarksPath(string date) => Path.Combine(_root, date, MarksFileName);

    /// <summary>Every edit of the file, also the ones this service does not show: a write keeps them all.</summary>
    private SortedDictionary<string, LessonMark?> ReadMarks(string date)
    {
        var path = MarksPath(date);
        if (!File.Exists(path))
            return new SortedDictionary<string, LessonMark?>(StringComparer.Ordinal);
        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, LessonMark?>>(File.ReadAllText(path, Encoding.UTF8), ReadOptions);
            return new SortedDictionary<string, LessonMark?>(read ?? new(), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{MarksFileName} of the lesson of {date} cannot be read ({ex.Message}); it was left as it is.", ex);
        }
    }

    /// <summary>Written next to the file and moved over it, so a reader never meets half a file.</summary>
    private void WriteMarks(string date, SortedDictionary<string, LessonMark?> marks)
    {
        var path = MarksPath(date);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(marks, WriteOptions), new UTF8Encoding(false));
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
                $"{MarksFileName} of the lesson of {date} could not be written ({ex.Message}); it holds what it held before.", ex);
        }
    }

    private static string Stamp(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
