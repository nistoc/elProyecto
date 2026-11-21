using System.Text;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Lays the text of a whole chunk onto the short pieces of the same audio, word by word, without a model.
/// <para>
/// A model that answers plain text hears a long stretch better than a short one, but says nothing about
/// where a phrase stands. The pieces are transcribed too: their words are worse, their place is known.
/// Both texts come from the same audio, so most words coincide; a global alignment (Needleman–Wunsch)
/// gives every word of the whole text the piece of its partner.
/// </para>
/// <para>
/// Words are compared by sound rather than by letters, because the same word may be written in Latin in
/// one answer and in Cyrillic in the other («beberse» / «беберсе»).
/// </para>
/// </summary>
internal static class SpeechPieceWordAlignment
{
    /// <summary>Two words are partners when their sound forms are at least this similar.</summary>
    public const double PartnerSimilarity = 0.5;

    private const double GapScore = -0.4;
    private const double MismatchScore = -0.7;

    /// <param name="Texts">Text of the whole chunk given to each piece, in the order of the pieces.</param>
    /// <param name="WordsWhole">Words in the text of the whole chunk.</param>
    /// <param name="WordsPieces">Words in the pieces' own texts.</param>
    /// <param name="Matched">Words of the whole text that found a partner among the pieces' words.</param>
    public sealed record Result(IReadOnlyList<string> Texts, int WordsWhole, int WordsPieces, int Matched);

    public static Result Assign(string? wholeText, IReadOnlyList<string?> pieceTexts)
    {
        // A token without letters or digits (a dash, a plus) stays glued to the word before it.
        var whole = new List<string>();
        foreach (var token in SplitTokens(wholeText))
        {
            if (HasLetterOrDigit(token) || whole.Count == 0)
                whole.Add(token);
            else
                whole[^1] += " " + token;
        }

        var pieceWordOwner = new List<int>();
        var pieceWords = new List<string>();
        for (var p = 0; p < pieceTexts.Count; p++)
        {
            foreach (var token in SplitTokens(pieceTexts[p]))
            {
                if (!HasLetterOrDigit(token))
                    continue;
                pieceWords.Add(token);
                pieceWordOwner.Add(p);
            }
        }

        var n = whole.Count;
        var m = pieceWords.Count;
        var texts = new string[pieceTexts.Count];
        Array.Fill(texts, "");
        if (n == 0 || pieceTexts.Count == 0)
            return new Result(texts, n, m, 0);

        var owner = new int[n];
        Array.Fill(owner, -1);
        var matched = 0;

        if (m > 0)
        {
            foreach (var (i, j, partners) in Align(whole.Select(Normalize).ToArray(), pieceWords.Select(Normalize).ToArray()))
            {
                if (i < 0 || j < 0)
                    continue;
                owner[i] = pieceWordOwner[j];
                if (partners)
                    matched++;
            }
        }

        // A word without a partner goes to the piece of the word before it (of the word after it at the very start).
        var last = -1;
        for (var k = 0; k < n; k++)
        {
            if (owner[k] >= 0)
                last = owner[k];
            else
                owner[k] = last;
        }

        var next = -1;
        for (var k = n - 1; k >= 0; k--)
        {
            if (owner[k] >= 0)
                next = owner[k];
            else
                owner[k] = next;
        }

        // Words keep their order: a word never stands in an earlier piece than the word before it.
        for (var k = 1; k < n; k++)
        {
            if (owner[k] < owner[k - 1])
                owner[k] = owner[k - 1];
        }

        var builders = new StringBuilder?[pieceTexts.Count];
        for (var k = 0; k < n; k++)
        {
            if (owner[k] < 0)
                continue;
            var sb = builders[owner[k]] ??= new StringBuilder();
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(whole[k]);
        }

        for (var p = 0; p < texts.Length; p++)
            texts[p] = builders[p]?.ToString() ?? "";

        return new Result(texts, n, m, matched);
    }

    /// <summary>
    /// A global alignment (Needleman–Wunsch) of two lists of sound forms, in order: a step of both words
    /// (<c>I</c> and <c>J</c> set; <c>Partners</c> when they sound alike, at least <see cref="PartnerSimilarity"/>),
    /// or of a word of one list alone (the other index is -1).
    /// </summary>
    internal static IReadOnlyList<(int I, int J, bool Partners)> Align(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var n = a.Count;
        var m = b.Count;
        var score = new float[n + 1, m + 1];
        var back = new byte[n + 1, m + 1]; // 0 both words, 1 word of a alone, 2 word of b alone
        for (var i = 1; i <= n; i++)
        {
            score[i, 0] = (float)(i * GapScore);
            back[i, 0] = 1;
        }

        for (var j = 1; j <= m; j++)
        {
            score[0, j] = (float)(j * GapScore);
            back[0, j] = 2;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var s = Similarity(a[i - 1], b[j - 1]);
                var both = score[i - 1, j - 1] + (float)(s >= PartnerSimilarity ? 2 * s - 0.6 : MismatchScore);
                var aAlone = score[i - 1, j] + (float)GapScore;
                var bAlone = score[i, j - 1] + (float)GapScore;
                if (both >= aAlone && both >= bAlone)
                {
                    score[i, j] = both;
                    back[i, j] = 0;
                }
                else if (aAlone >= bAlone)
                {
                    score[i, j] = aAlone;
                    back[i, j] = 1;
                }
                else
                {
                    score[i, j] = bAlone;
                    back[i, j] = 2;
                }
            }
        }

        var steps = new List<(int, int, bool)>(n + m);
        for (int i = n, j = m; i > 0 || j > 0;)
        {
            var step = back[i, j];
            if (i > 0 && j > 0 && step == 0)
            {
                steps.Add((i - 1, j - 1, Similarity(a[i - 1], b[j - 1]) >= PartnerSimilarity));
                i--;
                j--;
            }
            else if (i > 0 && (j == 0 || step == 1))
            {
                steps.Add((i - 1, -1, false));
                i--;
            }
            else
            {
                steps.Add((-1, j - 1, false));
                j--;
            }
        }

        steps.Reverse();
        return steps;
    }

    /// <summary>
    /// The sound form of a word: lower case, no accents, Cyrillic letters as Latin ones, and the Spanish
    /// spellings of one sound brought together (ll/y, qu/c/k, z/s/c before e and i, v/b, mute h).
    /// </summary>
    internal static string Normalize(string word)
    {
        var decomposed = word.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length + 4);
        foreach (var c in decomposed)
        {
            if (c >= '̀' && c <= 'ͯ')
                continue; // accent marks
            var latin = CyrillicAsLatin(c);
            if (latin != null)
            {
                sb.Append(latin);
                continue;
            }

            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                sb.Append(c);
        }

        var s = sb.ToString().Replace("ll", "y").Replace("qu", "k");
        sb.Clear();
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case 'c':
                    c = i + 1 < s.Length && s[i + 1] is 'e' or 'i' ? 's' : 'k';
                    break;
                case 'z':
                    c = 's';
                    break;
                case 'v':
                    c = 'b';
                    break;
                case 'y':
                    c = 'i';
                    break;
                case 'h':
                    continue;
            }

            if (sb.Length > 0 && sb[^1] == c)
                continue; // a doubled letter sounds as one
            sb.Append(c);
        }

        return sb.ToString();
    }

    internal static double Similarity(string a, string b)
    {
        if (a == b)
            return 1;
        if (a.Length == 0 || b.Length == 0)
            return 0;
        return 1 - (double)Levenshtein(a, b) / Math.Max(a.Length, b.Length);
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var replace = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), replace);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static IEnumerable<string> SplitTokens(string? text) =>
        (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool HasLetterOrDigit(string token) => token.Any(char.IsLetterOrDigit);

    private static string? CyrillicAsLatin(char c) => c switch
    {
        'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g", 'д' => "d", 'е' => "e", 'ё' => "e", 'ж' => "zh",
        'з' => "s", 'и' => "i", 'й' => "i", 'к' => "k", 'л' => "l", 'м' => "m", 'н' => "n", 'о' => "o",
        'п' => "p", 'р' => "r", 'с' => "s", 'т' => "t", 'у' => "u", 'ф' => "f", 'х' => "h", 'ц' => "s",
        'ч' => "ch", 'ш' => "sh", 'щ' => "sh", 'ъ' => "", 'ы' => "i", 'ь' => "", 'э' => "e", 'ю' => "yu",
        'я' => "ya",
        _ => null,
    };
}
