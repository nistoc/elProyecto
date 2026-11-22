using System.Text.Json;
using System.Text.Json.Serialization;

namespace XtractManager.Features.LessonPackages.Application;

/// <summary>
/// The package of one lesson: its topics, grammar, examples and words. The lists are made by the builder
/// (models read the transcripts of the lesson) and lie in <c>package.json</c>; what depends on the other
/// lessons and on the owner's edits is counted here each time the package is read
/// (<see cref="LessonPackageView"/>), so the file itself is never rewritten by the service.
/// </summary>
public sealed class LessonPackageDocument
{
    public const int SchemaVersionLatest = 1;

    public int SchemaVersion { get; set; } = SchemaVersionLatest;

    /// <summary>Date of the lesson, <c>YYYY-MM-DD</c>.</summary>
    public string? Lesson { get; set; }

    /// <summary>UTC time the lists were built, ISO 8601.</summary>
    public string? BuiltAt { get; set; }

    /// <summary>What built the lists (script and form), for the reader of the file.</summary>
    public string? Builder { get; set; }

    /// <summary>Model requests the build took.</summary>
    public int? Requests { get; set; }

    /// <summary>What the build cost, US dollars.</summary>
    public double? CostUsd { get; set; }

    public List<LessonPackagePeriod> Periods { get; set; } = new();
    public List<LessonTopic> Topics { get; set; } = new();
    public List<LessonGrammar> Grammar { get; set; } = new();

    /// <summary>Notes the builder took for the meaning of a word rather than a rule.</summary>
    public List<LessonGrammar> WordNotes { get; set; } = new();

    public List<LessonExample> Examples { get; set; } = new();

    /// <summary>Words that were explained, asked about, corrected or prompted in the lesson.</summary>
    public List<LessonWord> NewWords { get; set; } = new();

    /// <summary>Words the lesson used that are worth keeping.</summary>
    public List<LessonWord> Vocabulary { get; set; } = new();

    /// <summary>Words found by code: said in the lesson, held by no list and by no lesson before the build.</summary>
    public List<LessonOtherWord> OtherWords { get; set; } = new();

    /// <summary>Counters of the build (lines read, quotes found in place and so on).</summary>
    public Dictionary<string, double>? Stats { get; set; }
}

/// <summary>One recording of the lesson and the job whose transcript the lists were built from.</summary>
public sealed class LessonPackagePeriod
{
    public int N { get; set; }

    /// <summary>The job (a dialogue, as a rule) whose transcript was read.</summary>
    public string? Job { get; set; }

    /// <summary>Local time the recording started, <c>HH:mm</c>.</summary>
    public string? Start { get; set; }

    public int Lines { get; set; }

    /// <summary>Time of the last line, <c>m:ss</c>.</summary>
    public string? EndAt { get; set; }

    /// <summary>
    /// Counted on read: the mixed track of this recording in the recordings folder, when there is one —
    /// what a phrase is played from.
    /// </summary>
    public string? AudioTrack { get; set; }
}

/// <summary>
/// What every item of a list has for the owner's edits. All of it is counted when the package is read;
/// the builder writes none of it.
/// </summary>
public abstract class LessonItem
{
    /// <summary>The id the edits of this item are kept by; the same as long as the builder's text is.</summary>
    public string? Id { get; set; }

    /// <summary>The fields the owner wrote anew: name of the field — what the builder had there.</summary>
    public Dictionary<string, string?>? Edited { get; set; }

    /// <summary>The owner took the item out: the lists show it only on request.</summary>
    public bool LeftOut { get; set; }

    /// <summary>UTC time of the last edit, ISO 8601.</summary>
    public string? EditedAt { get; set; }
}

public sealed class LessonTopic : LessonItem
{
    public int Period { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public int? FromLine { get; set; }
    public int? ToLine { get; set; }
    public string? FromAt { get; set; }
    public string? ToAt { get; set; }
    public double? FromSec { get; set; }
    public double? ToSec { get; set; }
}

/// <summary>A place of the recording a list item points at.</summary>
public class LessonPlace : LessonItem
{
    public int Period { get; set; }

    /// <summary>Time in the recording of the period, <c>m:ss</c>; null when the quote was not found.</summary>
    public string? At { get; set; }

    public double? Sec { get; set; }

    /// <summary><c>teacher</c>, <c>student</c> or empty.</summary>
    public string? Who { get; set; }

    public int? Line { get; set; }

    /// <summary>The words the item quotes.</summary>
    public string? Quote { get; set; }

    /// <summary>The line of the transcript around the quote, as it was said.</summary>
    public string? Said { get; set; }

    /// <summary><c>exact</c>, <c>close</c>, <c>other</c> (found in a line next to the named one) or <c>none</c>.</summary>
    public string? Check { get; set; }
}

public sealed class LessonGrammar : LessonItem
{
    public string? Topic { get; set; }
    public string? Theme { get; set; }
    public string? Rule { get; set; }

    /// <summary><c>explained</c>, <c>corrected</c> or <c>asked</c>.</summary>
    public string? Kind { get; set; }

    public int Period { get; set; }

    /// <summary>How many notes of the builder this theme was merged from.</summary>
    public int Points { get; set; } = 1;

    public List<LessonPlace> Examples { get; set; } = new();
    public List<LessonGrammarPart> Parts { get; set; } = new();
}

public sealed class LessonGrammarPart
{
    public string? Topic { get; set; }
    public string? Rule { get; set; }
    public List<LessonPlace> Examples { get; set; } = new();
}

public sealed class LessonExample : LessonPlace
{
    public string? Ru { get; set; }

    /// <summary>What the example shows.</summary>
    public string? Shows { get; set; }
}

/// <summary>A word of the lists «new words» and «vocabulary».</summary>
public sealed class LessonWord : LessonPlace
{
    /// <summary>The word or phrase.</summary>
    public string? Es { get; set; }

    public string? Ru { get; set; }

    /// <summary>New words: <c>explained</c>, <c>asked</c>, <c>corrected</c>, <c>prompted</c> or <c>introduced</c>.</summary>
    public string? How { get; set; }

    /// <summary>Vocabulary: <c>noun</c>, <c>verb</c>, <c>adjective</c>, <c>adverb</c> or <c>expression</c>.</summary>
    public string? Pos { get; set; }

    /// <summary>What stands in the line when it is another thing than the right form.</summary>
    public string? Heard { get; set; }

    public string? Note { get; set; }

    /// <summary>Which pass of the builder found the word: 1 the model's list, 2 the question about a word, 3 the first form.</summary>
    public int? Source { get; set; }

    /// <summary>
    /// Counted on read: dates of the earlier lessons that have the word, oldest first; empty — never said
    /// before; null — the entry is a pattern with no word of its own to look for.
    /// </summary>
    public IReadOnlyList<string>? SeenIn { get; set; }

    /// <summary>
    /// Counted on read (<see cref="HeardElsewhere"/>): true — another hearing of the same speech has the word;
    /// false — the hearings there are have none of it, the word may not have been said; null — nothing to tell
    /// by, or the owner wrote the word anew.
    /// </summary>
    public bool? HeardElsewhere { get; set; }
}

/// <summary>A word found by code and not by a model's list.</summary>
public sealed class LessonOtherWord : LessonItem
{
    /// <summary>The word form in plain letters: what the builder compared it by.</summary>
    public string? Word { get; set; }

    /// <summary>The form as the transcript has it.</summary>
    public string? Raw { get; set; }

    /// <summary>How many times the form was said in the lesson.</summary>
    public int Count { get; set; }

    public int Period { get; set; }
    public string? At { get; set; }
    public double? Sec { get; set; }
    public string? Who { get; set; }

    /// <summary>The line of the first time it was said.</summary>
    public string? Said { get; set; }

    /// <summary>
    /// What a model took it for: <c>spanish</c>, <c>russian</c> (a Russian word in Latin letters),
    /// <c>maybe_russian</c>, <c>other</c>, <c>broken</c> or empty.
    /// </summary>
    public string? Kind { get; set; }

    public string? Lemma { get; set; }
    public string? Ru { get; set; }

    /// <summary>The model was sure of the meaning.</summary>
    public bool Sure { get; set; }

    /// <summary>Counted on read: dates of the earlier lessons that have the form, oldest first.</summary>
    public IReadOnlyList<string> SeenIn { get; set; } = Array.Empty<string>();

    /// <summary>Counted on read, as <see cref="LessonWord.HeardElsewhere"/>.</summary>
    public bool? HeardElsewhere { get; set; }
}

/// <summary>
/// The owner's edit of one item of a package: the fields written anew and whether the item is taken out.
/// Kept in <c>marks.json</c> next to the package, by the id of the item; the package file stays as built.
/// </summary>
public sealed class LessonMark
{
    /// <summary>Name of the field — the text the owner wrote.</summary>
    public Dictionary<string, string>? Fields { get; set; }

    /// <summary>Name of the field — what the builder had there when the edit was made. For the reader of the file.</summary>
    public Dictionary<string, string?>? Was { get; set; }

    public bool LeftOut { get; set; }

    /// <summary>Which list the item is in and what it is called, for the reader of the file.</summary>
    public string? Of { get; set; }

    /// <summary>UTC time of the edit, ISO 8601.</summary>
    public string? At { get; set; }

    /// <summary>What a hand or another tool added to the mark; the next write keeps it.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Other { get; set; }

    /// <summary>The owner changed the item: a field written anew, or the item taken out.</summary>
    [JsonIgnore]
    public bool HasEdit => LeftOut || Fields is { Count: > 0 };

    /// <summary>Nothing to keep in the file: no edit, and nothing a hand added.</summary>
    [JsonIgnore]
    public bool IsEmpty => !HasEdit && Other is not { Count: > 0 };

    /// <summary>What stays of the entry when the edit is taken off: what a hand added, or null when there is nothing.</summary>
    public LessonMark? WithoutEdit() => Other is { Count: > 0 } ? new LessonMark { Of = Of, Other = Other } : null;
}

/// <summary>That a lesson has a package, for the lessons list.</summary>
/// <param name="BuiltAt">UTC time the lists were built or the file was written, ISO 8601.</param>
public sealed record LessonPackageInfo(string? BuiltAt);

/// <summary>
/// That a lesson day has its page (<c>lesson.html</c> next to its package, made by the builder's <c>page</c>
/// command from the package, the owner's edits and the notes), for the lessons list.
/// </summary>
/// <param name="BuiltAt">UTC time the page was written, ISO 8601.</param>
/// <param name="Stale">
/// A file of the day folder the page is made of (the package, the owner's edits of it or of the speakers) was
/// written after the page: the page does not show it until it is made again. Words "said before" that a lesson
/// transcribed later changed are not told by this.
/// </param>
public sealed record LessonDayPageInfo(string BuiltAt, bool Stale);

/// <summary>The earlier lessons the words of a package were compared with.</summary>
/// <param name="Lessons">How many lessons before this one have a transcript.</param>
/// <param name="From">The oldest of them, <c>YYYY-MM-DD</c>; null when there is none.</param>
/// <param name="To">The newest of them.</param>
public sealed record LessonsCompared(int Lessons, string? From, string? To, IReadOnlyList<string> Dates);

/// <summary>How many rows each list has (the ones taken out are not counted) and how many of them no earlier lesson has.</summary>
/// <param name="Edited">Items with a field the owner wrote anew.</param>
/// <param name="LeftOut">Items the owner took out.</param>
/// <param name="Stale">Edits whose item the package no longer has (it was built anew); they stay in the file.</param>
/// <param name="NotHeardElsewhere">Words of the three lists no other hearing of the speech has (<see cref="LessonWord.HeardElsewhere"/> false).</param>
public sealed record LessonPackageCounts(
    int NewWords,
    int NewWordsNeverSeen,
    int Vocabulary,
    int VocabularyNeverSeen,
    int OtherWords,
    int OtherWordsNeverSeen,
    int Edited,
    int LeftOut,
    int Stale,
    int NotHeardElsewhere = 0);

/// <summary>A package as it is shown: the lists of the file with the edits put on and the earlier lessons counted.</summary>
public sealed record LessonPackageView(
    string Date,
    LessonPackageDocument Package,
    LessonsCompared Compared,
    LessonPackageCounts Counts);

/// <summary>The packages of the lessons and the owner's edits, by the date of the lesson.</summary>
public interface ILessonPackageStore
{
    /// <summary>The lessons that have a package. Empty when the folder of the packages is not there.</summary>
    IReadOnlyDictionary<string, LessonPackageInfo> List();

    /// <summary>The lesson days that have a page (<c>lesson.html</c>), also a day of notes alone with no package.</summary>
    IReadOnlyDictionary<string, LessonDayPageInfo> DayPages();

    /// <summary>The full path of the page of a lesson day, or null when the day has none.</summary>
    string? DayPagePath(string date);

    /// <summary>The package of the lesson, or null when there is none.</summary>
    /// <exception cref="InvalidOperationException">The file cannot be read.</exception>
    LessonPackageDocument? Read(string date);

    /// <summary>The edits of the lesson, by item id. Empty when none were made.</summary>
    /// <exception cref="InvalidOperationException">The file cannot be read.</exception>
    IReadOnlyDictionary<string, LessonMark> Marks(string date);

    /// <summary>
    /// Changes the edit of one item in one step: <paramref name="change"/> gets the edit the item has (null
    /// when it has none) and gives the one to keep (null or an empty one — the item keeps none).
    /// </summary>
    /// <returns>The edit the item has now, or null.</returns>
    /// <exception cref="InvalidOperationException">The file cannot be read; it is left as it is.</exception>
    /// <exception cref="IOException">The change could not be written; the file holds what it held before.</exception>
    LessonMark? ChangeMark(string date, string id, Func<LessonMark?, LessonMark?> change);
}

/// <summary>What the lessons folder and the jobs tell about the lessons, for a package that is being read.</summary>
/// <param name="Lessons">The words of the lessons that have a transcript, oldest first.</param>
/// <param name="MixedTrackByJob">
/// For a job of a recording (a dialogue, or a run of one of its tracks) — the id of the mixed track of that
/// recording: the audio a phrase of the package is played from.
/// </param>
public sealed record LessonsRead(
    IReadOnlyList<LessonWords> Lessons,
    IReadOnlyDictionary<string, string> MixedTrackByJob)
{
    public static readonly LessonsRead Nothing =
        new(Array.Empty<LessonWords>(), new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// For a job of a recording (a dialogue, or a run of one of its tracks) — which jobs the hearings of that
    /// recording are read from (<see cref="HeardElsewhere"/>).
    /// </summary>
    public IReadOnlyDictionary<string, LessonHearingSource> HearingSources { get; init; } =
        new Dictionary<string, LessonHearingSource>(StringComparer.Ordinal);
}

/// <summary>Reads the hearings of a recording (<see cref="LessonHearing"/>) from the job folders; the folders are only read.</summary>
public interface ILessonHearings
{
    /// <summary>Null when none of the jobs can be put on the timeline of the recording.</summary>
    LessonHearing? Read(LessonHearingSource source);
}

/// <summary>Reads the words of every lesson that has a transcript, for comparing a lesson with the earlier ones.</summary>
public interface ILessonWordIndex
{
    Task<LessonsRead> ReadAsync(CancellationToken ct = default);
}
