using System.Text;
using TranslationImprover.Features.Refine.Application;
using static TranslationImprover.Features.Refine.Infrastructure.TranscriptWords;
using Candidate = TranslationImprover.Features.Refine.Infrastructure.SpellingCandidates.Candidate;
using Kind = TranslationImprover.Features.Refine.Infrastructure.SpellingCandidates.Kind;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// The words in doubt of a transcript from the question to the new spelling. The judge is asked about the words
/// only (<see cref="Plan"/>, <see cref="QuestionsOf"/>) and answers with a language; what is written then is
/// decided here (<see cref="Apply"/>):
/// <list type="bullet">
/// <item>a word in Cyrillic letters said in Spanish gets the spelling the transcript itself has for the word it
/// sounds like — never a spelling of the model;</item>
/// <item>a word of both alphabets said in Spanish gets that spelling too, or, when the transcript has no such
/// word, its one or two Cyrillic letters written in Latin ones — only letters with one reading;</item>
/// <item>a word of both alphabets said in Russian gets its Latin letters written in Cyrillic ones, when the
/// result is a word the transcript has in Cyrillic, or when there are one or two such letters;</item>
/// <item>a word of one or two sounds said in Spanish is respelled only when, after all of the above, Spanish
/// stands on every side it has a neighbour on;</item>
/// <item>everything else stays as it was: a word said in Russian, a word made of both languages on purpose,
/// a word without an answer, a word the answers disagree on (<see cref="Agreed"/>), a word no rule above can
/// spell.</item>
/// </list>
/// Nothing of a line but the words asked about can change: the model returns no text.
/// </summary>
public static class CandidateRespelling
{
    /// <summary>How many words one request to the judge holds; the words of one line are never parted.</summary>
    public const int WordsPerRequest = 10;

    /// <summary>
    /// How many times one request is made. One answer is one draw: on 33 lesson dialogues the judge, asked again
    /// with the same prompt at temperature 0, named another language for 12 words of 451, and five of them were
    /// Russian words it now called Spanish. A word is written anew only when every answer names the same
    /// language for it (<see cref="Agreed"/>).
    /// </summary>
    public const int TimesAsked = 2;

    /// <summary>How many words of the line are sent on each side of the word asked about.</summary>
    private const int WordsAround = 14;

    /// <summary>One request to the judge: whole lines with their words in doubt.</summary>
    /// <param name="Lines">Indices of the lines, rising.</param>
    /// <param name="Words">The words in the order they are asked; the number of a word is its place here, from 1.</param>
    public sealed record Ask(IReadOnlyList<int> Lines, IReadOnlyList<Candidate> Words);

    /// <summary>Why a word in doubt stays as it was.</summary>
    public enum Left
    {
        SaidInRussian,
        OfBothLanguages,
        AnswersDiffer,
        NoAnswer,
        NoSafeSpelling,
        NoSpanishNextTo,
        RequestFailed
    }

    /// <param name="At">The time mark of the line.</param>
    /// <param name="Was">The token as it was, with its punctuation.</param>
    /// <param name="Token">Index of the token in the text of the line.</param>
    public sealed record Change(int Line, int Token, string At, string Was, string Now);

    public sealed record Applied(IReadOnlyList<Change> Changes, IReadOnlyDictionary<Left, int> LeftAsTheyWere);

    /// <summary>
    /// Is the word put to the judge? A word in Cyrillic letters is, when it has exactly the sound of a Spanish
    /// word of the transcript. One that is only close to it («момент» — "momento", «проблему» — "problema") is
    /// a Russian word nearly every time (225 such words on 33 lesson dialogues, a handful of them Spanish), and
    /// where it is Spanish the nearest word of the transcript is often another word («портате» — "portarte").
    /// </summary>
    public static bool IsAsked(Candidate word) => word.Kind != Kind.SoundsSpanish || word.Likeness >= 1;

    /// <summary>The requests to the judge for the words that are asked (<see cref="IsAsked"/>).</summary>
    public static List<Ask> Plan(IReadOnlyList<Candidate> candidates, int wordsPerRequest = WordsPerRequest)
    {
        var asks = new List<Ask>();
        var lines = new List<int>();
        var words = new List<Candidate>();
        foreach (var ofLine in candidates.Where(IsAsked).GroupBy(c => c.Line).OrderBy(g => g.Key))
        {
            lines.Add(ofLine.Key);
            words.AddRange(ofLine.OrderBy(c => c.Token));
            if (words.Count < wordsPerRequest)
                continue;
            asks.Add(new Ask(lines.ToList(), words.ToList()));
            lines.Clear();
            words.Clear();
        }

        if (words.Count > 0)
            asks.Add(new Ask(lines, words));
        return asks;
    }

    public static List<SpellingQuestion> QuestionsOf(IReadOnlyList<string> lines, Ask ask)
    {
        var questions = new List<SpellingQuestion>(ask.Words.Count);
        for (var i = 0; i < ask.Words.Count; i++)
        {
            var word = ask.Words[i];
            questions.Add(new SpellingQuestion(i + 1, word.Word, Marked(Tokens(TextOf(lines[word.Line])), word.Token)));
        }

        return questions;
    }

    /// <summary>The words of the line around one of them, that one marked: «да, [[вале]], пошли».</summary>
    private static string Marked(List<string> tokens, int token)
    {
        int from = Math.Max(0, token - WordsAround), to = Math.Min(tokens.Count, token + WordsAround + 1);
        var parts = new List<string>(to - from + 2);
        if (from > 0)
            parts.Add("…");
        for (var t = from; t < to; t++)
        {
            if (t != token)
            {
                parts.Add(tokens[t]);
                continue;
            }

            var (lead, word, trail) = Parts(tokens[t]);
            parts.Add($"{lead}[[{word}]]{trail}");
        }

        if (to < tokens.Count)
            parts.Add("…");
        return string.Join(' ', parts);
    }

    /// <summary>The words of the transcript written in Cyrillic letters only, lower-cased: its Russian, and the Spanish it wrote by ear.</summary>
    public static HashSet<string> CyrillicWordsOf(IEnumerable<string> lines)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            foreach (var token in Tokens(TextOf(line)))
            {
                if (HasCyrillic(token) && !HasLatin(token))
                    words.Add(LettersOf(token));
            }
        }

        return words;
    }

    /// <summary>
    /// What the answers to one request come to when it was made more than once: a word has a language only when
    /// every answer names that language for it.
    /// </summary>
    /// <param name="answers">Each answer by the number of the word.</param>
    /// <param name="differ">The numbers of the words the answers disagree on, or that only some of them answer.</param>
    public static Dictionary<int, SaidIn> Agreed(IReadOnlyList<IReadOnlyDictionary<int, SaidIn>> answers, out HashSet<int> differ)
    {
        var agreed = new Dictionary<int, SaidIn>();
        differ = new HashSet<int>();
        foreach (var number in answers.SelectMany(a => a.Keys).Distinct())
        {
            var first = answers[0].GetValueOrDefault(number, SaidIn.Unknown);
            if (first != SaidIn.Unknown && answers.All(a => a.GetValueOrDefault(number, SaidIn.Unknown) == first))
                agreed[number] = first;
            else
                differ.Add(number);
        }

        return agreed;
    }

    /// <summary>Writes into <paramref name="lines"/> what the answers of the judge come to for the words of one request.</summary>
    /// <param name="said">The answer by the number of the word (its place in <see cref="Ask.Words"/>, from 1).</param>
    /// <param name="cyrillicWords">The words of the transcript in Cyrillic letters (<see cref="CyrillicWordsOf"/>).</param>
    /// <param name="differ">The numbers of the words the answers disagree on (<see cref="Agreed"/>); they stay as they were.</param>
    public static Applied Apply(IList<string> lines, Ask ask, IReadOnlyDictionary<int, SaidIn> said, IReadOnlySet<string> cyrillicWords,
        IReadOnlySet<int>? differ = null)
    {
        var changes = new List<Change>();
        var left = new Dictionary<Left, int>();
        void Leave(Left why) => left[why] = left.GetValueOrDefault(why) + 1;

        var numbered = ask.Words.Select((word, i) => (Word: word, Number: i + 1, Said: said.GetValueOrDefault(i + 1, SaidIn.Unknown))).ToList();
        foreach (var ofLine in numbered.GroupBy(w => w.Word.Line))
        {
            var line = lines[ofLine.Key];
            var tokens = Tokens(TextOf(line));
            var now = new Dictionary<int, string>();

            // First the words judged on their own, then the short ones: those need to know what stands next to them now.
            foreach (var (word, number, language) in ofLine.OrderBy(w => w.Word.Kind == Kind.ShortInSpanish))
            {
                if (word.Token >= tokens.Count)
                {
                    Leave(Left.NoSafeSpelling);
                    continue;
                }

                if (differ != null && differ.Contains(number))
                {
                    Leave(Left.AnswersDiffer);
                    continue;
                }

                var why = Respell(word, language, cyrillicWords, out var spelling);
                if (why == null && word.Kind == Kind.ShortInSpanish
                    && !SpellingCandidates.SpanishOnEverySide(tokens, word.Token, k => IsLatinWord(now.GetValueOrDefault(k, tokens[k]))))
                    why = Left.NoSpanishNextTo;
                if (why != null)
                {
                    Leave(why.Value);
                    continue;
                }

                var (lead, was, trail) = Parts(tokens[word.Token]);
                if (string.Equals(was, spelling, StringComparison.Ordinal))
                    continue; // nothing to write
                now[word.Token] = lead + spelling + trail;
            }

            if (now.Count == 0)
                continue;
            lines[ofLine.Key] = ReplaceTokens(line, now);
            var at = TimeMarkOf(line);
            changes.AddRange(now.OrderBy(kv => kv.Key).Select(kv => new Change(ofLine.Key, kv.Key, at, tokens[kv.Key], kv.Value)));
        }

        return new Applied(changes, left);
    }

    /// <summary>The new spelling of a word for the answer it got; when it stays, the reason.</summary>
    private static Left? Respell(Candidate word, SaidIn said, IReadOnlySet<string> cyrillicWords, out string spelling)
    {
        spelling = word.Word;
        switch (said)
        {
            case SaidIn.Unknown:
                return Left.NoAnswer;
            case SaidIn.Both:
                return Left.OfBothLanguages;
            case SaidIn.Russian when word.Kind != Kind.BothAlphabets:
                return Left.SaidInRussian;
            case SaidIn.Russian:
            {
                var inCyrillic = InCyrillic(word.Word, cyrillicWords);
                if (inCyrillic == null)
                    return Left.NoSafeSpelling;
                spelling = inCyrillic;
                return null;
            }
            default:
            {
                // The spelling of the transcript only for exactly its sound; a word that is merely close may be another word.
                var inLatin = word.Spanish != null && word.Likeness >= 1 ? Cased(word.Word, word.Spanish.Spelling) : InLatin(word.Word);
                if (inLatin == null)
                    return Left.NoSafeSpelling;
                spelling = inLatin;
                return null;
            }
        }
    }

    /// <summary>A spelling of the transcript with the capital letters of the word it replaces: «Вале» — "Vale".</summary>
    public static string Cased(string was, string spelling)
    {
        var letters = was.Where(char.IsLetter).ToArray();
        if (letters.Length == 0 || spelling.Length == 0 || !char.IsUpper(letters[0]))
            return spelling;
        return letters.Length > 1 && letters.All(char.IsUpper)
            ? spelling.ToUpperInvariant()
            : char.ToUpperInvariant(spelling[0]) + spelling[1..];
    }

    /// <summary>
    /// A Spanish word of both alphabets with its Cyrillic letters written in Latin ones: «Теngo» — "Tengo".
    /// Only one or two letters, fewer than the word has Latin ones (a word that is mostly Cyrillic is not a
    /// Spanish word with a slip in it), and only those that give one Latin letter whatever the word is; «к»
    /// (c, qu, k), «с» (s, c, z), «б» and «в» (b, v), «х» (j, g) are not among them. Null when the word cannot
    /// be written so.
    /// </summary>
    public static string? InLatin(string word)
    {
        if (word.Count(IsCyrillic) >= word.Count(IsLatin))
            return null;
        var sb = new StringBuilder(word.Length);
        var replaced = 0;
        foreach (var c in word)
        {
            if (!IsCyrillic(c))
            {
                sb.Append(c);
                continue;
            }

            var latin = char.ToLowerInvariant(c) switch
            {
                'а' => 'a', 'е' => 'e', 'э' => 'e', 'и' => 'i', 'о' => 'o', 'у' => 'u',
                'д' => 'd', 'л' => 'l', 'м' => 'm', 'н' => 'n', 'п' => 'p', 'р' => 'r', 'т' => 't', 'ф' => 'f',
                _ => '\0'
            };
            if (latin == '\0' || ++replaced > 2)
                return null;
            sb.Append(char.IsUpper(c) ? char.ToUpperInvariant(latin) : latin);
        }

        return sb.ToString();
    }

    /// <summary>
    /// A Russian word of both alphabets with its Latin letters written in Cyrillic ones by sound: «Nет» — «Нет».
    /// Taken when the result is a word the transcript has in Cyrillic letters, or when one or two letters were
    /// written anew and the word has more Cyrillic ones; letters that stand for more than one sound (c, h, j,
    /// q, w, x, y) are never guessed.
    /// </summary>
    public static string? InCyrillic(string word, IReadOnlySet<string> cyrillicWords)
    {
        var sb = new StringBuilder(word.Length);
        var replaced = 0;
        foreach (var c in word)
        {
            if (!IsLatin(c))
            {
                sb.Append(c);
                continue;
            }

            // á — a: the accent is not a sound of its own.
            var letter = c.ToString().Normalize(NormalizationForm.FormD)[0];
            var cyrillic = char.ToLowerInvariant(letter) switch
            {
                'a' => 'а', 'b' => 'б', 'd' => 'д', 'e' => 'е', 'f' => 'ф', 'g' => 'г', 'i' => 'и', 'k' => 'к',
                'l' => 'л', 'm' => 'м', 'n' => 'н', 'o' => 'о', 'p' => 'п', 'r' => 'р', 's' => 'с', 't' => 'т',
                'u' => 'у', 'v' => 'в', 'z' => 'з',
                _ => '\0'
            };
            if (cyrillic == '\0')
                return null;
            replaced++;
            sb.Append(char.IsUpper(letter) ? char.ToUpperInvariant(cyrillic) : cyrillic);
        }

        var result = sb.ToString();
        return (replaced <= 2 && replaced < word.Count(IsCyrillic)) || cyrillicWords.Contains(LettersOf(result)) ? result : null;
    }

    /// <summary>
    /// The line with some of its tokens written anew; the time mark, the speaker and the white space between the
    /// words stay as they are.
    /// </summary>
    /// <param name="now">The new token by its index in <see cref="TranscriptWords.Tokens"/> of the line's text.</param>
    public static string ReplaceTokens(string line, IReadOnlyDictionary<int, string> now)
    {
        if (now.Count == 0)
            return line;

        var m = Line.Match(line);
        var (head, text, tail) = m.Success
            ? (m.Groups["head"].Value, m.Groups["text"].Value, m.Groups["tail"].Value)
            : ("", line, "");
        var sb = new StringBuilder(line.Length + 8).Append(head);
        int token = 0, i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                sb.Append(text[i++]);
                continue;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;
            sb.Append(now.TryGetValue(token, out var written) ? written : text[start..i]);
            token++;
        }

        return sb.Append(tail).ToString();
    }
}
