using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// One transcript entry: the line <c>- TIME SPEAKER: "text"</c> plus any continuation lines.
/// <see cref="Body"/> is everything after <c>SPEAKER: </c>, quotes and line breaks included, so an entry
/// is written back exactly as it was read.
/// </summary>
public sealed record TranscriptEntry(double TimeSec, string TimeText, string Speaker, string Body);

/// <summary>
/// One participant's transcript, ready to merge. <see cref="UntimedEntryIndexes"/> are the entries of a
/// chunk that came back without time marks: their time is only the start of that chunk.
/// </summary>
public sealed record DialogueTrack(
    string Speaker,
    IReadOnlyList<TranscriptEntry> Entries,
    IReadOnlySet<int>? UntimedEntryIndexes = null);

/// <summary>
/// Builds one dialogue out of per-participant transcripts. Each track holds a single voice, so the
/// speaker label of every line is replaced by the participant's name and all lines are put in time order.
/// Lines are neither glued nor split: the result has exactly one line per source line.
/// </summary>
public static class TrackDialogueMerger
{
    public const string ContentStart = ">>>>>>>";
    public const string ContentEnd = "<<<<<";

    /// <summary>Added to the name on entries whose time is only the start of their chunk.</summary>
    public const string UntimedSuffix = " (без времени)";

    private static readonly Regex EntryStart = new(
        @"^- (?<time>\d+(?:\.\d+)?) (?<speaker>[^:""]{1,80}): (?<body>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the entries of a combined transcript. Only the part between the <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> and
    /// <c>&lt;&lt;&lt;&lt;&lt;</c> wrappers is read when both are present. A line that does not start a new
    /// entry continues the previous one; lines before the first entry are dropped.
    /// </summary>
    public static IReadOnlyList<TranscriptEntry> Parse(string? markdown)
    {
        var entries = new List<TranscriptEntry>();
        if (string.IsNullOrEmpty(markdown))
            return entries;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var from = 0;
        var to = lines.Length;
        var start = Array.FindIndex(lines, l => l.Trim() == ContentStart);
        if (start >= 0)
        {
            var end = Array.FindIndex(lines, start + 1, l => l.Trim() == ContentEnd);
            if (end >= 0)
            {
                from = start + 1;
                to = end;
            }
        }

        string? timeText = null;
        string? speaker = null;
        double timeSec = 0;
        var body = new StringBuilder();

        void Flush()
        {
            if (timeText == null || speaker == null)
                return;
            entries.Add(new TranscriptEntry(timeSec, timeText, speaker, body.ToString().TrimEnd()));
            timeText = null;
            speaker = null;
            body.Clear();
        }

        for (var i = from; i < to; i++)
        {
            var line = lines[i];
            var m = EntryStart.Match(line);
            if (m.Success && double.TryParse(m.Groups["time"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
            {
                Flush();
                timeSec = t;
                timeText = m.Groups["time"].Value;
                speaker = m.Groups["speaker"].Value.Trim();
                body.Append(m.Groups["body"].Value);
            }
            else if (timeText != null)
            {
                body.Append('\n').Append(line);
            }
        }
        Flush();
        return entries;
    }

    /// <summary>
    /// Merges the tracks into one transcript in the same wrapped format. Order: by time; entries with an
    /// equal time keep the order of <paramref name="tracks"/>, then their order inside the track.
    /// </summary>
    public static string Merge(IReadOnlyList<DialogueTrack> tracks)
    {
        var all = new List<(double Time, int Track, int Index, string Line)>();
        for (var t = 0; t < tracks.Count; t++)
        {
            var track = tracks[t];
            for (var i = 0; i < track.Entries.Count; i++)
            {
                var e = track.Entries[i];
                var untimed = track.UntimedEntryIndexes?.Contains(i) == true;
                var label = untimed ? track.Speaker + UntimedSuffix : track.Speaker;
                all.Add((e.TimeSec, t, i, $"- {e.TimeText} {label}: {e.Body}"));
            }
        }

        var sb = new StringBuilder();
        sb.Append(ContentStart).Append('\n');
        foreach (var item in all.OrderBy(x => x.Time).ThenBy(x => x.Track).ThenBy(x => x.Index))
            sb.Append(item.Line).Append('\n');
        sb.Append(ContentEnd).Append('\n');
        return sb.ToString();
    }
}
