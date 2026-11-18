using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Features.Sessions.Infrastructure;

/// <summary>
/// <c>lesson_pages.json</c> in the workspace root, next to the job folders: the addresses of the pages made
/// of the lessons, by lesson date. A file, not a folder, so the jobs list does not take it for a job.
/// The recordings folder is only read, so the addresses cannot lie next to the lesson itself.
/// </summary>
public sealed class FileLessonPagesStore : ILessonPagesStore
{
    public const string FileName = "lesson_pages.json";
    public const int SchemaVersionLatest = 1;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<LessonPage>> Nothing =
        new Dictionary<string, IReadOnlyList<LessonPage>>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IJobWorkspace _workspace;
    private readonly ILogger<FileLessonPagesStore> _logger;
    private readonly object _gate = new();

    public FileLessonPagesStore(IJobWorkspace workspace, ILogger<FileLessonPagesStore> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    private string FilePath => Path.Combine(_workspace.WorkspaceRootPath, FileName);

    public IReadOnlyDictionary<string, IReadOnlyList<LessonPage>> Load()
    {
        lock (_gate)
        {
            try
            {
                return View(Read());
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "{File} cannot be read; the lessons show no pages", FilePath);
                return Nothing;
            }
        }
    }

    public IReadOnlyList<LessonPage> Put(string date, string url, string? title)
    {
        lock (_gate)
        {
            var doc = ReadToChange();
            if (!doc.Lessons.TryGetValue(date, out var pages) || pages == null)
                doc.Lessons[date] = pages = new List<StoredPage>();

            var known = pages.Find(p => p != null && SameAddress(p.Url, url));
            if (known != null)
            {
                known.Title = title;
            }
            else
            {
                if (pages.Count >= LessonPageRules.MaxPagesPerLesson)
                    throw new InvalidOperationException(
                        $"The lesson of {date} already has {LessonPageRules.MaxPagesPerLesson} pages.");
                pages.Insert(0, new StoredPage
                {
                    Url = url,
                    Title = title,
                    AddedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                });
            }

            Write(doc);
            return PagesOf(pages);
        }
    }

    public IReadOnlyList<LessonPage>? Remove(string date, string url)
    {
        lock (_gate)
        {
            var doc = ReadToChange();
            if (!doc.Lessons.TryGetValue(date, out var pages)
                || pages == null
                || pages.RemoveAll(p => p != null && SameAddress(p.Url, url)) == 0)
                return null;
            if (pages.Count == 0)
                doc.Lessons.Remove(date);

            Write(doc);
            return PagesOf(pages);
        }
    }

    /// <summary>A file that cannot be read is never written over: what the operator kept in it stays.</summary>
    private Document ReadToChange()
    {
        try
        {
            return Read();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{FileName} in the jobs folder cannot be read ({ex.Message}); it was left as it is.", ex);
        }
    }

    private Document Read()
    {
        var path = FilePath;
        if (!File.Exists(path))
            return new Document();
        var doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                  ?? new Document();
        doc.Lessons ??= new SortedDictionary<string, List<StoredPage>>(StringComparer.Ordinal);
        return doc;
    }

    /// <summary>
    /// Written next to the file and moved over it, so a reader never meets half a file. The jobs folder is
    /// not made here: when it is absent (the disk is not connected) the write fails and says so.
    /// </summary>
    private void Write(Document doc)
    {
        doc.SchemaVersion = SchemaVersionLatest;
        var path = FilePath;
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(doc, JsonOptions), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RemoveLeftover(temp);
            throw new IOException(
                $"{FileName} in the jobs folder could not be written ({ex.Message}); it holds what it held before.", ex);
        }
    }

    private static void RemoveLeftover(string temp)
    {
        try
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next write goes over it.
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<LessonPage>> View(Document doc)
    {
        var view = new Dictionary<string, IReadOnlyList<LessonPage>>(StringComparer.Ordinal);
        foreach (var (date, stored) in doc.Lessons)
        {
            if (stored == null || !LessonPageRules.TryDate(date, out var day))
                continue;
            var pages = PagesOf(stored);
            if (pages.Count > 0)
                view[day] = pages;
        }

        return view;
    }

    /// <summary>
    /// The file can be written by hand, so every address is checked again: one that is not http(s) is
    /// never handed to the UI as a link.
    /// </summary>
    private static IReadOnlyList<LessonPage> PagesOf(IEnumerable<StoredPage?> stored)
    {
        var pages = new List<LessonPage>();
        foreach (var page in stored)
        {
            if (page == null || !LessonPageRules.TryUrl(page.Url, out var url, out _))
                continue;
            if (pages.Any(p => SameAddress(p.Url, url)))
                continue;
            LessonPageRules.TryTitle(page.Title, out var title, out var titleError);
            pages.Add(new LessonPage(url, titleError == null ? title : null, page.AddedAt));
        }

        return pages;
    }

    private static bool SameAddress(string? stored, string url) =>
        LessonPageRules.TryUrl(stored, out var normal, out _) && string.Equals(normal, url, StringComparison.Ordinal);

    private sealed class Document
    {
        public int SchemaVersion { get; set; } = SchemaVersionLatest;

        public SortedDictionary<string, List<StoredPage>> Lessons { get; set; } = new(StringComparer.Ordinal);

        /// <summary>What the operator wrote into the file besides; the next write keeps it.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Other { get; set; }
    }

    private sealed class StoredPage
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
        public string? AddedAt { get; set; }

        /// <summary>A note or another field the operator added to the page by hand; the next write keeps it.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Other { get; set; }
    }
}
