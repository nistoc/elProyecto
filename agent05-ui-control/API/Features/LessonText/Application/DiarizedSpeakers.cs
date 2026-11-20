using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.LessonText.Application;

/// <summary>How the speakers of a diarize run were tied across its chunks.</summary>
/// <param name="Units">Chunks (or parts of a chunk) that named speakers.</param>
/// <param name="ByRepeat">Units after the first whose speakers were tied to the previous unit by the words both heard.</param>
/// <param name="ByWords">Units after the first tied only by how much each speaker said: the one who said most in
/// the unit is taken for the one who said most so far. The weaker of the two ways.</param>
/// <param name="ByVoice">Units whose speakers were named by the voice most of their words have
/// (<see cref="DiarizedSpeakers.TieByVoice"/>): every unit on its own, nothing carried from the previous one.</param>
public sealed record SpeakerTies(int Units, int ByRepeat, int ByWords, int ByVoice = 0);

/// <summary>
/// The speakers of a run of a model that tells speakers apart, the same all through the recording. The model
/// names the speakers anew in every chunk, so its names are tied from chunk to chunk: neighbouring chunks share
/// a stretch of speech (<c>chunk_overlap_sec</c> of the job), and a word heard in that stretch by both chunks
/// gives the name of the later chunk the name of the earlier one. A name the shared stretch says nothing about
/// takes, of the names not given in the chunk yet, the one that said most so far — in a lesson the one who says
/// most in a chunk is mostly the teacher (measured 2026-11-20 on a recording with a track per participant: 929
/// of 938 words given the right person this way, the model itself was not better inside its chunks).
/// The names are then <c>speaker_1</c>, <c>speaker_2</c>… by how much each said, most first.
/// </summary>
public static class DiarizedSpeakers
{
    /// <summary>How far apart in time the same word may stand in the two chunks that heard it.</summary>
    public const double RepeatMatchSec = 2.0;

    /// <summary>Fewer words heard by both chunks than this do not tie two names.</summary>
    public const int MinRepeatWords = 2;

    public const string NamePrefix = "speaker_";

    public sealed record Tied(IReadOnlyList<HeardWord> Words, SpeakerTies Ties);

    private sealed record Word(double Start, double End, string Text, string Sound, string Speaker);

    /// <summary>
    /// The words of the run with the tied names, in the order of their start; the lines that repeat the
    /// previous chunk are left out (the previous chunk has the same words). A line's words are spread evenly
    /// over its time.
    /// </summary>
    public static Tied Tie(IReadOnlyList<DiarizedSegment> segments)
    {
        var units = segments.GroupBy(s => s.Unit).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        var said = new List<int>();
        var names = new Dictionary<int, Dictionary<string, int>>();
        int byRepeat = 0, byWords = 0;
        for (var u = 0; u < units.Count; u++)
        {
            var unit = units[u];
            var number = unit[0].Unit;
            var words = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var segment in unit)
                words[segment.Speaker] = words.GetValueOrDefault(segment.Speaker) + (segment.Repeat ? 0 : WordCount(segment.Text));

            var given = new Dictionary<string, int>(StringComparer.Ordinal);
            if (u > 0 && units[u - 1][0].Unit == number - 1)
            {
                var votes = Votes(unit.Where(s => s.Repeat).ToList(), units[u - 1], names[number - 1]);
                foreach (var ((local, tied), count) in votes
                             .OrderByDescending(v => v.Value)
                             .ThenBy(v => v.Key.Local, StringComparer.Ordinal)
                             .ThenBy(v => v.Key.Tied))
                {
                    if (count >= MinRepeatWords && !given.ContainsKey(local) && !given.ContainsValue(tied))
                        given[local] = tied;
                }
            }

            if (u > 0)
            {
                if (given.Count > 0)
                    byRepeat++;
                else
                    byWords++;
            }

            var free = Enumerable.Range(0, said.Count)
                .Where(n => !given.ContainsValue(n))
                .OrderByDescending(n => said[n])
                .ThenBy(n => n)
                .ToList();
            foreach (var local in words
                         .Where(w => !given.ContainsKey(w.Key))
                         .OrderByDescending(w => w.Value)
                         .ThenBy(w => w.Key, StringComparer.Ordinal)
                         .Select(w => w.Key))
            {
                if (free.Count > 0)
                {
                    given[local] = free[0];
                    free.RemoveAt(0);
                }
                else
                {
                    given[local] = said.Count;
                    said.Add(0);
                }
            }

            foreach (var (local, count) in words)
                said[given[local]] += count;
            names[number] = given;
        }

        var order = Enumerable.Range(0, said.Count).OrderByDescending(n => said[n]).ThenBy(n => n).ToList();
        var final = new string[said.Count];
        for (var i = 0; i < order.Count; i++)
            final[order[i]] = NamePrefix + (i + 1);

        var heard = segments
            .Where(s => !s.Repeat)
            .SelectMany(s => Spread(s).Select(w => new HeardWord(w.Start, w.End, w.Text, final[names[s.Unit][s.Speaker]])))
            .OrderBy(w => w.Start)
            .ToList();
        return new Tied(heard, new SpeakerTies(units.Count, byRepeat, byWords));
    }

    /// <summary>
    /// For every name of the later unit and every tied name of the earlier one: how many words of the shared
    /// stretch both heard, the later under the one name and the earlier under the other.
    /// </summary>
    private static Dictionary<(string Local, int Tied), int> Votes(
        List<DiarizedSegment> repeat, List<DiarizedSegment> previous, Dictionary<string, int> previousNames)
    {
        var votes = new Dictionary<(string, int), int>();
        var ours = repeat.SelectMany(Spread).Where(w => w.Sound.Length >= 2).OrderBy(w => w.Start).ToList();
        if (ours.Count == 0)
            return votes;

        var from = ours[0].Start - RepeatMatchSec;
        var to = ours.Max(w => w.End) + RepeatMatchSec;
        var theirs = previous
            .Where(s => s.End >= from && s.Start <= to)
            .SelectMany(Spread)
            .Where(w => w.Sound.Length >= 2 && w.End >= from && w.Start <= to)
            .OrderBy(w => w.Start)
            .ToList();
        if (theirs.Count == 0)
            return votes;

        // The longest run of words found in the same order in both, each pair close in time.
        int n = ours.Count, m = theirs.Count;
        var best = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        for (var j = m - 1; j >= 0; j--)
            best[i, j] = Same(ours[i], theirs[j]) ? best[i + 1, j + 1] + 1 : Math.Max(best[i + 1, j], best[i, j + 1]);

        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (Same(ours[i], theirs[j]))
            {
                var key = (ours[i].Speaker, previousNames[theirs[j].Speaker]);
                votes[key] = votes.GetValueOrDefault(key) + 1;
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

        return votes;
    }

    /// <summary>
    /// The words of the run named by voice (<see cref="VoiceSpeakers"/>) instead of tied from chunk to chunk: every
    /// name the model gave in a unit takes the voice most of its words were said in, so two names of one unit may
    /// become one person (the model sometimes names one person anew inside a chunk). A name none of whose words
    /// has a voice takes the voice most words of its unit have, else the voice of most words of the run.
    /// The names are those of the voices (<c>voice_1</c>…). Measured 2026-11-20: 97.7 % of words given the right
    /// person on a recording with a track per participant (the same as <see cref="Tie"/>), and on a recording where
    /// the model named the teacher anew inside a chunk, the last six minutes right where <see cref="Tie"/> turned
    /// them around.
    /// </summary>
    /// <param name="voice">The voices of the same audio file, in the order of their start.</param>
    public static Tied TieByVoice(IReadOnlyList<DiarizedSegment> segments, IReadOnlyList<VoiceSegment> voice)
    {
        var own = segments.Where(s => !s.Repeat).SelectMany(s => Spread(s).Select(w => (s.Unit, Word: w))).ToList();
        var at = own.Select(o => VoiceSpeakers.At(voice, (o.Word.Start + o.Word.End) / 2)).ToList();

        static string? Most(IEnumerable<string?> voices) =>
            voices.Where(v => v != null).GroupBy(v => v!, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key).FirstOrDefault();

        var ofRun = Most(at) ?? VoiceSpeakers.NamePrefix + "1";
        var ofUnit = own.Select((o, i) => (o.Unit, Voice: at[i])).GroupBy(x => x.Unit)
            .ToDictionary(g => g.Key, g => Most(g.Select(x => x.Voice)) ?? ofRun);
        var ofName = own.Select((o, i) => (Key: (o.Unit, o.Word.Speaker), Voice: at[i])).GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => Most(g.Select(x => x.Voice)) ?? ofUnit[g.Key.Unit]);

        var heard = own
            .Select(o => new HeardWord(o.Word.Start, o.Word.End, o.Word.Text, ofName[(o.Unit, o.Word.Speaker)]))
            .OrderBy(w => w.Start)
            .ToList();
        var units = segments.Select(s => s.Unit).Distinct().Count();
        return new Tied(heard, new SpeakerTies(units, 0, 0, units));
    }

    private static bool Same(Word a, Word b) =>
        a.Sound == b.Sound && Math.Abs((a.Start + a.End) / 2 - (b.Start + b.End) / 2) <= RepeatMatchSec;

    private static IEnumerable<Word> Spread(DiarizedSegment segment)
    {
        var tokens = segment.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var span = Math.Max(0, segment.End - segment.Start);
        for (var i = 0; i < tokens.Length; i++)
        {
            var start = segment.Start + span * i / tokens.Length;
            var end = segment.Start + span * (i + 1) / tokens.Length;
            yield return new Word(start, end, tokens[i], SpeakerOverlay.Sound(tokens[i]), segment.Speaker);
        }
    }

    private static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(t => SpeakerOverlay.Sound(t).Length > 0);
}
