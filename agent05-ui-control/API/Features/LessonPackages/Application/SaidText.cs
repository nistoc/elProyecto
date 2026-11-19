using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace XtractManager.Features.LessonPackages.Application;

/// <summary>
/// The lines of a job as they were said, for counting which words a lesson has.
/// <para>
/// A refined transcript is not taken as it is. A refine may respell Spanish that the recogniser wrote in
/// Cyrillic («грасиас» — "gracias"), and that word was said. It may also write a Russian line in Spanish
/// («не могу» — "no puedo"), and that word was not. The raw transcript tells the two apart: of the
/// Latin-letter words of the refined text only those are kept that the raw text of the same job has — in the
/// same letters, or with the same sound in either alphabet (<see cref="SoundForm"/>).
/// </para>
/// <para>
/// A word that is not kept breaks its line in two, so a phrase is never found across it. A Russian word
/// that sounds like a Spanish one («проблема» — "problema") cannot be told from the Spanish word this way
/// and is kept.
/// </para>
/// </summary>
public static class SaidText
{
    private static readonly Regex Word = new(@"\p{L}+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LatinWord = new("[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HardGBeforeFrontVowel = new("gu(?=[ei])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SoftG = new("g(?=[ei])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SoftC = new("c(?=[ei])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The raw lines, and after them what of the refined lines the raw text has. With no raw line at all there
    /// is nothing to check the refined text against, and it is returned as it is.
    /// </summary>
    public static IReadOnlyList<string> Lines(IReadOnlyCollection<string> raw, IReadOnlyCollection<string> refined)
    {
        if (raw.Count == 0)
            return refined.ToList();

        var lines = new List<string>(raw.Count + refined.Count);
        lines.AddRange(raw);
        if (refined.Count == 0)
            return lines;

        var rawLines = new HashSet<string>(raw, StringComparer.Ordinal);
        var written = new HashSet<string>(StringComparer.Ordinal);
        var sounds = new HashSet<string>(StringComparer.Ordinal);
        // A lesson says the same words many times: each is turned into its sound once.
        var rawWords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in raw)
        foreach (Match m in Word.Matches(line))
        {
            var word = m.Value.ToLowerInvariant();
            if (!rawWords.Add(word))
                continue;
            if (LatinWord.Match(word) is { Success: true } latin && latin.Length == word.Length)
                written.Add(LessonWordRules.Plain(word));
            AddSound(sounds, SoundForm(word));
            AddSound(sounds, SoundForm(word, softLAsLl: true));
        }

        var kept = new Dictionary<string, bool>(StringComparer.Ordinal);
        var piece = new List<string>();
        foreach (var line in refined)
        {
            if (rawLines.Contains(line))
                continue;
            foreach (Match m in LatinWord.Matches(line))
            {
                var word = m.Value.ToLowerInvariant();
                if (!kept.TryGetValue(word, out var said))
                    kept[word] = said = written.Contains(LessonWordRules.Plain(word)) || sounds.Contains(SoundForm(word));
                if (said)
                {
                    piece.Add(m.Value);
                    continue;
                }

                Flush(piece, lines);
            }

            Flush(piece, lines);
        }

        return lines;
    }

    private static void AddSound(HashSet<string> sounds, string sound)
    {
        if (sound.Length > 0)
            sounds.Add(sound);
    }

    private static void Flush(List<string> piece, List<string> lines)
    {
        if (piece.Count > 0)
            lines.Add(string.Join(' ', piece));
        piece.Clear();
    }

    /// <summary>
    /// A word as it sounds, written the same way from either alphabet: «грасиас» and "gracias" both give
    /// "grasias", «кьеро» and "quiero" both give "kiero". Spanish spellings of one sound are made one
    /// (c/qu/k, c/z/s before e and i, b/v, ll/y, j and soft g, silent h); a doubled letter counts once.
    /// The same rule as <c>RespellingGuard.SoundForm</c> of agent06 — the two services share no code, so
    /// a change of one is a change of both.
    /// </summary>
    /// <param name="softLAsLl">Read «ль» before a vowel as the Spanish "ll" («бомбилья» — "bombilla") and not
    /// as "li" («фамилья» — "familia"). Both are heard.</param>
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
        s = HardGBeforeFrontVowel.Replace(s, "G");
        s = SoftG.Replace(s, "h").Replace("j", "h").Replace("G", "g");
        s = SoftC.Replace(s, "s").Replace("c", "k");
        s = s.Replace("z", "s").Replace("v", "b").Replace("y", "i").Replace("x", "ks").Replace("h", "");

        var once = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (once.Length == 0 || once[^1] != c)
                once.Append(c);
        }

        return once.ToString();
    }
}
