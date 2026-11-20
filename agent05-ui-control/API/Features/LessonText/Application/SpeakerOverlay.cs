using System.Text.RegularExpressions;
using XtractManager.Features.LessonPackages.Application;

namespace XtractManager.Features.LessonText.Application;

/// <summary>A word another recognition service heard in the same recording: when, and who it says spoke.</summary>
public sealed record HeardWord(double Start, double End, string Text, string Speaker)
{
    public double Middle => (Start + End) / 2;
}

/// <summary>A stretch of a line that one speaker said.</summary>
/// <param name="Speaker">Null when nothing was heard near the line.</param>
/// <param name="Start">Where the stretch begins in the recording, seconds.</param>
/// <param name="Words">Words of the line in it.</param>
/// <param name="Matched">How many of them were found among the heard words by their sound.</param>
public sealed record SpokenPart(string? Speaker, double Start, string Text, int Words, int Matched);

/// <summary>
/// Gives the lines of a transcript that names no speakers the speakers another recognition service heard in
/// the same recording. A line of ours is the speech between two pauses and often holds several turns, so it
/// is not given to one speaker as a whole: its words are found among the heard words by their sound
/// (<see cref="SaidText.SoundForm"/>, the two services write the same speech in different letters), every
/// word takes the speaker of the heard word it stands at, and the line is cut where the speaker changes.
/// A word that was not found stands between its found neighbours by its place in the line.
/// </summary>
public static class SpeakerOverlay
{
    /// <summary>How far outside a line its words are looked for: the two services do not cut speech at the same instant.</summary>
    public const double MarginSec = 1.5;

    /// <summary>A line with no next line after it is taken to be no longer than this.</summary>
    public const double MaxLineSec = 120;

    /// <summary>A stretch this short that has no found word is not a turn of its own.</summary>
    public const int UnmatchedRunWords = 2;

    /// <summary>How many words a cut may move to stand at the end of a sentence.</summary>
    public const int SnapWords = 2;

    private static readonly Regex Letters = new(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly char[] SentenceEnds = { '.', '?', '!', '…' };
    private static readonly char[] Closers = { '"', '»', '”', '\'', ')', ']' };

    /// <param name="heard">The heard words of the recording, in the order of their start.</param>
    /// <param name="nextStart">Start of the next line; null for the last one.</param>
    public static IReadOnlyList<SpokenPart> Split(string text, double start, double? nextStart, IReadOnlyList<HeardWord> heard)
    {
        var tokens = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return Array.Empty<SpokenPart>();

        var end = Math.Min(nextStart ?? start + MaxLineSec, start + MaxLineSec);
        if (end < start)
            end = start;
        var window = Window(heard, start - MarginSec, end + MarginSec);
        if (window.Count == 0)
            return new[] { new SpokenPart(null, start, string.Join(' ', tokens), tokens.Length, 0) };

        var anchors = Anchors(tokens, window);
        var matched = new int[tokens.Length];
        Array.Fill(matched, -1);
        foreach (var (k, j) in anchors)
            matched[k] = j;

        // One speaker all through the line: nothing to cut.
        var inside = window.Where(w => w.Middle >= start - 0.3 && w.Middle < end + 0.3).ToList();
        if (inside.Count > 0 && inside.All(w => w.Speaker == inside[0].Speaker)
            && anchors.All(a => window[a.Heard].Speaker == inside[0].Speaker))
            return new[] { new SpokenPart(inside[0].Speaker, start, string.Join(' ', tokens), tokens.Length, anchors.Count) };

        var lastHeard = inside.Count > 0 ? Math.Min(end, inside[^1].End + 0.2) : end;
        var times = Times(tokens.Length, anchors, window, start, Math.Max(start, lastHeard));
        var speakers = new string[tokens.Length];
        for (var k = 0; k < tokens.Length; k++)
            speakers[k] = matched[k] >= 0 ? window[matched[k]].Speaker : Nearest(window, times[k]).Speaker;

        Smooth(speakers, matched);
        SnapToSentences(tokens, speakers, matched);

        var parts = new List<SpokenPart>();
        var from = 0;
        for (var k = 1; k <= tokens.Length; k++)
        {
            if (k < tokens.Length && speakers[k] == speakers[from])
                continue;
            parts.Add(new SpokenPart(
                speakers[from],
                from == 0 ? start : Math.Round(times[from], 2),
                string.Join(' ', tokens, from, k - from),
                k - from,
                Enumerable.Range(from, k - from).Count(i => matched[i] >= 0)));
            from = k;
        }

        return parts;
    }

    /// <summary>A word as it sounds, the same from either alphabet; empty for a token with no letter.</summary>
    public static string Sound(string token)
    {
        var letters = string.Concat(Letters.Matches(token).Select(m => m.Value));
        return letters.Length == 0 ? "" : SaidText.SoundForm(letters);
    }

    private static List<HeardWord> Window(IReadOnlyList<HeardWord> heard, double from, double to)
    {
        // The words are in the order of their start: find the first that may reach into the window.
        int lo = 0, hi = heard.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (heard[mid].Start < from - 30)
                lo = mid + 1;
            else
                hi = mid;
        }

        var window = new List<HeardWord>();
        for (var i = lo; i < heard.Count && heard[i].Start <= to; i++)
        {
            if (heard[i].Middle >= from && heard[i].Middle <= to)
                window.Add(heard[i]);
        }

        return window;
    }

    /// <summary>The longest run of our words found in order among the heard ones (a word of one letter is no evidence).</summary>
    private static List<(int Ours, int Heard)> Anchors(string[] tokens, List<HeardWord> window)
    {
        var ours = tokens.Select(Sound).ToArray();
        var theirs = window.Select(w => Sound(w.Text)).ToArray();
        int n = ours.Length, m = theirs.Length;
        var best = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        for (var j = m - 1; j >= 0; j--)
        {
            best[i, j] = ours[i].Length >= 2 && ours[i] == theirs[j]
                ? best[i + 1, j + 1] + 1
                : Math.Max(best[i + 1, j], best[i, j + 1]);
        }

        var anchors = new List<(int, int)>();
        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (ours[i].Length >= 2 && ours[i] == theirs[j])
            {
                anchors.Add((i, j));
                i++;
                j++;
            }
            else if (best[i + 1, j] >= best[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return anchors;
    }

    /// <summary>When each of our words was said: a found word at its heard time, the others evenly between the found ones.</summary>
    private static double[] Times(int words, List<(int Ours, int Heard)> anchors, List<HeardWord> window, double start, double end)
    {
        var points = new List<(double Index, double Time)> { (-0.5, start) };
        foreach (var (k, j) in anchors)
        {
            var time = Math.Clamp(window[j].Middle, start, end);
            // Time never goes back along the line.
            points.Add((k, Math.Max(time, points[^1].Time)));
        }

        points.Add((words - 0.5, Math.Max(end, points[^1].Time)));

        var times = new double[words];
        var p = 0;
        for (var k = 0; k < words; k++)
        {
            while (p + 1 < points.Count - 1 && points[p + 1].Index <= k)
                p++;
            var (i0, t0) = points[p];
            var (i1, t1) = points[p + 1];
            times[k] = i1 - i0 < 1e-9 ? t0 : t0 + (t1 - t0) * (k - i0) / (i1 - i0);
        }

        return times;
    }

    private static HeardWord Nearest(List<HeardWord> window, double time)
    {
        var best = window[0];
        var bestGap = double.MaxValue;
        foreach (var word in window)
        {
            var gap = time < word.Start ? word.Start - time : time > word.End ? time - word.End : 0;
            if (gap < bestGap)
            {
                bestGap = gap;
                best = word;
            }
        }

        return best;
    }

    /// <summary>A short stretch none of whose words was found is a guess of the timing: it joins its neighbour.</summary>
    private static void Smooth(string[] speakers, int[] matched)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;
            var from = 0;
            for (var k = 1; k <= speakers.Length; k++)
            {
                if (k < speakers.Length && speakers[k] == speakers[from])
                    continue;
                var length = k - from;
                var whole = from == 0 && k == speakers.Length;
                if (!whole && length <= UnmatchedRunWords && !Enumerable.Range(from, length).Any(i => matched[i] >= 0))
                {
                    var into = from > 0 ? speakers[from - 1] : speakers[k];
                    for (var i = from; i < k; i++)
                        speakers[i] = into;
                    changed = true;
                }

                from = k;
            }

            if (!changed)
                return;
        }
    }

    /// <summary>
    /// A cut that fell inside a sentence moves to the nearest end of a sentence, when that is within
    /// <see cref="SnapWords"/> words and no found word changes its speaker by it.
    /// </summary>
    private static void SnapToSentences(string[] tokens, string[] speakers, int[] matched)
    {
        for (var cut = 1; cut < tokens.Length; cut++)
        {
            if (speakers[cut] == speakers[cut - 1] || EndsSentence(tokens[cut - 1]))
                continue;

            var left = speakers[cut - 1];
            var right = speakers[cut];
            for (var step = 1; step <= SnapWords; step++)
            {
                // Earlier: the words between go to the right speaker.
                var earlier = cut - step;
                if (earlier >= 1 && EndsSentence(tokens[earlier - 1])
                    && Enumerable.Range(earlier, step).All(i => speakers[i] == left && matched[i] < 0))
                {
                    for (var i = earlier; i < cut; i++)
                        speakers[i] = right;
                    break;
                }

                // Later: the words between go to the left speaker.
                var later = cut + step;
                if (later <= tokens.Length - 1 && EndsSentence(tokens[later - 1])
                    && Enumerable.Range(cut, step).All(i => speakers[i] == right && matched[i] < 0))
                {
                    for (var i = cut; i < later; i++)
                        speakers[i] = left;
                    cut = later;
                    break;
                }
            }
        }
    }

    private static bool EndsSentence(string token)
    {
        var bare = token.TrimEnd(Closers);
        return bare.Length > 0 && Array.IndexOf(SentenceEnds, bare[^1]) >= 0;
    }
}
