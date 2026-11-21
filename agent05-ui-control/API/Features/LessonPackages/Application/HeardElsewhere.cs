using System.Text.RegularExpressions;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Features.LessonPackages.Application;

/// <summary>
/// What a recording was heard as, besides the text a package was built from: for every line of that text the
/// transcriber's other hearings of its piece (<see cref="HeardLine"/>), and the lines of a run of another model on
/// the same audio.
/// </summary>
/// <param name="Lines">The lines of the text runs of the recording, in time order.</param>
/// <param name="Other">The lines of runs of the diarize model on the same audio (for a participant track the run
/// of the mixed file of its recording stands in), in time order.</param>
public sealed record LessonHearing(IReadOnlyList<HeardLine> Lines, IReadOnlyList<TimedLine> Other);

/// <summary>Which jobs a recording's hearing is read from.</summary>
/// <param name="TextJobs">The jobs whose lines are the text: the tracks of the newest dialogue, else the text run of each track.</param>
/// <param name="OtherJobs">The newest run of the diarize model of each track that has one; a participant track
/// that has none takes the one of the mixed file of its recording.</param>
public sealed record LessonHearingSource(IReadOnlyList<string> TextJobs, IReadOnlyList<string> OtherJobs)
{
    /// <summary>The sources of a recording, by the rules of <see cref="TrackTextRuns"/>.</summary>
    public static LessonHearingSource Of(RecordingPeriod period)
    {
        IReadOnlyList<string> text;
        if (period.Dialogues.Count > 0)
        {
            text = period.Dialogues.OrderBy(d => d.CreatedAt ?? "", StringComparer.Ordinal).Last().TrackJobIds;
        }
        else
        {
            text = period.Tracks
                .Select(t => TrackTextRuns.TextOrder(t.Runs).FirstOrDefault(r => r.HasTranscript || r.HasRefined)?.JobId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .ToList();
        }

        static string? Diarized(RecordingTrack track) =>
            track.Runs.Reverse().FirstOrDefault(r => r.HasTranscript && TrackTextRuns.NamesSpeakers(r))?.JobId;

        var mixed = period.Tracks.FirstOrDefault(t => t.Kind == LessonBatchPlanner.KindMixed) is { } m ? Diarized(m) : null;
        var other = period.Tracks
            .Select(t => Diarized(t) ?? (t.Kind == LessonBatchPlanner.KindParticipant ? mixed : null))
            .Where(id => id != null && !text.Contains(id, StringComparer.Ordinal))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new LessonHearingSource(text, other);
    }
}

/// <summary>
/// Was a word of a package heard by another hearing of the same speech, or only by the line it was taken from?
/// The plain-text model hears a chunk whole and every piece of it alone; when the two disagree, a line may hold a
/// Spanish word nobody said (a Russian phrase translated, a word misheard). A word is looked for, by sound and
/// loosely (<see cref="SaidText.SoundForm"/>, a letter or two apart, two words written as one), in what the piece of
/// its line heard alone, in what the piece asked again with the audio around it gave, and in the lines of the
/// diarize model within <see cref="OtherWindowSec"/> s. Code only, nothing is asked.
/// </summary>
public static class HeardElsewhere
{
    /// <summary>How far from the time of the word its line is looked for.</summary>
    public const double LineWindowSec = 40;

    /// <summary>How far from its line the diarize model's words are looked at.</summary>
    public const double OtherWindowSec = 20;

    /// <summary>Shorter words are not checked: too many words sound like them.</summary>
    public const int MinLetters = 4;

    private static readonly Regex AnyWord = new(@"\p{L}+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InBrackets = new(@"\([^)]*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True — another hearing has the word; false — the hearings there are have none of it: the word may not have
    /// been said; null — nothing to tell by (no Latin word of <see cref="MinLetters"/>+ letters, its line not found,
    /// the line has no form of it, or no other hearing of the line).
    /// </summary>
    /// <param name="entry">The word as the line has it (the builder's <c>heard</c>, the form found by code), else the entry.</param>
    /// <param name="sec">Where the word was said in the recording.</param>
    /// <param name="said">The line the word was taken from, as the package has it (its line, then its quote): the
    /// first of them that a line of the hearing holds is taken.</param>
    public static bool? Check(LessonHearing hearing, string? entry, double? sec, params string?[] said)
    {
        var wanted = LessonWordRules.LatinWords(InBrackets.Replace(entry ?? "", " ")).Where(w => w.Length >= MinLetters).ToList();
        if (wanted.Count == 0)
            return null;
        var line = said.Select(s => FindLine(hearing.Lines, s, sec)).FirstOrDefault(l => l != null);
        if (line == null)
            return null;

        // The word as the line has it: a lemma of the list stands in the line in another form.
        var forms = LessonWordRules.LatinWords(line.Text).Concat(LessonWordRules.LatinWords(line.Was)).Distinct(StringComparer.Ordinal).ToList();
        var targets = wanted.Select(w => FormIn(w, forms)).Where(f => f != null).Select(f => SaidText.SoundForm(f!)).Distinct(StringComparer.Ordinal).ToList();
        if (targets.Count == 0)
            return null;

        var pools = new[]
        {
            Pool(AnyWords(line.Own)),
            Pool(AnyWords(line.Context)),
            Pool(hearing.Other.Where(o => Math.Abs(o.Start - line.Start) <= OtherWindowSec).SelectMany(o => AnyWords(o.Text))),
        };
        var heard = false;
        foreach (var (sounds, pairs) in pools)
        {
            if (sounds.Count == 0)
                continue;
            heard = true;
            if (targets.All(t => Found(t, sounds, pairs)))
                return true;
        }

        return heard ? false : null;
    }

    /// <summary>
    /// The line the quote stands in: the one, near the time of the word, that holds the most of its words in their
    /// order (its text, or the text it had before it was replaced) — at least 0.6 of them.
    /// </summary>
    public static HeardLine? FindLine(IReadOnlyList<HeardLine> lines, string? said, double? sec)
    {
        var quote = Lower(said);
        if (quote.Count == 0 || lines.Count == 0)
            return null;
        var near = sec is { } at ? lines.Where(l => Math.Abs(l.Start - at) <= LineWindowSec).ToList() : lines.ToList();
        if (near.Count == 0)
            near = lines.ToList();

        HeardLine? best = null;
        var bestShare = 0.0;
        foreach (var line in near)
        {
            var share = Math.Max(Share(quote, Lower(line.Text)), line.Was != null ? Share(quote, Lower(line.Was)) : 0);
            if (share > bestShare)
            {
                best = line;
                bestShare = share;
            }
        }

        return bestShare >= 0.6 ? best : null;
    }

    /// <summary>How much of the quote the line holds: 1 when one stands in the other whole, else the share of its words in order.</summary>
    private static double Share(IReadOnlyList<string> quote, IReadOnlyList<string> line)
    {
        if (line.Count == 0)
            return 0;
        var q = " " + string.Join(' ', quote) + " ";
        var l = " " + string.Join(' ', line) + " ";
        if (l.Contains(q, StringComparison.Ordinal) || (line.Count >= 3 && q.Contains(l, StringComparison.Ordinal)))
            return 1;
        return (double)CommonInOrder(quote, line) / quote.Count;
    }

    /// <summary>The form of the word the line has: the same, the same word in another form (the beginning but the last two letters), or one that sounds nearly alike.</summary>
    private static string? FormIn(string word, IReadOnlyList<string> forms)
    {
        if (forms.Contains(word, StringComparer.Ordinal))
            return word;
        foreach (var form in forms)
        {
            var stem = Math.Max(MinLetters, Math.Min(word.Length, form.Length) - 2);
            if (word.Length >= stem && form.Length >= stem && string.CompareOrdinal(word, 0, form, 0, stem) == 0)
                return form;
        }

        var sound = SaidText.SoundForm(word);
        return forms.FirstOrDefault(f => Alike(sound, SaidText.SoundForm(f), 0.8));
    }

    private static (HashSet<string> Sounds, List<string> Pairs) Pool(IEnumerable<string> words)
    {
        var list = words.ToList();
        var sounds = new HashSet<string>(StringComparer.Ordinal);
        var single = new List<string>(list.Count);
        foreach (var word in list)
        {
            var sound = SaidText.SoundForm(word);
            single.Add(sound);
            if (sound.Length > 0)
                sounds.Add(sound);
            // «ль» before a vowel may be the Spanish ll: both readings are heard.
            if (SaidText.SoundForm(word, softLAsLl: true) is { Length: > 0 } soft)
                sounds.Add(soft);
        }

        var pairs = new List<string>();
        for (var i = 0; i + 1 < single.Count; i++)
            pairs.Add(single[i] + single[i + 1]);
        return (sounds, pairs);
    }

    /// <summary>Equal, or a letter or two apart, or two neighbouring words of the pool written as one.</summary>
    private static bool Found(string sound, HashSet<string> sounds, List<string> pairs)
    {
        if (sounds.Contains(sound))
            return true;
        foreach (var other in sounds)
        {
            if (Alike(sound, other, 0.8))
                return true;
        }

        foreach (var pair in pairs)
        {
            if (Alike(sound, pair, 0.85))
                return true;
        }

        return false;
    }

    private static bool Alike(string a, string b, double share)
    {
        if (a.Length == 0 || b.Length == 0 || Math.Abs(a.Length - b.Length) > 2)
            return false;
        return 2.0 * CommonInOrder(a.ToCharArray(), b.ToCharArray()) / (a.Length + b.Length) >= share;
    }

    /// <summary>The length of the longest run the two have in the same order, gaps allowed.</summary>
    private static int CommonInOrder<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        var row = new int[b.Count + 1];
        for (var i = 1; i <= a.Count; i++)
        {
            var diagonal = 0;
            for (var j = 1; j <= b.Count; j++)
            {
                var above = row[j];
                row[j] = EqualityComparer<T>.Default.Equals(a[i - 1], b[j - 1]) ? diagonal + 1 : Math.Max(row[j], row[j - 1]);
                diagonal = above;
            }
        }

        return row[b.Count];
    }

    private static IEnumerable<string> AnyWords(string? text) =>
        string.IsNullOrEmpty(text) ? Enumerable.Empty<string>() : AnyWord.Matches(text).Select(m => m.Value.ToLowerInvariant());

    private static List<string> Lower(string? text) => AnyWords(text).Select(LessonWordRules.Plain).ToList();
}
