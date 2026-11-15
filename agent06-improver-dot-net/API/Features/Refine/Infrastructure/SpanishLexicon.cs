using static TranslationImprover.Features.Refine.Infrastructure.TranscriptWords;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// The Spanish a transcript holds: its words written in Latin letters, as they are spelled there and as they
/// sound. A word in Cyrillic letters is looked up in it by sound (<see cref="SoundsLike"/>) — the spelling of a
/// respelled word is taken from here, never from a model.
/// </summary>
public sealed class SpanishLexicon
{
    /// <param name="Spelling">The word as the transcript spells it most often, lower-cased, accents kept.</param>
    /// <param name="Bare">Lower-cased letters a–z, accents dropped.</param>
    /// <param name="Count">How many times it is written in the transcript.</param>
    public sealed record Entry(string Spelling, string Bare, int Count, string Sound);

    public sealed record Match(Entry Entry, double Likeness);

    private readonly Dictionary<string, Entry> _byBare;
    private readonly Dictionary<int, List<Entry>> _bySoundLength = new();
    private readonly Dictionary<string, Match?> _asked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _words;

    private SpanishLexicon(Dictionary<string, Entry> byBare, int wordCount)
    {
        _byBare = byBare;
        _words = new HashSet<string>(byBare.Keys, StringComparer.Ordinal);
        WordCount = wordCount;
        foreach (var entry in byBare.Values)
        {
            if (!_bySoundLength.TryGetValue(entry.Sound.Length, out var list))
                _bySoundLength[entry.Sound.Length] = list = new List<Entry>();
            list.Add(entry);
        }
    }

    /// <summary>How many words of Latin letters the transcript has (each time one is written).</summary>
    public int WordCount { get; }

    /// <summary>The words without accents, lower-cased: what <see cref="RespellingGuard"/> asks for.</summary>
    public IReadOnlySet<string> Words => _words;

    public Entry? Find(string word) => _byBare.GetValueOrDefault(Bare(word));

    public static SpanishLexicon Of(IEnumerable<string> lines)
    {
        // bare form → every spelling of it with its count
        var spellings = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var wordCount = 0;
        foreach (var line in lines)
        {
            foreach (var token in Tokens(TextOf(line)))
            {
                if (!IsLatinWord(token))
                    continue;
                var word = Parts(token).Word.ToLowerInvariant();
                var bare = Bare(word);
                if (bare.Length == 0)
                    continue;
                wordCount++;
                if (!spellings.TryGetValue(bare, out var seen))
                    spellings[bare] = seen = new Dictionary<string, int>(StringComparer.Ordinal);
                seen[word] = seen.GetValueOrDefault(word) + 1;
            }
        }

        var byBare = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var (bare, seen) in spellings)
        {
            // The spelling written most often; of two written equally often, the one that sorts first — the same each run.
            var spelling = seen.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
            byBare[bare] = new Entry(spelling, bare, seen.Values.Sum(), RespellingGuard.SoundForm(bare));
        }

        return new SpanishLexicon(byBare, wordCount);
    }

    /// <summary>
    /// The word of the transcript with exactly the sound of a word of one or two sounds («ла» — "la"), of several
    /// the one written most often. Null for a longer word (<see cref="SoundsLike"/> is for those) and when
    /// there is none.
    /// </summary>
    public Entry? SameShortSound(string cyrillicWord)
    {
        var sound = RespellingGuard.SoundForm(LettersOf(cyrillicWord));
        if (sound.Length == 0 || sound.Length >= RespellingGuard.MinSoundLength || !_bySoundLength.TryGetValue(sound.Length, out var entries))
            return null;
        return entries
            .Where(e => string.Equals(e.Sound, sound, StringComparison.Ordinal))
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.Bare, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// The word of the transcript a word in Cyrillic letters sounds like (from <see cref="RespellingGuard.SoundsAlikeFrom"/>),
    /// the closest one, and of equally close ones the one written more often. Null when there is none, or when
    /// the word is too short to judge by sound.
    /// </summary>
    public Match? SoundsLike(string cyrillicWord)
    {
        var key = LettersOf(cyrillicWord);
        if (_asked.TryGetValue(key, out var known))
            return known;

        Match? best = null;
        foreach (var sound in new[] { RespellingGuard.SoundForm(key), RespellingGuard.SoundForm(key, softLAsLl: true) }.Distinct())
        {
            if (sound.Length < RespellingGuard.MinSoundLength)
                continue;
            // Likeness = 1 − distance / longer length, so only lengths close to this one can reach the bar.
            var shortest = (int)Math.Ceiling(sound.Length * RespellingGuard.SoundsAlikeFrom);
            var longest = (int)Math.Floor(sound.Length / RespellingGuard.SoundsAlikeFrom);
            for (var length = Math.Max(RespellingGuard.MinSoundLength, shortest); length <= longest; length++)
            {
                if (!_bySoundLength.TryGetValue(length, out var entries))
                    continue;
                foreach (var entry in entries)
                {
                    var likeness = RespellingGuard.Similarity(sound, entry.Sound);
                    if (likeness < RespellingGuard.SoundsAlikeFrom)
                        continue;
                    if (best == null || likeness > best.Likeness
                        || (likeness == best.Likeness && (entry.Count > best.Entry.Count
                            || (entry.Count == best.Entry.Count && string.CompareOrdinal(entry.Bare, best.Entry.Bare) < 0))))
                        best = new Match(entry, likeness);
                }
            }
        }

        _asked[key] = best;
        return best;
    }
}
