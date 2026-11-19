using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Sessions.Application;

/// <summary>
/// One lesson (or interview) as it lies in the recordings folder: a day folder <c>YYYY.MM.DD[ title]</c>,
/// or Zoom period folders of one date that were left in the root.
/// </summary>
/// <param name="Id">Name of the day folder; <c>@loose/YYYY-MM-DD</c> for period folders left in the root.</param>
/// <param name="Date">Date of the lesson, <c>YYYY-MM-DD</c>.</param>
/// <param name="Title">Text after the date in the folder name, if any.</param>
/// <param name="Loose">True when the periods lie in the root and not in a day folder.</param>
public sealed record RecordingSession(
    string Id,
    string Date,
    string? Title,
    bool Loose,
    IReadOnlyList<RecordingPeriod> Periods)
{
    /// <summary>
    /// Pages made of this lesson outside the service (the lesson package), the one put last first.
    /// They go by the date of the lesson, see <see cref="ILessonPagesStore"/>.
    /// </summary>
    public IReadOnlyList<LessonPage> Pages { get; init; } = Array.Empty<LessonPage>();

    /// <summary>
    /// That the lesson has a package the service itself shows (topics, grammar, words; see
    /// <see cref="LessonPackages.Application.ILessonPackageStore"/>); null when it has none.
    /// </summary>
    public LessonPackages.Application.LessonPackageInfo? Package { get; init; }

    /// <summary>
    /// Where the lesson stands on the way from a recording to a package, see <see cref="LessonStage"/>;
    /// null where nobody worked it out.
    /// </summary>
    public string? Stage { get; init; }
}

/// <summary>One recording of a lesson (Zoom cuts a lesson into periods of about 40 minutes).</summary>
/// <param name="Id">Path of the period folder (or of the audio file of an old flat recording) from the root, with '/'.</param>
/// <param name="StartedAt">Local time of the recording start, <c>YYYY-MM-DDTHH:mm:ss</c>; null when the name has no time.</param>
/// <param name="State"><c>ready</c> (has audio), <c>unconverted</c> (only raw <c>.zoom</c> files) or <c>empty</c>.</param>
/// <param name="HasOldTranscript">A hand-kept <c>*transcript*.md</c> lies next to an old flat recording.</param>
/// <param name="RawZoomBytes">Size of the raw <c>.zoom</c> files that are still in the folder.</param>
public sealed record RecordingPeriod(
    string Id,
    string? StartedAt,
    string? Title,
    string State,
    bool HasVideo,
    bool HasChat,
    bool HasSubtitles,
    bool HasOldTranscript,
    long RawZoomBytes,
    IReadOnlyList<RecordingTrack> Tracks)
{
    /// <summary>Dialogue jobs merged from runs of this period's tracks.</summary>
    public IReadOnlyList<PeriodDialogue> Dialogues { get; init; } = Array.Empty<PeriodDialogue>();
}

/// <summary>One audio file of a period. The file stays where it is; a run works on a copy.</summary>
/// <param name="Id">Path of the file from the recordings root, with '/'. This is what a run is started with.</param>
/// <param name="Kind"><c>mixed</c> (everybody in one file) or <c>participant</c> (one voice).</param>
/// <param name="Participant">Name read from the file name of a participant track.</param>
public sealed record RecordingTrack(
    string Id,
    string Kind,
    string? Participant,
    string FileName,
    long SizeBytes,
    double? DurationSec)
{
    /// <summary>Jobs that transcribed this file, oldest first.</summary>
    public IReadOnlyList<TrackRun> Runs { get; init; } = Array.Empty<TrackRun>();
}

/// <summary>One job that transcribed a track.</summary>
/// <param name="HasTranscript">A combined <c>transcript*.md</c> is in the job folder.</param>
/// <param name="HasRefined">A refiner output <c>transcript_fixed*.md</c> is in the job folder.</param>
public sealed record TrackRun(
    string JobId,
    string Status,
    string Phase,
    string? CreatedAt,
    bool HasTranscript,
    bool HasRefined)
{
    /// <summary>What the job was transcribed with; null when its folder does not tell.</summary>
    public JobRunSettings? Settings { get; init; }

    /// <summary>How much of the run's speech is Spanish and how much Russian; null when no chunk is answered yet.</summary>
    public LanguageShare? Share { get; init; }
}

/// <summary>A dialogue job built from runs of the tracks of one period.</summary>
public sealed record PeriodDialogue(
    string JobId,
    string? Title,
    string? CreatedAt,
    IReadOnlyList<string> TrackJobIds,
    int Merges);

/// <summary>A track found in the recordings folder together with where its file is.</summary>
public sealed record RecordingTrackFile(
    RecordingSession Session,
    RecordingPeriod Period,
    RecordingTrack Track,
    string FullPath);

/// <summary>
/// The folder with lesson recordings (<c>Recordings:RootPath</c>). It is only read: nothing in it is written,
/// moved or deleted.
/// </summary>
public interface IRecordingsCatalog
{
    /// <summary>False when the folder is not configured or does not exist.</summary>
    bool Enabled { get; }

    /// <summary>Absolute path of the folder; null when <see cref="Enabled"/> is false.</summary>
    string? RootPath { get; }

    /// <summary>Lessons found in the folder, newest first.</summary>
    IReadOnlyList<RecordingSession> Scan();

    /// <summary>The track with this id, or null. Only files the scan itself lists can be found.</summary>
    RecordingTrackFile? FindTrack(string? trackId);
}
