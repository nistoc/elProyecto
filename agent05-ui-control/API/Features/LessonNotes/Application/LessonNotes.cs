using System.Text.RegularExpressions;

namespace XtractManager.Features.LessonNotes.Application;

/// <summary>
/// A note of the owner's notes vault about a lesson. The vault was copied into the day folder of every lesson
/// (<c>&lt;day folder&gt;/obsidian/</c>, next to the recordings): the homework an earlier parse of the lesson made,
/// a summary, cards, a log. The notes are only read.
/// </summary>
/// <param name="Path">Path of the note from the recordings root, with '/'. The text of the note is asked by it.</param>
/// <param name="Title">The file name without <c>.md</c>.</param>
/// <param name="Kind">What the note is, told by its name: see <see cref="LessonNoteKinds"/>.</param>
/// <param name="ModifiedAt">Last write time of the file, UTC, ISO 8601.</param>
public sealed record LessonNote(string Path, string Title, string Kind, long SizeBytes, string ModifiedAt);

/// <summary>How many notes the vault copy of a lesson holds, and how many of them are homework.</summary>
public sealed record LessonNotesInfo(int Notes, int Homework);

/// <summary>The notes of one lesson, homework first.</summary>
public sealed record LessonNotesResponse(string Date, IReadOnlyList<LessonNote> Notes);

/// <summary>
/// What a note is, told by its file name (the notes have no field that says it). The words are those the
/// owner's notes use: Spanish, Russian and English.
/// </summary>
public static class LessonNoteKinds
{
    public const string Homework = "homework";
    public const string Summary = "summary";
    public const string Cards = "cards";
    public const string Dialogues = "dialogues";
    public const string Topics = "topics";
    public const string Transcript = "transcript";
    public const string Log = "log";
    public const string Other = "other";

    /// <summary>The order the notes of a lesson are listed in.</summary>
    public static readonly IReadOnlyList<string> Order = [Homework, Summary, Cards, Dialogues, Topics, Transcript, Log, Other];

    private static readonly Regex LogWord = new(@"(?<![\p{L}\p{N}])log(?![\p{L}])", RegexOptions.CultureInvariant);
    private static readonly Regex HomeworkShort = new(@"(?<!\p{L})дз(?!\p{L})", RegexOptions.CultureInvariant);

    public static string Of(string fileName)
    {
        var n = fileName.ToLowerInvariant();
        if (n.Contains("anki", StringComparison.Ordinal))
            return Cards;
        if (LogWord.IsMatch(n))
            return Log;
        if (ContainsAny(n, "расшифровк", "транскрипт", "transcript"))
            return Transcript;
        if (ContainsAny(n, "tarea", "deberes", "homework", "домашн", "задани", "упражнен") || HomeworkShort.IsMatch(n))
            return Homework;
        if (ContainsAny(n, "conspect", "конспект", "resumen", "summary"))
            return Summary;
        if (ContainsAny(n, "dialogo", "diálogo", "dialogue", "диалог"))
            return Dialogues;
        if (ContainsAny(n, "theme", "tema", "topic"))
            return Topics;
        return Other;
    }

    public static int Rank(string kind)
    {
        for (var i = 0; i < Order.Count; i++)
        {
            if (Order[i] == kind)
                return i;
        }
        return Order.Count;
    }

    private static bool ContainsAny(string name, params string[] words) =>
        words.Any(w => name.Contains(w, StringComparison.Ordinal));
}

/// <summary>
/// The notes of the vault copies in the day folders of the recordings folder. Nothing is written: the folder is
/// the owner's and is only read.
/// </summary>
public interface ILessonNotesReader
{
    /// <summary>The notes of the lesson of this date, homework first; empty when its day folder has no vault copy.</summary>
    IReadOnlyList<LessonNote> List(string date);

    /// <summary>For every lesson date whose day folder has a vault copy with notes: how many, and how many are homework.</summary>
    IReadOnlyDictionary<string, LessonNotesInfo> Infos();

    /// <summary>
    /// The file of a note of the lesson of this date, by its <see cref="LessonNote.Path"/>; null for anything that
    /// is not a note of that lesson (another folder, another lesson, not <c>.md</c>).
    /// </summary>
    string? NoteFile(string date, string? path);

    /// <summary>
    /// A picture or a sound a note of the lesson shows (<c>![[name]]</c>): found by its file name in the vault copy
    /// of that lesson, as the vault itself finds it; null when there is none or it is not a picture or a sound.
    /// </summary>
    string? EmbeddedFile(string date, string? name);
}
