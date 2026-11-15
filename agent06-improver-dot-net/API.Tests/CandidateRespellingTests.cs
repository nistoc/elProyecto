using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>
/// The words in doubt from the question to the new spelling: which words are put to the judge, how a question
/// looks, and what the code writes for each answer.
/// </summary>
public class CandidateRespellingTests
{
    private static string Line(string text, string time = "12.50", string speaker = "Olga") =>
        $"- {time} {speaker}: \"{text}\"\n";

    private static readonly string[] Transcript =
    {
        Line("Vale, tengo una casa en Madrid. Un tema.", "1.00"),
        Line("Да, вале, это мой дом.", "2.00"),
        Line("Каса у меня большая, Теngo и Nет.", "3.00"),
        Line("Una casa эн Madrid, это тема.", "4.00"),
        Line("Ну, вале, пошли. Кiero casaчка.", "5.00"),
    };

    private static (List<string> Lines, List<CandidateRespelling.Ask> Asks, HashSet<string> Cyrillic) Planned(int wordsPerRequest = 10)
    {
        var lines = Transcript.ToList();
        var found = SpellingCandidates.Find(lines, SpanishLexicon.Of(lines));
        return (lines, CandidateRespelling.Plan(found, wordsPerRequest), CandidateRespelling.CyrillicWordsOf(lines));
    }

    private static Dictionary<int, SaidIn> Answers(CandidateRespelling.Ask ask, Func<SpellingCandidates.Candidate, SaidIn?> judge)
    {
        var said = new Dictionary<int, SaidIn>();
        for (var i = 0; i < ask.Words.Count; i++)
        {
            if (judge(ask.Words[i]) is { } answer)
                said[i + 1] = answer;
        }

        return said;
    }

    private const SaidIn Spanish = SaidIn.Spanish;
    private const SaidIn Russian = SaidIn.Russian;

    [Fact]
    public void The_words_of_one_line_are_asked_in_one_request()
    {
        var (_, asks, _) = Planned(wordsPerRequest: 3);

        Assert.Equal(2, asks.Count);
        Assert.Equal(new[] { 1, 2 }, asks[0].Lines);
        Assert.Equal(new[] { "вале", "Каса", "Теngo", "Nет" }, asks[0].Words.Select(w => w.Word));
        Assert.Equal(new[] { 3, 4 }, asks[1].Lines);
        Assert.Equal(new[] { "эн", "тема", "вале", "Кiero", "casaчка" }, asks[1].Words.Select(w => w.Word));
    }

    [Fact]
    public void A_word_that_is_only_close_to_a_Spanish_one_is_not_asked()
    {
        var lines = new[] { Line("Un momento, por favor. La casa."), Line("Один момент, каса.") };
        var found = SpellingCandidates.Find(lines, SpanishLexicon.Of(lines));

        // «момент» sounds like "momento" without its last sound: a Russian word nearly every time.
        Assert.Equal(new[] { "момент", "каса" }, found.Select(c => c.Word));
        Assert.Equal(new[] { "каса" }, CandidateRespelling.Plan(found).Single().Words.Select(w => w.Word));
    }

    [Fact]
    public void A_question_is_the_word_and_its_line_with_the_word_marked()
    {
        var (lines, asks, _) = Planned();

        var questions = CandidateRespelling.QuestionsOf(lines, asks.Single());

        Assert.Equal(new SpellingQuestion(1, "вале", "Да, [[вале]], это мой дом."), questions[0]);
        Assert.Equal(new SpellingQuestion(4, "Nет", "Каса у меня большая, Теngo и [[Nет]]."), questions[3]);
        Assert.Equal(Enumerable.Range(1, 9), questions.Select(q => q.Id));
    }

    [Fact]
    public void A_long_line_is_sent_as_the_words_around_the_word()
    {
        var before = string.Join(' ', Enumerable.Range(1, 20).Select(i => "слово" + i));
        var lines = new[] { Line("Vale."), Line($"{before} вале и ещё {before}") };
        var found = SpellingCandidates.Find(lines, SpanishLexicon.Of(lines));

        var text = CandidateRespelling.QuestionsOf(lines, CandidateRespelling.Plan(found).Single()).Single().Text;

        Assert.StartsWith("… слово7 ", text);
        Assert.Contains(" слово20 [[вале]] и ещё слово1 ", text);
        Assert.EndsWith(" слово12 …", text);
    }

    [Fact]
    public void Each_answer_is_written_by_its_own_rule()
    {
        var (lines, asks, cyrillic) = Planned();
        var ask = asks.Single();

        var applied = CandidateRespelling.Apply(lines, ask, Answers(ask, w => w.Word switch
        {
            "вале" => w.Line == 1 ? Spanish : Russian,                 // Spanish in one line, Russian in another
            "Каса" or "Теngo" or "эн" or "Кiero" => Spanish,
            "Nет" or "тема" => Russian,
            "casaчка" => SaidIn.Both,
            _ => (SaidIn?)null
        }), cyrillic);

        Assert.Equal(Line("Да, vale, это мой дом.", "2.00"), lines[1]);
        Assert.Equal(Line("Casa у меня большая, Tengo и Нет.", "3.00"), lines[2]);
        Assert.Equal(Line("Una casa en Madrid, это тема.", "4.00"), lines[3]);
        Assert.Equal(Line("Ну, вале, пошли. Кiero casaчка.", "5.00"), lines[4]); // «Кiero»: «К» may be c, qu or k
        Assert.Equal(
            new[] { "2.00 «вале,» → «vale,»", "3.00 «Каса» → «Casa»", "3.00 «Теngo» → «Tengo»", "3.00 «Nет.» → «Нет.»", "4.00 «эн» → «en»" },
            applied.Changes.Select(c => $"{c.At} «{c.Was}» → «{c.Now}»"));
        Assert.Equal(2, applied.LeftAsTheyWere[CandidateRespelling.Left.SaidInRussian]);
        Assert.Equal(1, applied.LeftAsTheyWere[CandidateRespelling.Left.OfBothLanguages]);
        Assert.Equal(1, applied.LeftAsTheyWere[CandidateRespelling.Left.NoSafeSpelling]);
    }

    [Fact]
    public void A_word_without_an_answer_stays()
    {
        var (lines, asks, cyrillic) = Planned();

        var applied = CandidateRespelling.Apply(lines, asks.Single(), new Dictionary<int, SaidIn>(), cyrillic);

        Assert.Equal(Transcript, lines);
        Assert.Empty(applied.Changes);
        Assert.Equal(9, applied.LeftAsTheyWere[CandidateRespelling.Left.NoAnswer]);
    }

    [Fact]
    public void A_word_the_answers_disagree_on_stays()
    {
        var (lines, asks, cyrillic) = Planned();
        var ask = asks.Single();
        var first = Answers(ask, w => w.Word is "Каса" or "Теngo" or "эн" ? Spanish : w.Word == "Nет" ? Russian : null);
        // Asked again: another language for «Каса», no answer for «эн», an answer for «тема» where the first had none.
        var again = Answers(ask, w => w.Word == "Теngo" ? Spanish : w.Word is "Nет" or "Каса" or "тема" ? Russian : null);

        var said = CandidateRespelling.Agreed(new[] { first, again }, out var differ);
        var applied = CandidateRespelling.Apply(lines, ask, said, cyrillic, differ);

        Assert.Equal(new[] { "Теngo", "Nет" }, said.Keys.OrderBy(n => n).Select(n => ask.Words[n - 1].Word));
        Assert.Equal(new[] { "Каса", "эн", "тема" }, differ.OrderBy(n => n).Select(n => ask.Words[n - 1].Word));
        Assert.Equal(Line("Каса у меня большая, Tengo и Нет.", "3.00"), lines[2]);
        Assert.Equal(Line("Una casa эн Madrid, это тема.", "4.00"), lines[3]);
        Assert.Equal(3, applied.LeftAsTheyWere[CandidateRespelling.Left.AnswersDiffer]);
        Assert.Equal(4, applied.LeftAsTheyWere[CandidateRespelling.Left.NoAnswer]); // «вале» twice, «Кiero», «casaчка»
    }

    [Fact]
    public void One_answer_alone_is_taken_as_it_is()
    {
        var (_, asks, _) = Planned();
        var only = Answers(asks.Single(), w => w.Word == "Каса" ? Spanish : w.Word == "Nет" ? Russian : null);

        var said = CandidateRespelling.Agreed(new[] { only }, out var differ);

        Assert.Equal(only, said);
        Assert.Empty(differ);
    }

    [Fact]
    public void A_word_called_Spanish_is_written_anew_also_when_Russian_has_the_same_word()
    {
        // The code takes the word of the judge: it has no dictionary of Russian to hold «тема» back with.
        var (lines, asks, cyrillic) = Planned();
        var ask = asks.Single();

        CandidateRespelling.Apply(lines, ask, Answers(ask, w => w.Word == "тема" ? Spanish : null), cyrillic);

        Assert.Equal(Line("Una casa эн Madrid, это tema.", "4.00"), lines[3]);
    }

    [Fact]
    public void A_short_word_is_written_anew_only_with_Spanish_on_every_side_after_the_other_words()
    {
        // «эн» stands between two words in doubt: it is Spanish only if both turn out Spanish.
        var lines = new List<string> { Line("Una casa en Madrid."), Line("Каса эн Мадрид, да.") };
        var found = SpellingCandidates.Find(lines, SpanishLexicon.Of(lines));
        var ask = CandidateRespelling.Plan(found).Single();
        Assert.Equal(new[] { "Каса", "эн", "Мадрид" }, ask.Words.Select(w => w.Word));
        var cyrillic = CandidateRespelling.CyrillicWordsOf(lines);

        var oneRussian = lines.ToList();
        var applied = CandidateRespelling.Apply(oneRussian, ask, Answers(ask, w => w.Word == "Мадрид" ? Russian : Spanish), cyrillic);
        Assert.Equal(Line("Casa эн Мадрид, да."), oneRussian[1]);
        Assert.Equal(1, applied.LeftAsTheyWere[CandidateRespelling.Left.NoSpanishNextTo]);

        CandidateRespelling.Apply(lines, ask, Answers(ask, _ => Spanish), cyrillic);
        Assert.Equal(Line("Casa en Madrid, да."), lines[1]);
    }

    [Theory]
    [InlineData("вале", "vale", "vale")]
    [InlineData("Вале", "vale", "Vale")]
    [InlineData("ВАЛЕ", "vale", "VALE")]
    [InlineData("Эста", "está", "Está")]
    public void The_spelling_of_the_transcript_gets_the_capital_letters_of_the_word(string was, string spelling, string expected)
    {
        Assert.Equal(expected, CandidateRespelling.Cased(was, spelling));
    }

    [Theory]
    [InlineData("Теngo", "Tengo")]
    [InlineData("casа", "casa")]   // the last letter is Cyrillic
    [InlineData("Кiero", null)]    // «К»: c, qu or k
    [InlineData("Nет", null)]      // more Cyrillic letters than Latin ones: not a Spanish word with a slip
    [InlineData("amигоs", null)]   // three letters to write anew
    public void A_Spanish_word_of_both_alphabets_gets_only_letters_with_one_reading(string word, string? expected)
    {
        Assert.Equal(expected, CandidateRespelling.InLatin(word));
    }

    [Theory]
    [InlineData("Nет", "Нет")]
    [InlineData("самa", "сама")]        // the last letter is Latin
    [InlineData("pomню", "помню")]      // three letters, and the transcript has the word
    [InlineData("tibя", null)]          // three letters, and «тибя» is no word of the transcript
    [InlineData("Yсли", null)]          // y: more than one sound
    [InlineData("casaчка", null)]       // four letters, not a word of the transcript
    public void A_Russian_word_of_both_alphabets_gets_its_Latin_letters_by_sound(string word, string? expected)
    {
        var cyrillicWords = new HashSet<string> { "помню", "тебя", "если" };

        Assert.Equal(expected, CandidateRespelling.InCyrillic(word, cyrillicWords));
    }

    [Fact]
    public void Only_the_tokens_named_change_and_the_rest_of_the_line_stays_to_the_letter()
    {
        var line = "- 12.50 Olga:  \"¿Вале?   Да,\tвале…\"  \r\n";

        var written = CandidateRespelling.ReplaceTokens(line, new Dictionary<int, string> { [0] = "¿Vale?", [2] = "vale…" });

        Assert.Equal("- 12.50 Olga:  \"¿Vale?   Да,\tvale…\"  \r\n", written);
        Assert.Same(line, CandidateRespelling.ReplaceTokens(line, new Dictionary<int, string>()));
    }
}
