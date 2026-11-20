namespace XtractManager.Features.Sessions.Application;

/// <summary>
/// Which run of a track gives its text, and which one its speakers. The plain-text model (<c>gpt-transcribe</c>)
/// writes Spanish in Latin letters and Russian in Cyrillic but tells no speakers; the diarize model tells the
/// speakers and writes much of the Spanish in Cyrillic. So the text of a track is its newest run of the plain
/// model, and a run of the diarize model next to it is only the source of its speakers; a track with no run of
/// the plain model takes its newest run, as before. The model of a run is known from its folder
/// (<see cref="TrackRun.Settings"/>); a run whose model is not known is no plain run.
/// The lesson text and the word count of the lessons read a track by the same rule.
/// </summary>
public static class TrackTextRuns
{
    public const string PlainModelPrefix = "gpt-transcribe";

    /// <summary>True for a run of the plain-text model.</summary>
    public static bool IsPlain(TrackRun run) =>
        run.Settings?.Model is { } model && model.StartsWith(PlainModelPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a run of a model that tells speakers apart.</summary>
    public static bool NamesSpeakers(TrackRun run) =>
        run.Settings?.Model is { } model && model.Contains("diarize", StringComparison.OrdinalIgnoreCase);

    /// <summary>The runs in the order they are tried for the text: those of the plain model newest first, then the others newest first.</summary>
    public static IEnumerable<TrackRun> TextOrder(IReadOnlyList<TrackRun> runs)
    {
        var newestFirst = runs.Reverse().ToList();
        return newestFirst.Where(IsPlain).Concat(newestFirst.Where(r => !IsPlain(r)));
    }

    /// <summary>
    /// The runs that may give the speakers of a text read from <paramref name="textRun"/>, newest first:
    /// the runs of the diarize model, when the text is of the plain model; none otherwise.
    /// </summary>
    public static IEnumerable<TrackRun> SpeakerSources(IReadOnlyList<TrackRun> runs, TrackRun textRun) =>
        IsPlain(textRun)
            ? runs.Reverse().Where(r => r.HasTranscript && NamesSpeakers(r))
            : Enumerable.Empty<TrackRun>();
}
