namespace XtractManager.Features.Sessions.Application;

/// <summary>What the lessons of the asked dates still need before their text is whole, and what that costs.</summary>
/// <param name="UnknownDates">Asked dates no lesson of the recordings folder has.</param>
/// <param name="Runs">Transcriptions the plan starts (the paid part).</param>
/// <param name="Dialogues">Dialogues the plan merges (code only, nothing is paid).</param>
/// <param name="Minutes">Minutes of audio the runs send.</param>
/// <param name="EstimateUsd">Upper bound of what the runs cost.</param>
public sealed record LessonBatchPlan(
    IReadOnlyList<LessonBatchLesson> Lessons,
    IReadOnlyList<string> UnknownDates,
    int Runs,
    int Dialogues,
    double Minutes,
    double EstimateUsd);

/// <param name="Stage">Where the text of the lesson stands, see <see cref="LessonStage"/>.</param>
public sealed record LessonBatchLesson(string Date, string Stage, IReadOnlyList<LessonBatchStep> Steps);

/// <summary>One recording or track of a lesson and what the batch does with it.</summary>
/// <param name="Kind">See <see cref="LessonBatchStepKind"/>.</param>
/// <param name="Period">Id of the recording (the period) the step belongs to.</param>
/// <param name="Track">Id of the track; null for a dialogue and for a recording that is not converted.</param>
/// <param name="Model">The model the run names in its job settings; null when it stays on the shared one.</param>
/// <param name="Reason">Why the step is what it is, in words.</param>
public sealed record LessonBatchStep(
    string Kind,
    string Period,
    string? Track,
    string? TrackKind,
    string? Model,
    double Minutes,
    double EstimateUsd,
    string Reason)
{
    /// <summary>The jobs the step speaks of: the run that has the text, the run it waits for, the runs a dialogue merges, the job just started.</summary>
    public IReadOnlyList<string> JobIds { get; init; } = Array.Empty<string>();
}

public static class LessonBatchStepKind
{
    /// <summary>The track has no text and no run: the batch starts one.</summary>
    public const string Run = "run";

    /// <summary>Every participant track of the recording has text and no dialogue is merged yet: the batch merges one.</summary>
    public const string Dialogue = "dialogue";

    /// <summary>A run of the track is queued or going.</summary>
    public const string Wait = "wait";

    /// <summary>The track has text already.</summary>
    public const string Done = "done";

    /// <summary>The same file as another track of the batch: one transcription serves both.</summary>
    public const string Duplicate = "duplicate";

    /// <summary>The runs of the track all ended without a text; it is not started again unless the call says so.</summary>
    public const string Failed = "failed";

    /// <summary>The recording has only raw Zoom files; there is no audio to send.</summary>
    public const string Unconverted = "unconverted";

    /// <summary>A run step the call has carried out: the job is queued.</summary>
    public const string Started = "started";

    /// <summary>A dialogue step the call has carried out.</summary>
    public const string Merged = "merged";
}

/// <summary>Where the text of a lesson stands.</summary>
public static class LessonStage
{
    /// <summary>No recording with audio: there is nothing to transcribe.</summary>
    public const string NoRecording = "no_recording";

    /// <summary>No recording of the lesson has text.</summary>
    public const string NoText = "no_text";

    /// <summary>Some recordings have text, others have neither text nor a run.</summary>
    public const string Partial = "partial";

    /// <summary>A run is queued or going.</summary>
    public const string Transcribing = "transcribing";

    /// <summary>Every track has text; a dialogue of participant tracks is still to be merged.</summary>
    public const string DialoguePending = "dialogue_pending";

    /// <summary>Every recording has its text (and its dialogue, where it has participant tracks).</summary>
    public const string TextReady = "text_ready";

    /// <summary>The lesson has a package.</summary>
    public const string Package = "package";
}

/// <param name="Model">The model the call names for every run; null = by the kind of the track.</param>
/// <param name="ParticipantModel">The model of a participant track when the call names none; null = the shared one.</param>
/// <param name="MixedOnlyModel">The model of a recording that has only the mixed file when the call names none; null = the shared one.</param>
/// <param name="UsdPerMinute">Upper bound of the price of a minute of a track of this kind with this model.</param>
/// <param name="RetryFailed">Start a track again whose runs all ended without a text.</param>
public sealed record LessonBatchOptions(
    string? Model,
    string? ParticipantModel,
    string? MixedOnlyModel,
    Func<string, string?, double> UsdPerMinute,
    bool RetryFailed = false);

/// <summary>
/// Works out, from the lessons as they are now, what is missing for the text of each asked lesson. It only
/// reads its arguments, so asking again after some runs finished gives what is still missing then: a batch
/// can be called until every lesson is at <see cref="LessonStage.TextReady"/>.
/// </summary>
public static class LessonBatchPlanner
{
    public const string KindParticipant = "participant";
    public const string KindMixed = "mixed";

    /// <param name="sessions">Lessons with the runs of their tracks and the dialogues of their recordings.</param>
    /// <param name="dates">Dates asked for, <c>YYYY-MM-DD</c>.</param>
    /// <param name="contentKey">Something equal for two files with the same content; null when it cannot be told.</param>
    public static LessonBatchPlan Plan(
        IReadOnlyList<RecordingSession> sessions,
        IReadOnlyList<string> dates,
        LessonBatchOptions options,
        Func<RecordingTrack, string?>? contentKey = null)
    {
        var asked = dates.Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();
        var firstWithContent = new Dictionary<string, string>(StringComparer.Ordinal);
        var lessons = new List<LessonBatchLesson>();
        var unknown = new List<string>();
        foreach (var date in asked)
        {
            var ofDate = sessions.Where(s => s.Date == date).OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            if (ofDate.Count == 0)
            {
                unknown.Add(date);
                continue;
            }

            var steps = ofDate
                .SelectMany(s => s.Periods)
                .OrderBy(p => p.StartedAt ?? p.Id, StringComparer.Ordinal)
                .SelectMany(p => StepsOf(p, options, contentKey, firstWithContent))
                .ToList();
            lessons.Add(new LessonBatchLesson(date, StageOf(steps), steps));
        }

        var runs = lessons.SelectMany(l => l.Steps).Where(s => s.Kind == LessonBatchStepKind.Run).ToList();
        return new LessonBatchPlan(
            lessons,
            unknown,
            runs.Count,
            lessons.SelectMany(l => l.Steps).Count(s => s.Kind == LessonBatchStepKind.Dialogue),
            Math.Round(runs.Sum(s => s.Minutes), 1),
            Math.Round(runs.Sum(s => s.EstimateUsd), 4));
    }

    /// <summary>Where the text of one lesson stands (a package is not looked at here).</summary>
    public static string StageOf(RecordingSession session) =>
        StageOf(session.Periods
            .SelectMany(p => StepsOf(p, new LessonBatchOptions(null, null, null, (_, _) => 0), null, new Dictionary<string, string>()))
            .ToList());

    private static string StageOf(IReadOnlyList<LessonBatchStep> steps)
    {
        bool Any(string kind) => steps.Any(s => s.Kind == kind);
        if (!steps.Any(s => s.Kind != LessonBatchStepKind.Unconverted))
            return LessonStage.NoRecording;
        if (Any(LessonBatchStepKind.Wait))
            return LessonStage.Transcribing;
        if (Any(LessonBatchStepKind.Run) || Any(LessonBatchStepKind.Failed))
            return Any(LessonBatchStepKind.Done) ? LessonStage.Partial : LessonStage.NoText;
        return Any(LessonBatchStepKind.Dialogue) ? LessonStage.DialoguePending : LessonStage.TextReady;
    }

    private static IEnumerable<LessonBatchStep> StepsOf(
        RecordingPeriod period,
        LessonBatchOptions options,
        Func<RecordingTrack, string?>? contentKey,
        Dictionary<string, string> firstWithContent)
    {
        if (period.State == "unconverted")
        {
            yield return new LessonBatchStep(
                LessonBatchStepKind.Unconverted, period.Id, null, null, null, 0, 0,
                "Only raw Zoom files are in the folder: convert the recording in Zoom first.");
            yield break;
        }

        if (period.State != "ready")
            yield break;

        // One voice a file gives the speakers for free, so participant tracks are taken when the recording has them.
        var participants = period.Tracks.Where(t => t.Kind == KindParticipant).ToList();
        var wanted = participants.Count > 0 ? participants : period.Tracks.Where(t => t.Kind == KindMixed).ToList();
        var textJobs = new List<string>();
        foreach (var track in wanted)
        {
            var minutes = Math.Round((track.DurationSec ?? 0) / 60.0, 1);
            LessonBatchStep Step(string kind, string? model, double usd, string reason, params string[] jobs) =>
                new(kind, period.Id, track.Id, track.Kind, model, minutes, usd, reason) { JobIds = jobs };

            var withText = track.Runs.LastOrDefault(r => r.HasTranscript);
            if (withText != null)
            {
                textJobs.Add(withText.JobId);
                Remember(track);
                yield return Step(LessonBatchStepKind.Done, withText.Settings?.Model, 0, "The track has a transcript.", withText.JobId);
                continue;
            }

            var key = contentKey?.Invoke(track);
            if (key != null && firstWithContent.TryGetValue(key, out var first) && first != track.Id)
            {
                yield return Step(LessonBatchStepKind.Duplicate, null, 0, $"The same file as {first}: one transcription serves both.");
                continue;
            }

            Remember(track);
            var going = track.Runs.Where(r => r.Status is "queued" or "running").Select(r => r.JobId).ToArray();
            if (going.Length > 0)
            {
                yield return Step(LessonBatchStepKind.Wait, null, 0, "A run of the track is queued or going.", going);
                continue;
            }

            if (track.Runs.Count > 0 && !options.RetryFailed)
            {
                yield return Step(
                    LessonBatchStepKind.Failed, null, 0,
                    "Every run of the track ended without a transcript; it is started again only with retryFailed.",
                    track.Runs.Select(r => r.JobId).ToArray());
                continue;
            }

            var model = options.Model
                        ?? (track.Kind == KindParticipant ? options.ParticipantModel : options.MixedOnlyModel);
            yield return Step(
                LessonBatchStepKind.Run, model, Math.Round(minutes * options.UsdPerMinute(track.Kind, model), 4),
                track.Kind == KindParticipant
                    ? "A participant track without a transcript."
                    : "The recording has only the mixed file and no transcript.");
        }

        if (participants.Count >= 2 && textJobs.Count == participants.Count && period.Dialogues.Count == 0)
        {
            yield return new LessonBatchStep(
                LessonBatchStepKind.Dialogue, period.Id, null, null, null, 0, 0,
                "Every participant track has a transcript and no dialogue is merged yet.")
            {
                JobIds = textJobs,
            };
        }

        void Remember(RecordingTrack track)
        {
            var key = contentKey?.Invoke(track);
            if (key != null)
                firstWithContent.TryAdd(key, track.Id);
        }
    }
}
