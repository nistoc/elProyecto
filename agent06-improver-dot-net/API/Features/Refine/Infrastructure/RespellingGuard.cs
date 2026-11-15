using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static TranslationImprover.Features.Refine.Infrastructure.TranscriptWords;

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
/// <item>digits and punctuation of the stretch are the same (the opening ¿ and ¡ aside);</item>
/// <item>Spanish stands next to it — a word in Latin letters, or another word the check respells: a Russian word
/// that sounds like a Spanish word of the transcript («институт» — "instituto") passes every test above, and
/// only the speech around it tells the two apart. Not asked of a word written in both alphabets at once, of a
/// word that is the whole text of its line, and of a transcript that writes its Spanish in Cyrillic as a habit.</item>
/// </list>
/// A word of one or two sounds («ла» — "la") is respelled only with Spanish on each side it has a neighbour on,
/// with exactly the same sound, and never when it is a Russian word («и», «но», «мы»).
/// The time mark and the speaker of a line are never taken from the model. A batch that came back with another
/// number of lines is put back whole: its lines cannot be told apart.
/// </summary>
public static class RespellingGuard
{
    /// <summary>
    /// How alike the sound of the old and the new stretch must be. On real lessons a true respelling scored 1.0
    /// (0.91 for a loan word heard with one sound more); a changed ending of a verb 0.80 and a Russian word
    /// turned into its Spanish cognate 0.82 stay below.
    /// </summary>
    public const double SoundsAlikeFrom = 0.85;

    /// <summary>A stretch shorter than this in sound is judged as a short word (one letter, "он" → "on").</summary>
    public const int MinSoundLength = 3;

    /// <param name="Lines">The lines to keep, each ending as the original line ends.</param>
    /// <param name="LetThrough">Changed stretches kept as the model wrote them.</param>
    /// <param name="PutBack">Changed stretches (or whole lines) put back as they were.</param>
    public sealed record Outcome(IReadOnlyList<string> Lines, int LetThrough, int PutBack);

    /// <summary>One changed stretch of a line and what the check did with it.</summary>
    /// <param name="At">The time mark of the line, empty for a line of another form.</param>
    /// <param name="Why">"respelled", "short word in Spanish speech", "no Spanish next to it", "not a respelling", "form of the line broken".</param>
    public sealed record Decision(string At, string Was, string Now, bool Kept, string Why);

    private enum Kind { Same, Sound, Short, Other }

    private sealed class Slot
    {
        public required IReadOnlyList<string> Was { get; init; }
        public required IReadOnlyList<string> Now { get; init; }
        public required Kind Kind { get; init; }
        public bool Kept { get; set; }
    }

    /// <summary>
    /// Russian words of one or two sounds that sound like a Spanish word ("и" — "y", "но" — "no", "мы" — "mi").
    /// Between two Spanish words they are still Russian: «una casa и un perro».
    /// </summary>
    private static readonly HashSet<string> RussianShortWords = new(StringComparer.Ordinal)
    {
        "и", "а", "о", "у", "э", "е", "я", "но", "да", "ни", "не", "на", "те", "ту", "то", "та", "ты", "вы", "мы",
        "он", "их", "ну", "же", "ли", "бы", "во", "со", "ко", "за", "до", "по", "от", "из", "об", "ой", "ай", "ах",
        "эх", "ох", "ух", "эй", "ха", "хе", "хо", "ел", "ей", "ее", "её", "им", "ем", "ум", "ус", "ок", "эм"
    };

    /// <summary>Is it one of the Russian words of one or two sounds that sound like a Spanish word?</summary>
    public static bool IsRussianShortWord(string word) => RussianShortWords.Contains(LettersOf(word));

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

    /// <param name="spanishMustBeNextTo">Keep a respelling only where Spanish stands next to it. Off for a
    /// transcript that writes its Spanish in Cyrillic letters as a habit (<see cref="SpellingCandidates.Habit"/>):
    /// there a word that sounds Spanish is Spanish nearly every time, and its neighbours are in Cyrillic too.</param>
    /// <param name="decisions">Gets every changed stretch with what was done with it.</param>
    public static Outcome Apply(IReadOnlyList<string> original, IReadOnlyList<string> refined, IReadOnlySet<string> latinWords,
        bool spanishMustBeNextTo = true, ICollection<Decision>? decisions = null)
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

            lines.Add(ApplyToLine(line, refined[next++], latinWords, out var through, out var back, spanishMustBeNextTo, decisions));
            letThrough += through;
            putBack += back;
        }

        return new Outcome(lines, letThrough, putBack);
    }

    public static string ApplyToLine(string original, string refined, IReadOnlySet<string> latinWords, out int letThrough, out int putBack,
        bool spanishMustBeNextTo = true, ICollection<Decision>? decisions = null)
    {
        letThrough = 0;
        putBack = 0;

        var was = Line.Match(original);
        string head, text, tail, newText;
        if (was.Success)
        {
            var now = Line.Match(refined);
            if (!now.Success)
            {
                // The model broke the form of the line: nothing of it can be placed.
                putBack = 1;
                decisions?.Add(new Decision(TimeMarkOf(was.Groups["head"].Value), was.Groups["text"].Value, refined.Trim(), false, "form of the line broken"));
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

        // The line as slots: a token that stayed, or a changed stretch with what it sounds like.
        var slots = new List<Slot>(Math.Max(oldTokens.Count, newTokens.Count));
        foreach (var stretch in Stretches(oldTokens, newTokens))
        {
            if (stretch.Same)
            {
                foreach (var token in stretch.Was)
                    slots.Add(new Slot { Was = new[] { token }, Now = new[] { token }, Kind = Kind.Same });
            }
            else if (stretch.Was.Count == stretch.Now.Count && stretch.Was.Count > 1)
            {
                // Word for word: a respelling next to another change is judged alone, not lost with its neighbour.
                for (var k = 0; k < stretch.Was.Count; k++)
                {
                    var one = new[] { stretch.Was[k] };
                    var other = new[] { stretch.Now[k] };
                    var same = string.Equals(stretch.Was[k], stretch.Now[k], StringComparison.Ordinal);
                    slots.Add(new Slot { Was = one, Now = same ? one : other, Kind = same ? Kind.Same : Judge(one, other, latinWords) });
                }
            }
            else
            {
                slots.Add(new Slot { Was = stretch.Was, Now = stretch.Now, Kind = Judge(stretch.Was, stretch.Now, latinWords) });
            }
        }

        // What a slot sounds like is settled above; whether Spanish stands next to it is read off those
        // verdicts, not off each other's outcome, so the order of the words does not matter.
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            string why;
            switch (slot.Kind)
            {
                case Kind.Same:
                    continue;
                case Kind.Sound:
                    slot.Kept = !spanishMustBeNextTo || OfBothAlphabets(slot) || SpanishIsNextTo(slots, i, shortWord: false);
                    why = slot.Kept ? "respelled" : "no Spanish next to it";
                    break;
                case Kind.Short:
                    slot.Kept = SpanishIsNextTo(slots, i, shortWord: true);
                    why = slot.Kept ? "short word in Spanish speech" : "no Spanish next to it";
                    break;
                default:
                    why = "not a respelling";
                    break;
            }

            if (slot.Kept)
                letThrough++;
            else
                putBack++;
            decisions?.Add(new Decision(TimeMarkOf(head), string.Join(" ", slot.Was), string.Join(" ", slot.Now), slot.Kept, why));
        }

        return letThrough == 0
            ? original
            : head + string.Join(" ", slots.SelectMany(s => s.Kept ? s.Now : s.Was)) + tail;
    }

    /// <summary>
    /// Is there Spanish next to the slot? Spanish is a word in Latin letters that stays in the line, or another
    /// word the check would respell — so a phrase in Cyrillic («пор фавор») carries itself. A word that is the
    /// whole text of its line has nothing next to it and is let through. A short word needs Spanish on every
    /// side it has a neighbour on.
    /// </summary>
    private static bool SpanishIsNextTo(List<Slot> slots, int i, bool shortWord)
    {
        var left = Neighbour(slots, i, -1, skipShort: !shortWord, out var leftExists);
        var right = Neighbour(slots, i, +1, skipShort: !shortWord, out var rightExists);
        if (!leftExists && !rightExists)
        {
            // Alone in its line: nothing to read the language from.
            return !shortWord && !slots.Where((s, k) => k != i).Any(s => s.Was.Any(HasLetter));
        }

        return shortWord
            ? (!leftExists || left) && (!rightExists || right)
            : left || right;
    }

    /// <summary>The nearest word on one side: is there one, and is it Spanish?</summary>
    private static bool Neighbour(List<Slot> slots, int i, int step, bool skipShort, out bool exists)
    {
        exists = false;
        for (var k = i + step; k >= 0 && k < slots.Count; k += step)
        {
            var slot = slots[k];
            var words = slot.Was.Where(HasLetter).ToList();
            if (words.Count == 0 || (skipShort && slot.Kind == Kind.Short))
                continue; // punctuation alone, words the model added, a short word between two respelled ones

            exists = true;
            if (slot.Kind == Kind.Sound)
                return true;
            return IsLatinWord(step < 0 ? words[^1] : words[0]);
        }

        return false;
    }

    /// <summary>"- 12.50 speaker_0: \"" — "12.50".</summary>
    private static string TimeMarkOf(string head)
    {
        var parts = head.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : "";
    }

    private static bool OfBothAlphabets(Slot slot) =>
        slot.Was.Where(HasLetter).All(w => HasLatin(w) && HasCyrillic(w));

    /// <summary>Does the new stretch sound like the old one written in Latin letters, and is it nothing else?
    /// (What stands next to it is judged with the line, in <see cref="ApplyToLine"/>.)</summary>
    public static bool IsRespelling(IReadOnlyList<string> was, IReadOnlyList<string> now, IReadOnlySet<string> latinWords) =>
        Judge(was, now, latinWords) == Kind.Sound;

    private static Kind Judge(IReadOnlyList<string> was, IReadOnlyList<string> now, IReadOnlySet<string> latinWords)
    {
        var oldWords = was.Where(HasLetter).ToList();
        var newWords = now.Where(HasLetter).ToList();
        if (oldWords.Count == 0 || newWords.Count == 0)
            return Kind.Other;
        if (!oldWords.All(HasCyrillic))
            return Kind.Other;
        if (!newWords.All(w => HasLatin(w) && !HasCyrillic(w)))
            return Kind.Other;
        if (!string.Equals(DigitsAndPunctuation(was), DigitsAndPunctuation(now), StringComparison.Ordinal))
            return Kind.Other;
        if (!newWords.All(w => latinWords.Contains(Bare(w))))
            return Kind.Other;

        var like = Likeness(oldWords, newWords, out var shortest);
        if (shortest < MinSoundLength)
        {
            // One or two sounds: «ла» — "la", «йо» — "yo". Only the same sound, and never a Russian word.
            return shortest > 0 && like >= 1 && oldWords.Count == 1 && newWords.Count == 1
                   && !RussianShortWords.Contains(LettersOf(oldWords[0]))
                ? Kind.Short
                : Kind.Other;
        }

        return like >= SoundsAlikeFrom ? Kind.Sound : Kind.Other;
    }

    /// <summary>
    /// How alike two stretches sound, 0 to 1. «ль» before a vowel is heard both as "li" («фамилья» — "familia")
    /// and as the Spanish "ll" («бомбилья» — "bombilla"); the closer of the two readings counts.
    /// </summary>
    public static double Likeness(IReadOnlyList<string> cyrillicWords, IReadOnlyList<string> latinWords, out int shortest)
    {
        var latin = string.Concat(latinWords.Select(w => SoundForm(w)));
        var cyrillic = string.Concat(cyrillicWords.Select(w => SoundForm(w)));
        shortest = Math.Min(cyrillic.Length, latin.Length);
        var like = Similarity(cyrillic, latin);
        if (like < 1)
        {
            var other = string.Concat(cyrillicWords.Select(w => SoundForm(w, softLAsLl: true)));
            if (!string.Equals(other, cyrillic, StringComparison.Ordinal))
                like = Math.Max(like, Similarity(other, latin));
        }

        return like;
    }

    /// <summary>
    /// A word as it sounds, written the same way from either alphabet: «грасиас» and "gracias" both give
    /// "grasias", «кьеро» and "quiero" both give "kiero". Spanish spellings of one sound are made one
    /// (c/qu/k, c/z/s before e and i, b/v, ll/y, j and soft g, silent h); a doubled letter counts once.
    /// </summary>
    /// <param name="softLAsLl">Read «ль» before a vowel as the Spanish "ll" («бомбилья» — "bombilla") and not
    /// as "li" («фамилья» — "familia"). Both are heard; see <see cref="Likeness"/>.</param>
    public static string SoundForm(string word, bool softLAsLl = false)
    {
        const string vowels = "аеёиоуэюя";
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
                case 'л':
                    if (softLAsLl && i + 2 < lower.Length && lower[i + 1] == 'ь' && vowels.Contains(lower[i + 2]))
                    {
                        // «лья» as "lla": the sound of ll and the plain vowel.
                        latin.Append('i').Append(lower[i + 2] switch { 'я' => 'a', 'ю' => 'u', 'ё' => 'o', 'е' => 'e', 'э' => 'e', 'и' => 'i', 'о' => 'o', 'у' => 'u', _ => 'a' });
                        i += 2;
                    }
                    else
                    {
                        // Upper case keeps a doubled Cyrillic л («коллега») from the rule of the Spanish ll below.
                        latin.Append('L');
                    }

                    break;
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
                    if (i + 1 < lower.Length && vowels.Contains(lower[i + 1]))
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
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' or 'G' or 'L')
                plain.Append(c);
        }

        var s = plain.ToString();
        // Two letters for one sound are kept apart from the single-letter rules below (upper case = taken).
        s = s.Replace("ch", "C").Replace("sh", "S").Replace("zh", "Z");
        s = s.Replace("ll", "i").Replace("qu", "k").Replace("L", "l");
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
