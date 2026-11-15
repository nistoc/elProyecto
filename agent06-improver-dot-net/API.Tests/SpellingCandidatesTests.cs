using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>
/// The Spanish a transcript holds, the words whose spelling is in doubt, and how the transcript writes its
/// Spanish — all read off the transcript by code.
/// </summary>
public class SpellingCandidatesTests
{
    private static string Line(string text, string time = "12.50", string speaker = "Olga") =>
        $"- {time} {speaker}: \"{text}\"\n";

    private static readonly string[] Transcript =
    {
        Line("Vale, ¿cómo estás? Tengo una casa."),
        Line("Да, вале, это мой дом. Como una casa."),
        Line("Кiero ir, Nет. Como siempre: cómo, cómo."),
        Line("Мой web-дизайнер и email'а нет."),
        Line("Каса, tengo. Пошли домой."),
    };

    [Fact]
    public void The_lexicon_keeps_each_word_as_the_transcript_spells_it_most_often()
    {
        var lexicon = SpanishLexicon.Of(Transcript);

        Assert.Equal("cómo", lexicon.Find("como")!.Spelling); // 3 times with the accent, 2 without
        Assert.Equal(5, lexicon.Find("Cómo")!.Count);
        Assert.Equal("vale", lexicon.Find("VALE")!.Spelling);
        Assert.Null(lexicon.Find("olga")); // a speaker label is not speech
        Assert.DoesNotContain("iero", lexicon.Words); // a word of both alphabets is not Spanish yet
        Assert.Contains("casa", lexicon.Words);
        // vale, cómo ×5, estás, tengo ×2, una ×2, casa ×2, ir, siempre, web-дизайнер and email'а are not Latin words
        Assert.Equal(15, lexicon.WordCount);
    }

    [Fact]
    public void A_word_in_Cyrillic_is_found_in_the_lexicon_by_its_sound()
    {
        var lexicon = SpanishLexicon.Of(Transcript);

        var match = lexicon.SoundsLike("Вале,")!;
        Assert.Equal("vale", match.Entry.Spelling);
        Assert.Equal(1, match.Likeness);
        Assert.Equal("cómo", lexicon.SoundsLike("комо")!.Entry.Spelling);
        Assert.Null(lexicon.SoundsLike("дом"));
        Assert.Null(lexicon.SoundsLike("ир")); // too short to judge by sound
    }

    [Fact]
    public void Candidates_are_the_words_of_both_alphabets_and_the_words_that_sound_Spanish()
    {
        var lexicon = SpanishLexicon.Of(Transcript);

        var found = SpellingCandidates.Find(Transcript, lexicon);

        Assert.Equal(
            new[]
            {
                (1, 1, "вале", SpellingCandidates.Kind.SoundsSpanish, "vale"),
                (2, 0, "Кiero", SpellingCandidates.Kind.BothAlphabets, null),
                (2, 2, "Nет", SpellingCandidates.Kind.BothAlphabets, null),
                (4, 0, "Каса", SpellingCandidates.Kind.SoundsSpanish, "casa"),
            },
            found.Select(c => (c.Line, c.Token, c.Word, c.Kind, c.Spanish?.Spelling)));
    }

    [Theory]
    [InlineData("web-дизайнером", true)]
    [InlineData("email'а", true)]
    [InlineData("Кiero", false)]
    [InlineData("полу-casа", false)] // the second part is itself of both alphabets
    public void A_word_joined_of_a_Latin_and_a_Cyrillic_part_is_not_a_broken_word(string word, bool compound)
    {
        Assert.Equal(compound, SpellingCandidates.IsCompound(word));
    }

    [Theory]
    [InlineData(18, 2284, false)] // a participant track: Spanish is in Latin letters, a slip now and then
    [InlineData(71, 1587, false)]
    [InlineData(6, 20, false)] // too few words to call it a habit
    [InlineData(31, 116, true)] // the older transcriber: a good part of the Spanish is in Cyrillic
    [InlineData(57, 367, true)]
    public void The_habit_of_a_transcript_is_read_off_its_own_words(int soundingSpanish, int inLatin, bool writesSpanishInCyrillic)
    {
        Assert.Equal(writesSpanishInCyrillic, new SpellingCandidates.Habit(soundingSpanish, inLatin).WritesSpanishInCyrillic);
    }
}
