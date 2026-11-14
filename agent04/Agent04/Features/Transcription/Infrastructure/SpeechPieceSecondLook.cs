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
/// <para>
/// The same answer settles a doubt about a line that is not lone. The text of a whole chunk now and then
/// gives Russian speech in Spanish — translated («Это уже прошлое» as «Esto ya es pasado»), or turned into Spanish-looking
/// sounds. Such a line is seen by setting it against the piece asked alone: the line is Spanish, the piece
/// is Russian, and they do not sound alike (<see cref="LooksTranslated"/>). Neither is believed as it is —
/// a piece alone mishears too. The piece is asked again with audio around it, and the line is replaced
/// only when that answer has a run that sounds like what the piece heard alone, and sounds more like it
/// than anything in the answer sounds like the line (<see cref="SaidAsHeardAround"/>).
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

    /// <summary>A line is in another language than its piece when its share of Latin words is this much higher.</summary>
    public const double OtherLanguageShare = 0.4;

    /// <summary>
    /// The same speech in two spellings («керемос» and «queremos») is at least this alike by its letters;
    /// a translation is not. On the lesson of 2026-11-13 translated lines stood at 0.07–0.47.
    /// </summary>
    public const double UnlikeSimilarity = 0.5;

    /// <summary>A piece of fewer letters («Угу.» against «Mhm.») says too little to doubt a line by it.</summary>
    public const int MinLettersToDoubt = 4;

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
        var (run, similarity) = ClosestRun(Words(own), Words(around), spanishOnly: true);
        return run != null && similarity >= RunSimilarity ? Respell(own!, run) : null;
    }

    /// <summary>
    /// True when the line the whole text gave to a piece is written in Spanish where the piece asked alone
    /// is Russian, and the two do not sound alike: the line may be a translation of what was said. Two
    /// spellings of the same speech are not that: «queremos salir» sounds like «керемос салир».
    /// </summary>
    public static bool LooksTranslated(string? line, string? own)
    {
        var lineWords = Words(line);
        var ownWords = Words(own);
        if (lineWords.Count == 0 || ownWords.Count == 0)
            return false;

        var ownLetters = Letters(ownWords);
        if (ownLetters.Length < MinLettersToDoubt)
            return false;
        if (LatinShare(lineWords) - LatinShare(ownWords) < OtherLanguageShare)
            return false;
        return SpeechPieceWordAlignment.Similarity(Letters(lineWords), ownLetters) < UnlikeSimilarity;
    }

    /// <summary>
    /// What a piece with a doubted line says, by the answer for the piece asked again with audio around
    /// it: the run of words of that answer, in any script, that sounds most like what the piece heard
    /// alone — when the two are at least <see cref="RunSimilarity"/> alike, and more alike than any run of
    /// the answer is to the line. Else null: the answer stands by the line, or has neither. The run is
    /// given as it stands in that answer, with the marks inside it; cut out of the middle of a sentence
    /// it gets the capital letter and the end mark of the piece.
    /// </summary>
    public static string? SaidAsHeardAround(string? own, string? line, string? around)
    {
        var heard = Words(around);
        var (ownRun, ownSimilarity) = ClosestRun(Words(own), heard, spanishOnly: false);
        if (ownRun == null || ownSimilarity < RunSimilarity)
            return null;

        var (_, lineSimilarity) = ClosestRun(Words(line), heard, spanishOnly: false);
        return ownSimilarity > lineSimilarity ? AsLine(own!, ownRun) : null;
    }

    /// <summary>A run of an answer as a line of its own: no comma left hanging at its end, the piece's capital letter and end mark.</summary>
    private static string AsLine(string own, IReadOnlyList<string> run)
    {
        var sb = new StringBuilder(string.Join(" ", run).TrimEnd(',', ';', ':', '-', '–', '—', ' '));
        var trimmed = own.Trim();
        var firstLetter = trimmed.FirstOrDefault(char.IsLetter);
        if (firstLetter != default && char.IsUpper(firstLetter))
        {
            for (var i = 0; i < sb.Length; i++)
            {
                if (!char.IsLetter(sb[i]))
                    continue;
                sb[i] = char.ToUpperInvariant(sb[i]);
                break;
            }
        }

        if (sb.Length > 0 && char.IsLetterOrDigit(sb[^1]))
        {
            var end = trimmed.Length;
            while (end > 0 && !char.IsLetterOrDigit(trimmed[end - 1]))
                end--;
            sb.Append(trimmed, end, trimmed.Length - end);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The run of <paramref name="heard"/> whose letters, read as one string, sound most like the letters
    /// of <paramref name="words"/>, and how alike they are. A run of Spanish words only holds no Cyrillic
    /// word and does not go over the end of a sentence.
    /// </summary>
    private static (List<string>? Run, double Similarity) ClosestRun(List<string> words, List<string> heard, bool spanishOnly)
    {
        var letters = Letters(words);
        if (letters.Length == 0)
            return (null, 0.0);

        List<string>? best = null;
        var bestSimilarity = 0.0;
        for (var from = 0; from < heard.Count; from++)
        {
            for (var count = 1; count <= words.Count + ExtraRunWords && from + count <= heard.Count; count++)
            {
                var last = heard[from + count - 1];
                if (spanishOnly && !IsLatinWord(last))
                    break;

                var run = heard.GetRange(from, count);
                var similarity = SpeechPieceWordAlignment.Similarity(letters, Letters(run));
                if (similarity > bestSimilarity)
                {
                    best = run;
                    bestSimilarity = similarity;
                }

                if (spanishOnly && EndsSentence(last))
                    break;
            }
        }

        return (best, bestSimilarity);
    }

    private static double LatinShare(IReadOnlyCollection<string> words) =>
        words.Count == 0 ? 0.0 : (double)words.Count(IsLatinWord) / words.Count;

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
