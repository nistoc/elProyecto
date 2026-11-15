using static TranslationImprover.Features.Refine.Infrastructure.TranscriptWords;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// The words of a transcript whose spelling is in doubt, found by code: a word written in both alphabets at
/// once («Теngo», «Nет»), and a word in Cyrillic letters that sounds like a Spanish word of the same
/// transcript («бале» — "vale"). Nothing else in a transcript is a candidate for a change.
/// </summary>
public static class SpellingCandidates
{
    public enum Kind
    {
        /// <summary>Letters of both alphabets in one word.</summary>
        BothAlphabets,

        /// <summary>Cyrillic letters, the sound of a Spanish word of the transcript.</summary>
        SoundsSpanish
    }

    /// <param name="Line">Index of the line in the list given to <see cref="Find"/>.</param>
    /// <param name="Token">Index of the token in the text of that line (<see cref="TranscriptWords.Tokens"/>).</param>
    /// <param name="Word">The word as written, without the punctuation around it.</param>
    /// <param name="Spanish">The word of the transcript it sounds like; null when there is none.</param>
    public sealed record Candidate(int Line, int Token, string Word, Kind Kind, SpanishLexicon.Entry? Spanish, double Likeness);

    public static List<Candidate> Find(IReadOnlyList<string> lines, SpanishLexicon lexicon)
    {
        var found = new List<Candidate>();
        for (var l = 0; l < lines.Count; l++)
        {
            var tokens = Tokens(TextOf(lines[l]));
            for (var t = 0; t < tokens.Count; t++)
            {
                var word = Parts(tokens[t]).Word;
                if (word.Length == 0 || !HasCyrillic(word))
                    continue;

                if (HasLatin(word))
                {
                    if (IsCompound(word))
                        continue;
                    var like = lexicon.SoundsLike(word);
                    found.Add(new Candidate(l, t, word, Kind.BothAlphabets, like?.Entry, like?.Likeness ?? 0));
                    continue;
                }

                var match = lexicon.SoundsLike(word);
                if (match != null)
                    found.Add(new Candidate(l, t, word, Kind.SoundsSpanish, match.Entry, match.Likeness));
            }
        }

        return found;
    }

    /// <summary>
    /// «web-дизайнером», «email'а»: a Spanish or English word with a Russian part joined by a hyphen or an
    /// apostrophe is two words written the way they are said, each in its own alphabet — not a broken word.
    /// </summary>
    public static bool IsCompound(string word)
    {
        var parts = word.Split('-', '\'', '’', '‑');
        return parts.Length > 1 && parts.All(p => !(HasLatin(p) && HasCyrillic(p)));
    }

    /// <summary>
    /// How a transcript writes its Spanish, read off the transcript itself. The transcriber of participant tracks
    /// writes Spanish in Latin letters, and a word in Cyrillic that sounds Spanish is there as often a Russian
    /// word («институт» — "instituto"); the older one writes a good part of the Spanish in Cyrillic, and there
    /// such a word is Spanish nearly every time. Measured on 36 transcripts: 0.1–4.3 % in 32 of the first kind
    /// (a 33rd, all in Russian, has 6 such words against 20 in Latin letters — hence <see cref="EnoughWords"/>),
    /// 13–21 % in 3 of the second.
    /// </summary>
    /// <param name="SoundingSpanish">Words in Cyrillic letters that sound like a Spanish word of the transcript.</param>
    /// <param name="InLatin">Words written in Latin letters.</param>
    public sealed record Habit(int SoundingSpanish, int InLatin)
    {
        public const double CyrillicShareFrom = 0.10;
        public const int EnoughWords = 20;

        /// <summary>Of the Spanish words the transcript can be seen to hold, the part written in Cyrillic.</summary>
        public double CyrillicShare => SoundingSpanish + InLatin == 0 ? 0 : (double)SoundingSpanish / (SoundingSpanish + InLatin);

        /// <summary>Does the transcript write Spanish in Cyrillic letters as a habit, not as a rare slip?</summary>
        public bool WritesSpanishInCyrillic => SoundingSpanish >= EnoughWords && CyrillicShare >= CyrillicShareFrom;
    }

    public static Habit HabitOf(IReadOnlyList<Candidate> candidates, SpanishLexicon lexicon) =>
        new(candidates.Count(c => c.Kind == Kind.SoundsSpanish), lexicon.WordCount);
}
