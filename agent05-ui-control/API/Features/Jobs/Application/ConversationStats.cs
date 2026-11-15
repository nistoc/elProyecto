namespace XtractManager.Features.Jobs.Application;

/// <summary>A line of a track on the timeline of the recording: when it starts and ends, in seconds.</summary>
public sealed record TimedLine(double Start, double End, string Text);

/// <summary>The lines of one participant of a dialogue.</summary>
public sealed record ConversationTrackInput(string JobId, string Speaker, IReadOnlyList<TimedLine> Lines);

/// <summary>
/// How the talk of a dialogue goes: who speaks over whom and who waits how long. Counted from the lines of the
/// participant tracks on the common timeline of the recording (<see cref="ConversationStats"/>).
/// </summary>
public sealed class ConversationReport
{
    /// <summary>A pause is "long" from this many seconds (<see cref="ConversationStats.LongPauseSec"/>).</summary>
    public double LongPauseSeconds { get; set; }

    /// <summary>From the start of the first line to the end of the last one.</summary>
    public double SpanSeconds { get; set; }

    /// <summary>Seconds in which two or more participants speak at once.</summary>
    public double OverlapSeconds { get; set; }

    /// <summary>Seconds inside the span in which nobody speaks.</summary>
    public double SilenceSeconds { get; set; }

    /// <summary>How many lines follow a line of another participant.</summary>
    public int SpeakerChanges { get; set; }

    public List<TrackConversation> Tracks { get; set; } = new();
}

public sealed class TrackConversation
{
    public string JobId { get; set; } = "";
    public string Speaker { get; set; } = "";
    public int Lines { get; set; }

    /// <summary>Lines this participant began while another one was still speaking, short assents left out.</summary>
    public int Interruptions { get; set; }

    /// <summary>Short assents ("mhm", "sí, sí") said over the speech of another participant.</summary>
    public int Backchannels { get; set; }

    /// <summary>Seconds this participant speaks while another one speaks too.</summary>
    public double OverlapSeconds { get; set; }

    /// <summary>Pauses of <see cref="ConversationReport.LongPauseSeconds"/> or more before this participant answers another one.</summary>
    public int LongPausesBeforeAnswer { get; set; }

    public double LongestPauseBeforeAnswerSeconds { get; set; }

    /// <summary>The usual wait before this participant answers another one; null when they never did.</summary>
    public double? MedianAnswerGapSeconds { get; set; }

    /// <summary>Pauses of <see cref="ConversationReport.LongPauseSeconds"/> or more between two lines of this participant with nobody speaking in between.</summary>
    public int LongPausesInOwnSpeech { get; set; }

    public double LongestPauseInOwnSpeechSeconds { get; set; }
}

/// <summary>
/// Counts <see cref="ConversationReport"/>. The rules, in words:
/// <list type="bullet">
/// <item>A line that begins while a line of another participant has been going for at least
/// <see cref="SimultaneousStartSec"/> and goes on for at least <see cref="OverlapMinSec"/> more is said over that
/// speech. It is a short assent when it has at most <see cref="BackchannelMaxWords"/> words and lasts at most
/// <see cref="BackchannelMaxSec"/>. It is an interruption when the two then speak together for at least
/// <see cref="InterruptionJointSec"/>. A shorter overlap is a quick answer: the line counts as begun without a wait.</item>
/// <item>A line that begins when nobody else speaks has a pause before it: from the end of the speech that ended
/// last to the start of the line. After another participant it is a wait before an answer, after the participant's
/// own line it is a pause in their own speech.</item>
/// </list>
/// A line is a stretch of speech between pauses of about a second, so a shorter pause inside it counts as speech:
/// the seconds "both at once" are an upper estimate, and the times are good to a few tenths of a second.
/// </summary>
public static class ConversationStats
{
    public const double LongPauseSec = 3.0;
    public const double OverlapMinSec = 0.3;
    public const double SimultaneousStartSec = 0.3;

    /// <summary>
    /// On real lessons a quarter of the lines begun over the other's speech overlapped it for under 0.7 s — the
    /// usual quick change of speaker, not an interruption.
    /// </summary>
    public const double InterruptionJointSec = 1.0;
    public const double BackchannelMaxSec = 1.5;
    public const int BackchannelMaxWords = 2;

    /// <summary>Null when fewer than two tracks have a line: there is no talk to describe.</summary>
    public static ConversationReport? Compute(IReadOnlyList<ConversationTrackInput> tracks)
    {
        if (tracks.Count(t => t.Lines.Count > 0) < 2)
            return null;

        var n = tracks.Count;
        var lines = new List<(int Track, double Start, double End, string Text)>();
        for (var t = 0; t < n; t++)
        {
            foreach (var line in tracks[t].Lines)
                lines.Add((t, line.Start, Math.Max(line.Start, line.End), line.Text));
        }

        lines.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Track.CompareTo(b.Track));

        var perTrack = tracks.Select(t => new TrackConversation { JobId = t.JobId, Speaker = t.Speaker, Lines = t.Lines.Count }).ToList();
        var answerGaps = Enumerable.Range(0, n).Select(_ => new List<double>()).ToArray();

        // Per participant: the line that started last before the current one, and the latest end of their speech so far.
        var lastStart = new double[n];
        var lastEnd = new double[n];
        var spoke = new bool[n];
        var speakerChanges = 0;
        var previousTrack = -1;

        foreach (var line in lines)
        {
            var t = line.Track;
            if (previousTrack >= 0 && previousTrack != t)
                speakerChanges++;
            previousTrack = t;

            // How long this line and the speech of another participant it began over go on together; -1 when it began over nobody.
            var joint = -1.0;
            for (var u = 0; u < n; u++)
            {
                if (u != t && spoke[u]
                    && lastStart[u] <= line.Start - SimultaneousStartSec
                    && lastEnd[u] >= line.Start + OverlapMinSec)
                    joint = Math.Max(joint, Math.Min(line.End, lastEnd[u]) - line.Start);
            }

            if (joint >= 0 && IsBackchannel(line.Text, line.End - line.Start))
            {
                perTrack[t].Backchannels++;
            }
            else if (joint >= InterruptionJointSec)
            {
                perTrack[t].Interruptions++;
            }
            else
            {
                // Whose speech ended last before this line? Speech still going counts as ending now.
                var previous = -1;
                var previousEnd = double.NegativeInfinity;
                for (var u = 0; u < n; u++)
                {
                    if (!spoke[u])
                        continue;
                    var end = Math.Min(lastEnd[u], line.Start);
                    if (end > previousEnd || (end == previousEnd && u != t))
                    {
                        previous = u;
                        previousEnd = end;
                    }
                }

                if (previous >= 0)
                {
                    var gap = Math.Max(0, line.Start - previousEnd);
                    var stats = perTrack[t];
                    if (previous != t)
                    {
                        answerGaps[t].Add(gap);
                        if (gap >= LongPauseSec)
                            stats.LongPausesBeforeAnswer++;
                        stats.LongestPauseBeforeAnswerSeconds = Math.Max(stats.LongestPauseBeforeAnswerSeconds, gap);
                    }
                    else
                    {
                        if (gap >= LongPauseSec)
                            stats.LongPausesInOwnSpeech++;
                        stats.LongestPauseInOwnSpeechSeconds = Math.Max(stats.LongestPauseInOwnSpeechSeconds, gap);
                    }
                }
            }

            lastStart[t] = line.Start;
            lastEnd[t] = spoke[t] ? Math.Max(lastEnd[t], line.End) : line.End;
            spoke[t] = true;
        }

        for (var t = 0; t < n; t++)
        {
            perTrack[t].MedianAnswerGapSeconds = Median(answerGaps[t]) is { } median ? Round(median) : null;
            perTrack[t].LongestPauseBeforeAnswerSeconds = Round(perTrack[t].LongestPauseBeforeAnswerSeconds);
            perTrack[t].LongestPauseInOwnSpeechSeconds = Round(perTrack[t].LongestPauseInOwnSpeechSeconds);
        }

        var report = new ConversationReport
        {
            LongPauseSeconds = LongPauseSec,
            SpeakerChanges = speakerChanges,
            Tracks = perTrack,
        };
        SweepOverlapAndSilence(lines, n, report);
        return report;
    }

    /// <summary>A short assent: one or two words said quickly.</summary>
    public static bool IsBackchannel(string? text, double seconds)
    {
        if (seconds > BackchannelMaxSec)
            return false;
        var words = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetter));
        return words <= BackchannelMaxWords;
    }

    private static void SweepOverlapAndSilence(List<(int Track, double Start, double End, string Text)> lines, int n, ConversationReport report)
    {
        var events = new List<(double Time, int Delta, int Track)>(lines.Count * 2);
        foreach (var line in lines)
        {
            if (line.End <= line.Start)
                continue;
            events.Add((line.Start, +1, line.Track));
            events.Add((line.End, -1, line.Track));
        }

        if (events.Count == 0)
            return;

        events.Sort((a, b) => a.Time.CompareTo(b.Time));
        var active = new int[n];
        var trackOverlap = new double[n];
        double overlap = 0, silence = 0;
        var at = events[0].Time;
        foreach (var e in events)
        {
            var dt = e.Time - at;
            if (dt > 0)
            {
                var speaking = active.Count(c => c > 0);
                if (speaking == 0)
                    silence += dt;
                else if (speaking >= 2)
                {
                    overlap += dt;
                    for (var t = 0; t < n; t++)
                    {
                        if (active[t] > 0)
                            trackOverlap[t] += dt;
                    }
                }

                at = e.Time;
            }

            active[e.Track] += e.Delta;
        }

        report.SpanSeconds = Round(events[^1].Time - events[0].Time);
        report.OverlapSeconds = Round(overlap);
        report.SilenceSeconds = Round(silence);
        for (var t = 0; t < n; t++)
            report.Tracks[t].OverlapSeconds = Round(trackOverlap[t]);
    }

    private static double? Median(List<double> values)
    {
        if (values.Count == 0)
            return null;
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }

    private static double Round(double seconds) => Math.Round(seconds, 3, MidpointRounding.AwayFromZero);
}
