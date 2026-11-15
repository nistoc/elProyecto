using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// Of what the model changed in a batch keeps one kind of edit only — a Spanish word that was written in Cyrillic
/// letters by ear, respelled in Latin letters — and puts everything else back as it was. The built-in prompt asks
/// the model for exactly that, and a small model still does more: it "corrects" the speaker's Spanish, translates
/// a Russian word, writes a Russian word in Latin letters. Code can tell those apart from a respelling.
/// <para>A changed stretch of a line is let through when all of this holds:</para>
/// <list type="bullet">
/// <item>every word it replaces has a Cyrillic letter, and every word put in its place is of Latin letters only;</item>
/// <item>the two sound alike (<see cref="SoundsAlikeFrom"/> on <see cref="SoundForm"/>) — a translation or a
/// changed ending does not;</item>
/// <item>every new word is already written in Latin letters somewhere in the transcript (the Spanish the
/// transcript holds, <see cref="LatinWordsOf"/>) — a Russian word written in Latin letters is not;</item>
/// <item>digits and punctuation of the stretch are the same (the opening ¿ and ¡ aside).</item>
/// </list>
/// The time mark and the speaker of a line are never taken from the model. A batch that came back with another
/// number of lines is put back whole: its lines cannot be told apart.
/// <para>What still gets through: a Russian word that sounds like a Latin word of the same transcript.</para>
/// </summary>
public static class RespellingGuard
{
    /// <summary>
    /// How alike the sound of the old and the new stretch must be. On real lessons a true respelling scored 1.0
    /// (0.91 for a loan word heard with one sound more); a changed ending of a verb 0.80 and a Russian word
    /// turned into its Spanish cognate 0.82 stay below.
    /// </summary>
    public const double SoundsAlikeFrom = 0.85;

    /// <summary>A stretch shorter than this in sound is too little to judge (one letter, "он" → "on").</summary>
    public const int MinSoundLength = 3;

    /// <param name="Lines">The lines to keep, each ending as the original line ends.</param>
    /// <param name="LetThrough">Changed stretches kept as the model wrote them.</param>
    /// <param name="PutBack">Changed stretches (or whole lines) put back as they were.</param>
    public sealed record Outcome(IReadOnlyList<string> Lines, int LetThrough, int PutBack);

    // - 12.34 speaker_0: "text"   (the speaker may be a participant's name)
    private static readonly Regex TranscriptLine = new(
        "^(?<head>\\s*-\\s+\\S+\\s+[^:\"]+:\\s*\")(?<text>.*)(?<tail>\"\\s*)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>The words of Latin letters only in the text of the lines, lower-cased and without accents.</summary>
    public static HashSet<string> LatinWordsOf(IEnumerable<string> lines)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            foreach (var token in Tokens(TextOf(line)))
            {
                if (!HasLatin(token) || HasCyrillic(token))
                    continue;
                var bare = Bare(token);
                if (bare.Length > 0)
                    words.Add(bare);
            }
        }

        return words;
    }

    public static Outcome Apply(IReadOnlyList<string> original, IReadOnlyList<string> refined, IReadOnlySet<string> latinWords)
    {
        // The client drops the empty lines of the model's answer, so the lines are paired without the empty ones.
        var spoken = original.Count(l => !string.IsNullOrWhiteSpace(l));
        if (spoken != refined.Count)
            return new Outcome(original, 0, spoken);

        var lines = new List<string>(original.Count);
        int letThrough = 0, putBack = 0, next = 0;
        foreach (var line in original)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
                continue;
            }

            lines.Add(ApplyToLine(line, refined[next++], latinWords, out var through, out var back));
            letThrough += through;
            putBack += back;
        }

        return new Outcome(lines, letThrough, putBack);
    }

    public static string ApplyToLine(string original, string refined, IReadOnlySet<string> latinWords, out int letThrough, out int putBack)
    {
        letThrough = 0;
        putBack = 0;

        var was = TranscriptLine.Match(original);
        string head, text, tail, newText;
        if (was.Success)
        {
            var now = TranscriptLine.Match(refined);
            if (!now.Success)
            {
                // The model broke the form of the line: nothing of it can be placed.
                putBack = 1;
                return original;
            }

            head = was.Groups["head"].Value;
            text = was.Groups["text"].Value;
            tail = was.Groups["tail"].Value;
            newText = now.Groups["text"].Value;
        }
        else
        {
            // Not a transcript line (plain text): the whole line is the text, its ending stays.
            text = original.TrimEnd('\r', '\n');
            head = "";
            tail = original[text.Length..];
            newText = refined.TrimEnd('\r', '\n');
        }

        var oldTokens = Tokens(text);
        var newTokens = Tokens(newText);
        if (oldTokens.SequenceEqual(newTokens, StringComparer.Ordinal))
            return original;

        var kept = new List<string>(Math.Max(oldTokens.Count, newTokens.Count));
        foreach (var stretch in Stretches(oldTokens, newTokens))
        {
            if (stretch.Same)
            {
                kept.AddRange(stretch.Was);
            }
            else if (stretch.Was.Count == stretch.Now.Count && stretch.Was.Count > 1)
            {
                // Word for word: a respelling next to another change is judged alone, not lost with its neighbour.
                for (var k = 0; k < stretch.Was.Count; k++)
                {
                    if (string.Equals(stretch.Was[k], stretch.Now[k], StringComparison.Ordinal))
                    {
                        kept.Add(stretch.Was[k]);
                    }
                    else if (IsRespelling(new[] { stretch.Was[k] }, new[] { stretch.Now[k] }, latinWords))
                    {
                        kept.Add(stretch.Now[k]);
                        letThrough++;
                    }
                    else
                    {
                        kept.Add(stretch.Was[k]);
                        putBack++;
                    }
                }
            }
            else if (IsRespelling(stretch.Was, stretch.Now, latinWords))
            {
                kept.AddRange(stretch.Now);
                letThrough++;
            }
            else
            {
                kept.AddRange(stretch.Was);
                putBack++;
            }
        }

        return letThrough == 0 ? original : head + string.Join(" ", kept) + tail;
    }

    /// <summary>Is the new stretch the old one written in Latin letters, and nothing else?</summary>
    public static bool IsRespelling(IReadOnlyList<string> was, IReadOnlyList<string> now, IReadOnlySet<string> latinWords)
    {
        var oldWords = was.Where(HasLetter).ToList();
        var newWords = now.Where(HasLetter).ToList();
        if (oldWords.Count == 0 || newWords.Count == 0)
            return false;
        if (!oldWords.All(HasCyrillic))
            return false;
        if (!newWords.All(w => HasLatin(w) && !HasCyrillic(w)))
            return false;
        if (!string.Equals(DigitsAndPunctuation(was), DigitsAndPunctuation(now), StringComparison.Ordinal))
            return false;
        if (!newWords.All(w => latinWords.Contains(Bare(w))))
            return false;

        var oldSound = string.Concat(oldWords.Select(SoundForm));
        var newSound = string.Concat(newWords.Select(SoundForm));
        if (oldSound.Length < MinSoundLength || newSound.Length < MinSoundLength)
            return false;
        return Similarity(oldSound, newSound) >= SoundsAlikeFrom;
    }

    /// <summary>
    /// A word as it sounds, written the same way from either alphabet: «грасиас» and "gracias" both give
    /// "grasias", «кьеро» and "quiero" both give "kiero". Spanish spellings of one sound are made one
    /// (c/qu/k, c/z/s before e and i, b/v, ll/y, j and soft g, silent h); a doubled letter counts once.
    /// </summary>
    public static string SoundForm(string word)
    {
        var lower = word.ToLowerInvariant();
        var latin = new StringBuilder(lower.Length + 4);
        for (var i = 0; i < lower.Length; i++)
        {
            var c = lower[i];
            switch (c)
            {
                case 'а': latin.Append('a'); break;
                case 'б': latin.Append('b'); break;
                case 'в': latin.Append('v'); break;
                // Always the hard sound: by ear the soft Spanish g is written with х. Upper case keeps it from the soft-g rule below.
                case 'г': latin.Append('G'); break;
                case 'д': latin.Append('d'); break;
                case 'е': latin.Append('e'); break;
                case 'ё': latin.Append("io"); break;
                case 'ж': latin.Append("zh"); break;
                case 'з': latin.Append('s'); break;
                case 'и': latin.Append('i'); break;
                case 'й': latin.Append('i'); break;
                case 'к': latin.Append('k'); break;
                case 'л': latin.Append('l'); break;
                case 'м': latin.Append('m'); break;
                case 'н': latin.Append('n'); break;
                case 'о': latin.Append('o'); break;
                case 'п': latin.Append('p'); break;
                case 'р': latin.Append('r'); break;
                case 'с': latin.Append('s'); break;
                case 'т': latin.Append('t'); break;
                case 'у': latin.Append('u'); break;
                case 'ф': latin.Append('f'); break;
                case 'х': latin.Append('h'); break;
                case 'ц': latin.Append('s'); break;
                case 'ч': latin.Append("ch"); break;
                case 'ш': latin.Append("sh"); break;
                case 'щ': latin.Append("sh"); break;
                case 'ъ': break;
                case 'ы': latin.Append('i'); break;
                // A soft sign before a vowel is heard as a short "i": «кьеро» — "quiero", «эспаньол» — "español".
                case 'ь':
                    if (i + 1 < lower.Length && "аеёиоуэюя".Contains(lower[i + 1]))
                        latin.Append('i');
                    break;
                case 'э': latin.Append('e'); break;
                case 'ю': latin.Append("iu"); break;
                case 'я': latin.Append("ia"); break;
                case 'ñ': latin.Append("ni"); break;
                default: latin.Append(c); break;
            }
        }

        var plain = new StringBuilder(latin.Length);
        foreach (var c in latin.ToString().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or 'G')
                plain.Append(c);
        }

        var s = plain.ToString();
        // Two letters for one sound are kept apart from the single-letter rules below (upper case = taken).
        s = s.Replace("ch", "C").Replace("sh", "S").Replace("zh", "Z");
        s = s.Replace("ll", "i").Replace("qu", "k");
        s = Regex.Replace(s, "gu(?=[ei])", "G");
        s = Regex.Replace(s, "g(?=[ei])", "h").Replace("j", "h").Replace("G", "g");
        s = Regex.Replace(s, "c(?=[ei])", "s").Replace("c", "k");
        s = s.Replace("z", "s").Replace("v", "b").Replace("y", "i").Replace("x", "ks").Replace("h", "");

        var once = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (once.Length == 0 || once[^1] != c)
                once.Append(c);
        }

        return once.ToString();
    }

    /// <summary>1 for the same string, 0 for nothing in common: one minus the edit distance over the longer length.</summary>
    public static double Similarity(string a, string b)
    {
        if (a == b)
            return 1;
        if (a.Length == 0 || b.Length == 0)
            return 0;

        var previous = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var replace = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), replace);
            }

            previous = current;
        }

        return 1 - (double)previous[b.Length] / Math.Max(a.Length, b.Length);
    }

    /// <summary>The runs of tokens the two lists share and, between them, the stretches in which they differ.</summary>
    private static List<(bool Same, List<string> Was, List<string> Now)> Stretches(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int m = a.Count, n = b.Count;
        var common = new int[m + 1, n + 1];
        for (var i = m - 1; i >= 0; i--)
        {
            for (var j = n - 1; j >= 0; j--)
            {
                common[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? common[i + 1, j + 1] + 1
                    : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
        }

        var result = new List<(bool, List<string>, List<string>)>();
        (bool Same, List<string> Was, List<string> Now)? open = null;
        void Take(bool same, string? was, string? now)
        {
            if (open == null || open.Value.Same != same)
            {
                if (open != null)
                    result.Add(open.Value);
                open = (same, new List<string>(), new List<string>());
            }

            if (was != null)
                open.Value.Was.Add(was);
            if (now != null)
                open.Value.Now.Add(now);
        }

        int x = 0, y = 0;
        while (x < m || y < n)
        {
            if (x < m && y < n && string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                Take(true, a[x], null);
                x++;
                y++;
            }
            else if (y < n && (x == m || common[x, y + 1] >= common[x + 1, y]))
            {
                Take(false, null, b[y++]);
            }
            else
            {
                Take(false, a[x++], null);
            }
        }

        if (open != null)
            result.Add(open.Value);
        return result;
    }

    private static string TextOf(string line)
    {
        var m = TranscriptLine.Match(line);
        return m.Success ? m.Groups["text"].Value : line;
    }

    private static List<string> Tokens(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool HasLetter(string token) => token.Any(char.IsLetter);

    private static bool HasCyrillic(string token) => token.Any(c => c is >= 'Ѐ' and <= 'ӿ');

    private static bool HasLatin(string token) =>
        token.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= 'À' and <= 'ɏ');

    /// <summary>Lower-cased letters a–z of the word, accents dropped: the form the transcript's Spanish is kept in.</summary>
    private static string Bare(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (var c in token.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (c is >= 'a' and <= 'z')
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>What is neither a letter nor a space, in order; the opening ¿ and ¡ only Spanish spelling has are left out.</summary>
    private static string DigitsAndPunctuation(IReadOnlyList<string> tokens)
    {
        var sb = new StringBuilder();
        foreach (var token in tokens)
        {
            foreach (var c in token)
            {
                if (!char.IsLetter(c) && !char.IsWhiteSpace(c) && c != '¿' && c != '¡'
                    && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
