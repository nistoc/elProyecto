using System.Globalization;
using System.Text.RegularExpressions;
using XtractManager.Features.LessonNotes.Application;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Features.LessonNotes.Infrastructure;

/// <summary>
/// Reads the vault copy of every day folder of the recordings folder: <c>YYYY.MM.DD[ title]/obsidian/**/*.md</c>.
/// Folders and files whose name starts with a dot (the vault's own settings, its bin) are not notes. Nothing is
/// cached: a list of all lessons is some hundreds of file names.
/// </summary>
public sealed class LessonNotesReader : ILessonNotesReader
{
    /// <summary>The name of the vault copy inside a day folder.</summary>
    public const string VaultFolderName = "obsidian";

    /// <summary>What a note may show of its folder: pictures and sounds.</summary>
    public static readonly IReadOnlyDictionary<string, string> EmbeddedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
    };

    private static readonly Regex DayFolder = new(@"^(\d{4})\.(\d{2})\.(\d{2})(\s.*)?$", RegexOptions.CultureInvariant);

    private readonly IRecordingsCatalog _catalog;

    public LessonNotesReader(IRecordingsCatalog catalog) => _catalog = catalog;

    public IReadOnlyList<LessonNote> List(string date)
    {
        var root = _catalog.RootPath;
        if (root == null)
            return Array.Empty<LessonNote>();
        return Vaults(root)
            .Where(v => v.Date == date)
            .SelectMany(v => Notes(v.Path))
            .Select(full => Note(root, full))
            .OrderBy(n => LessonNoteKinds.Rank(n.Kind))
            .ThenBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(n => n.Path, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyDictionary<string, LessonNotesInfo> Infos()
    {
        var root = _catalog.RootPath;
        var infos = new Dictionary<string, LessonNotesInfo>(StringComparer.Ordinal);
        if (root == null)
            return infos;
        foreach (var vault in Vaults(root))
        {
            var notes = 0;
            var homework = 0;
            foreach (var file in Notes(vault.Path))
            {
                notes++;
                if (LessonNoteKinds.Of(Path.GetFileNameWithoutExtension(file)) == LessonNoteKinds.Homework)
                    homework++;
            }
            if (notes == 0)
                continue;
            infos[vault.Date] = infos.TryGetValue(vault.Date, out var before)
                ? new LessonNotesInfo(before.Notes + notes, before.Homework + homework)
                : new LessonNotesInfo(notes, homework);
        }
        return infos;
    }

    public string? NoteFile(string date, string? path)
    {
        var root = _catalog.RootPath;
        if (root == null || string.IsNullOrWhiteSpace(path) || !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return null;
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        foreach (var vault in Vaults(root).Where(v => v.Date == date))
        {
            var inside = vault.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(inside, StringComparison.OrdinalIgnoreCase)
                && !Hidden(Path.GetRelativePath(vault.Path, full))
                && File.Exists(full))
                return full;
        }
        return null;
    }

    public string? EmbeddedFile(string date, string? name)
    {
        var root = _catalog.RootPath;
        if (root == null || string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\']) >= 0 || name.Contains("..", StringComparison.Ordinal)
            || name.StartsWith('.') || !EmbeddedTypes.ContainsKey(Path.GetExtension(name)))
            return null;
        foreach (var vault in Vaults(root).Where(v => v.Date == date))
        {
            var found = Files(vault.Path, "*")
                .Where(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Length)
                .FirstOrDefault();
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>The vault copies of the day folders: date of the lesson and the folder.</summary>
    private static IEnumerable<(string Date, string Path)> Vaults(string root)
    {
        IEnumerable<string> days;
        try
        {
            days = Directory.EnumerateDirectories(root).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var day in days)
        {
            var m = DayFolder.Match(Path.GetFileName(day));
            if (!m.Success || !DateOnly.TryParseExact($"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}", "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;
            string? vault;
            try
            {
                vault = Directory.EnumerateDirectories(day)
                    .FirstOrDefault(d => string.Equals(Path.GetFileName(d), VaultFolderName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (vault != null)
                yield return (date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Path.GetFullPath(vault));
        }
    }

    private static IEnumerable<string> Notes(string vault) => Files(vault, "*.md");

    private static IEnumerable<string> Files(string vault, string pattern)
    {
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(vault, pattern, SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
        return files.Where(f => !Hidden(Path.GetRelativePath(vault, f)));
    }

    /// <summary>A part of the path starts with a dot: the vault's own settings or bin, not a note.</summary>
    private static bool Hidden(string relative) =>
        relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.StartsWith('.'));

    private static LessonNote Note(string root, string full)
    {
        var info = new FileInfo(full);
        var title = Path.GetFileNameWithoutExtension(full);
        return new LessonNote(
            Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/'),
            title,
            LessonNoteKinds.Of(title),
            info.Length,
            info.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    }
}
