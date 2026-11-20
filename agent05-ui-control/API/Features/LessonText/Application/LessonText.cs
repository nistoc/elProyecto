namespace XtractManager.Features.LessonText.Application;

/// <summary>What the lines of a recording were read from.</summary>
public static class LessonTextSource
{
    /// <summary>The dialogue merged from the participant tracks of the recording.</summary>
    public const string Dialogue = "dialogue";

    /// <summary>The transcripts of the runs of its tracks.</summary>
    public const string Tracks = "tracks";

    /// <summary>No job transcribed it: the answer of another recognition service that lies next to the audio.</summary>
    public const string OtherService = "other_service";
}

/// <summary>Where the speakers of the lines come from.</summary>
public static class LessonSpeakersFrom
{
    /// <summary>Every participant has a track of their own: the speaker is the name of the track.</summary>
    public const string Participants = "participants";

    /// <summary>The model that transcribed the mixed recording named them (<c>speaker_N</c>).</summary>
    public const string Model = "model";

    /// <summary>Another recognition service named them; our lines were cut by its words (<see cref="SpeakerOverlay"/>).</summary>
    public const string OtherService = "other_service";

    public const string None = "none";
}

/// <summary>
/// Who a speaker of a lesson is. A role a person gave (<see cref="ILessonSpeakersStore"/>) always stands;
/// where none is given, the service guesses it (<see cref="LessonRoleGuess"/>) and says that it guessed.
/// </summary>
public static class LessonRole
{
    public const string Teacher = "teacher";
    public const string Student = "student";
    public const string Other = "other";

    public static bool IsRole(string? role) => role is Teacher or Student or Other;
}

/// <param name="Start">Seconds from the start of the recording.</param>
/// <param name="Speaker">Null where nobody could be named.</param>
/// <param name="Role">The role of the speaker, given or guessed; null when neither.</param>
public sealed record LessonTextLine(double Start, string? Speaker, string? Role, string Text);

/// <param name="Role">Given by a person, else guessed; null when neither.</param>
/// <param name="RoleGiven">True when a person gave the role (through <see cref="ILessonSpeakersStore"/>, or by
/// labelling the track with the role: «Преподаватель», «Ученик»), false when the service guessed it.</param>
/// <param name="SpanishShare">Share of the speaker's words that are written in Latin letters, 0–1.</param>
public sealed record LessonTextSpeaker(string Id, string? Role, bool RoleGiven, int Lines, int Words, double SpanishShare);

/// <param name="Id">Id of the recording in the lessons list (<c>RecordingPeriod.Id</c>).</param>
/// <param name="Source">See <see cref="LessonTextSource"/>.</param>
/// <param name="JobIds">The jobs whose transcripts were read.</param>
/// <param name="Files">Names of the files read: transcripts, and the answer file the speakers were taken from.</param>
/// <param name="SpeakersFrom">See <see cref="LessonSpeakersFrom"/>.</param>
public sealed record LessonTextRecording(
    string Id,
    string? StartedAt,
    string Source,
    IReadOnlyList<string> JobIds,
    IReadOnlyList<string> Files,
    string SpeakersFrom,
    IReadOnlyList<LessonTextSpeaker> Speakers,
    IReadOnlyList<LessonTextLine> Lines)
{
    /// <summary>When the speakers were laid on from another service: our words of the recording.</summary>
    public int? OverlayWords { get; init; }

    /// <summary>
    /// Of <see cref="OverlayWords"/>, those found among the heard words by their sound; the others got the
    /// speaker of the heard word nearest to where they stand. The share tells how much the cut can be trusted.
    /// </summary>
    public int? OverlayWordsMatched { get; init; }
}

/// <summary>The text of one lesson: its recordings in the order they were made, each with its lines in time order.</summary>
public sealed record LessonTextDocument(string Date, IReadOnlyList<LessonTextRecording> Recordings)
{
    public int Lines => Recordings.Sum(r => r.Lines.Count);

    public int LinesWithSpeaker => Recordings.Sum(r => r.Lines.Count(l => l.Speaker != null));

    public int LinesWithRole => Recordings.Sum(r => r.Lines.Count(l => l.Role != null));
}

public interface ILessonTextReader
{
    /// <returns>Null when the recordings folder has no lesson of that date.</returns>
    /// <exception cref="InvalidOperationException">The roles file of the lesson cannot be read.</exception>
    Task<LessonTextDocument?> ReadAsync(string date, CancellationToken ct = default);
}

/// <summary>
/// The roles of the speakers of a lesson, as a person gave them: by recording, then by the name of the
/// speaker in its lines. The recording <see cref="AllRecordings"/> holds roles for every recording of the lesson.
/// </summary>
public interface ILessonSpeakersStore
{
    public const string AllRecordings = "*";

    /// <exception cref="InvalidOperationException">The file cannot be read.</exception>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Roles(string date);

    /// <param name="role">A role of <see cref="LessonRole"/>; null takes the role away.</param>
    /// <exception cref="InvalidOperationException">The file cannot be read; it is left as it is.</exception>
    /// <exception cref="IOException">The change could not be written; the file holds what it held before.</exception>
    void SetRole(string date, string recording, string speaker, string? role);
}
