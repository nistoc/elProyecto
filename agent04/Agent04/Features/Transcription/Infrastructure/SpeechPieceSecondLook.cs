using System.Text;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// How a lone piece is spelled: a piece that got no word of the whole chunk's text and so keeps what was
/// heard in it alone.
/// <para>
/// The text of a whole chunk leaves out what a listener leaves out: a word said twice, a false start, a
/// word thrown in while the other one speaks. The piece that holds only such a word has no better text
/// than its own, and a second of sound without its surroundings is where the model most often writes a
/// Spanish word in Cyrillic («Томар.» right before «tomar agua»).
/// </para>
/// <para>
/// Two things tell how such a piece is spelled. The line next to it, when the piece repeats its first
/// words or the last words of the line before: the same word gets the same spelling. And the answer for
/// the piece asked again with a few seconds of audio around it: the run of Spanish words of that answer
/// that sounds most like what the piece heard alone is taken, when it sounds like it at all. The run is
/// found by sound, not by its place among the lines: that answer too drops repetitions, so its
/// «queremos, queremos» may stand for three pieces.
/// </para>
/// </summary>
internal static class SpeechPieceSecondLook
{
    /// <summary>Two words are one word said twice when their sound forms are at least this similar.</summary>
    public const double SameWordSimilarity = 0.75;

    /// <summary>A word of this many letters or fewer must sound exactly the same: «но» is not «nos».</summary>
    public const int ShortWordLetters = 3;

    /// <summary>A longer piece is not a repeated word or two.</summary>
    public const int MaxRepeatedWords = 4;

    /// <summary>
    /// A run of words of the answer with audio around is the piece when its letters are at least this
    /// similar to the piece's. On the lesson of 2026-11-13 the right runs stood at 0.67–0.86 and the
    /// nearest wrong one at 0.50.
    /// </summary>
    public const double RunSimilarity = 0.6;

    /// <summary>A piece glues words together («Номеакуэрдо»), so its run may hold this many words more than the piece.</summary>
    public const int ExtraRunWords = 2;

    public static bool HasCyrillic(string? text) => (text ?? "").Any(IsCyrillic);

    /// <summary>Letters in the sound forms of the words: «Угу.» has three, «Да.» two.</summary>
    public static int SoundLetters(string? text) => Words(text).Sum(w => SpeechPieceWordAlignment.Normalize(w).Length);

    /// <summary>
    /// The piece's text in the spelling of its neighbour, when the piece says the first words of the
    /// next line or the last words of the previous one and they stand there in Latin letters; else null.
    /// The piece keeps its own capital letter and its own mark at the end.
    /// </summary>
    public static string? SpelledAsNeighbour(string? own, string? previousLine, string? nextLine)
    {
        var words = Words(own);
        if (words.Count == 0 || words.Count > MaxRepeatedWords)
            return null;

        var next = Words(nextLine);
        if (next.Count >= words.Count && Repeats(words, next.GetRange(0, words.Count)))
            return Respell(own!, next.GetRange(0, words.Count));

        var previous = Words(previousLine);
        if (previous.Count >= words.Count && Repeats(words, previous.GetRange(previous.Count - words.Count, words.Count)))
            return Respell(own!, previous.GetRange(previous.Count - words.Count, words.Count));

        return null;
    }

    /// <summary>
    /// The piece's text in the spelling of the answer for the piece with audio around it: the run of
    /// Latin words of that answer whose letters sound most like the piece, when they are at least
    /// <see cref="RunSimilarity"/> alike; else null (the answer left the piece out, or kept it Russian).
    /// The letters of the run are compared as one string, so «Номеакуэрдо» finds «no me acuerdo». A run
    /// does not go over the end of a sentence. The piece keeps its own capital letter and end mark.
    /// </summary>
    public static string? SpelledAsHeardAround(string? own, string? around)
    {
        var words = Words(own);
        var letters = Letters(words);
        if (letters.Length == 0)
            return null;

        var heard = Words(around);
        List<string>? best = null;
        var bestSimilarity = 0.0;
        for (var from = 0; from < heard.Count; from++)
        {
            for (var count = 1; count <= words.Count + ExtraRunWords && from + count <= heard.Count; count++)
            {
                var last = heard[from + count - 1];
                if (!IsLatinWord(last))
                    break;

                var run = heard.GetRange(from, count);
                var similarity = SpeechPieceWordAlignment.Similarity(letters, Letters(run));
                if (similarity > bestSimilarity)
                {
                    best = run;
                    bestSimilarity = similarity;
                }

                if (EndsSentence(last))
                    break;
            }
        }

        return best != null && bestSimilarity >= RunSimilarity ? Respell(own!, best) : null;
    }

    private static string Letters(IEnumerable<string> words) =>
        string.Concat(words.Select(SpeechPieceWordAlignment.Normalize));

    private static bool EndsSentence(string word)
    {
        var end = word.Length;
        while (end > 0 && word[end - 1] is '"' or '»' or ')' or '”')
            end--;
        return end > 0 && word[end - 1] is '.' or '!' or '?' or '…';
    }

    private static bool Repeats(IReadOnlyList<string> own, IReadOnlyList<string> neighbour)
    {
        for (var i = 0; i < own.Count; i++)
        {
            if (!IsLatinWord(neighbour[i]) || !SameWord(own[i], neighbour[i]))
                return false;
        }

        return true;
    }

    private static bool SameWord(string a, string b)
    {
        var x = SpeechPieceWordAlignment.Normalize(a);
        var y = SpeechPieceWordAlignment.Normalize(b);
        if (x.Length == 0 || y.Length == 0)
            return false;
        if (Math.Min(x.Length, y.Length) <= ShortWordLetters)
            return x == y;
        return SpeechPieceWordAlignment.Similarity(x, y) >= SameWordSimilarity;
    }

    private static string Respell(string own, IReadOnlyList<string> neighbourWords)
    {
        var sb = new StringBuilder();
        foreach (var word in neighbourWords)
        {
            var bare = Bare(word);
            if (bare.Length == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(bare);
        }

        var trimmed = own.Trim();
        var firstLetter = trimmed.FirstOrDefault(char.IsLetter);
        if (sb.Length > 0 && firstLetter != default && char.IsUpper(firstLetter))
            sb[0] = char.ToUpperInvariant(sb[0]);

        var end = trimmed.Length;
        while (end > 0 && !char.IsLetterOrDigit(trimmed[end - 1]))
            end--;
        sb.Append(trimmed, end, trimmed.Length - end);
        return sb.ToString();
    }

    /// <summary>The word without the marks around it: «¿tomar?» is «tomar».</summary>
    private static string Bare(string word)
    {
        int from = 0, to = word.Length;
        while (from < to && !char.IsLetterOrDigit(word[from]))
            from++;
        while (to > from && !char.IsLetterOrDigit(word[to - 1]))
            to--;
        return word[from..to];
    }

    /// <summary>Letters of the Latin alphabet with its accents stand before U+0370; Cyrillic is U+0400–U+04FF.</summary>
    private static bool IsLatinWord(string word) =>
        !word.Any(IsCyrillic) && word.Any(c => char.IsLetter(c) && c < 'Ͱ');

    private static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';

    private static List<string> Words(string? text) =>
        (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(t => t.Any(char.IsLetterOrDigit)).ToList();
}
