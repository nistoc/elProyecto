using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>
/// Of what the model changed only a Spanish word written in Cyrillic letters and respelled in Latin letters is
/// kept; everything else of a line comes back as it was.
/// </summary>
public class RespellingGuardTests
{
    private static string Line(string text, string time = "12.50", string speaker = "speaker_0") =>
        $"- {time} {speaker}: \"{text}\"\n";

    /// <summary>The Spanish a made-up transcript already holds.</summary>
    private static readonly HashSet<string> Known = RespellingGuard.LatinWordsOf(new[]
    {
        Line("Vale, por favor, ¿cómo estás?"),
        Line("Tengo una casa, puede ser, puedo ir, on."),
        Line("Gracias, quiero hablar español."),
        Line("La bombilla de mi colega y el instituto, no, yo."),
    });

    private static string Checked(string original, string refined, out int letThrough, out int putBack) =>
        RespellingGuard.ApplyToLine(Line(original), Line(refined), Known, out letThrough, out putBack);

    // ---------- how a word sounds ----------

    [Theory]
    [InlineData("грасиас", "gracias")]
    [InlineData("кьеро", "quiero")]
    [InlineData("эспаньол", "español")]
    [InlineData("хамон", "jamón")]
    [InlineData("буэнос", "buenos")]
    [InlineData("мучо", "mucho")]
    [InlineData("вале", "vale")]
    [InlineData("аора", "ahora")]
    [InlineData("ямо", "llamo")]
    [InlineData("хенте", "gente")]
    [InlineData("гитарра", "guitarra")]
    [InlineData("Теngo", "tengo")] // a word of both alphabets
    [InlineData("коллега", "colega")] // a doubled Cyrillic л is one l, not the Spanish ll
    public void A_Spanish_word_sounds_the_same_from_either_alphabet(string cyrillic, string latin)
    {
        Assert.Equal(RespellingGuard.SoundForm(latin), RespellingGuard.SoundForm(cyrillic));
    }

    [Theory]
    [InlineData("бомбилья", "bombilla")] // «ль» before a vowel heard as the Spanish ll
    [InlineData("фамилья", "familia")] // and as "li"
    [InlineData("тортилья", "tortilla")]
    public void A_soft_l_before_a_vowel_is_heard_both_ways(string cyrillic, string latin)
    {
        Assert.Equal(1, RespellingGuard.Likeness(new[] { cyrillic }, new[] { latin }, out _));
    }

    [Theory]
    [InlineData("дом", "casa")] // a translation
    [InlineData("пуэдо", "puede")] // another ending
    [InlineData("жара", "shara")] // ж is not the sound of sh
    public void Another_word_does_not_sound_alike(string was, string now)
    {
        Assert.True(RespellingGuard.Likeness(new[] { was }, new[] { now }, out _) < RespellingGuard.SoundsAlikeFrom);
    }

    // ---------- what is let through ----------

    [Fact]
    public void A_Spanish_word_in_Cyrillic_next_to_Spanish_is_respelled_and_the_rest_of_the_line_stays()
    {
        var kept = Checked("Sí, вале, пошли домой.", "Sí, vale, пошли домой.", out var through, out var back);

        Assert.Equal(Line("Sí, vale, пошли домой."), kept);
        Assert.Equal(1, through);
        Assert.Equal(0, back);
    }

    [Fact]
    public void A_phrase_in_Cyrillic_carries_itself_and_the_opening_question_mark_may_come()
    {
        // Each of the two words has the other next to it: a phrase needs no Latin word around.
        Assert.Equal(Line("Ну, por favor, скажи."), Checked("Ну, пор фавор, скажи.", "Ну, por favor, скажи.", out var through, out _));
        Assert.Equal(2, through);

        Assert.Equal(Line("¿Cómo estás? Хорошо."), Checked("Комо эстас? Хорошо.", "¿Cómo estás? Хорошо.", out through, out _));
        Assert.Equal(2, through);
    }

    [Fact]
    public void A_word_of_both_alphabets_is_respelled_wherever_it_stands()
    {
        Assert.Equal(Line("Теперь Tengo время."), Checked("Теперь Теngo время.", "Теперь Tengo время.", out var through, out _));
        Assert.Equal(1, through);
    }

    [Fact]
    public void A_word_that_is_the_whole_line_is_respelled()
    {
        Assert.Equal(Line("Vale."), Checked("Вале.", "Vale.", out var through, out _));
        Assert.Equal(1, through);
    }

    // ---------- Spanish next to the word ----------

    [Theory]
    [InlineData("Да, вале, пошли домой.", "Да, vale, пошли домой.")] // a Spanish word quoted in Russian speech…
    [InlineData("Вот сам институт, у нас.", "Вот сам instituto, у нас.")] // …cannot be told from a Russian word that sounds Spanish
    [InlineData("Мой коллега пришёл.", "Мой colega пришёл.")]
    public void A_word_among_Russian_words_stays(string original, string refined)
    {
        var decisions = new List<RespellingGuard.Decision>();

        var kept = RespellingGuard.ApplyToLine(Line(original), Line(refined), Known, out var through, out var back, decisions: decisions);

        Assert.Equal(Line(original), kept);
        Assert.Equal(0, through);
        Assert.Equal(1, back);
        var decision = Assert.Single(decisions);
        Assert.False(decision.Kept);
        Assert.Equal("no Spanish next to it", decision.Why);
        Assert.Equal("12.50", decision.At);
    }

    [Fact]
    public void In_a_transcript_that_writes_Spanish_in_Cyrillic_nothing_is_asked_of_the_neighbours()
    {
        var kept = RespellingGuard.ApplyToLine(Line("Да, вале, пошли домой."), Line("Да, vale, пошли домой."), Known, out var through, out _, spanishMustBeNextTo: false);

        Assert.Equal(Line("Да, vale, пошли домой."), kept);
        Assert.Equal(1, through);
    }

    [Fact]
    public void Spanish_on_one_side_is_enough()
    {
        Assert.Equal(Line("¿El colega всегда?"), Checked("¿El коллега всегда?", "¿El colega всегда?", out var through, out _));
        Assert.Equal(1, through);

        Assert.Equal(Line("Bombilla, tengo una."), Checked("Бомбилья, tengo una.", "Bombilla, tengo una.", out through, out _));
        Assert.Equal(1, through);
    }

    // ---------- a word of one or two sounds ----------

    [Fact]
    public void A_short_word_inside_Spanish_speech_is_respelled()
    {
        Assert.Equal(Line("Tengo la casa."), Checked("Tengo ла casa.", "Tengo la casa.", out var through, out _));
        Assert.Equal(1, through);

        // At the edge of the line the one neighbour it has is asked.
        Assert.Equal(Line("Yo tengo una casa."), Checked("Йо tengo una casa.", "Yo tengo una casa.", out through, out _));
        Assert.Equal(1, through);

        // Between two words respelled with it.
        Assert.Equal(Line("Casa de bombilla."), RespellingGuard.ApplyToLine(Line("Каса де бомбилья."), Line("Casa de bombilla."),
            RespellingGuard.LatinWordsOf(new[] { Line("Una casa de la bombilla.") }), out through, out _));
        Assert.Equal(3, through);
    }

    [Theory]
    [InlineData("Tengo ла дом.", "Tengo la дом.")] // Russian on one side
    [InlineData("Ла дом.", "La дом.")]
    [InlineData("Casa и bombilla.", "Casa y bombilla.")] // a Russian word between two Spanish ones stays Russian
    [InlineData("Casa, но tengo.", "Casa, no tengo.")]
    [InlineData("Он пришёл.", "On пришёл.")]
    [InlineData("Ла.", "La.")] // alone in its line: nothing to read the language from
    public void A_short_word_is_not_respelled_otherwise(string original, string refined)
    {
        var kept = Checked(original, refined, out var through, out var back);

        Assert.Equal(Line(original), kept);
        Assert.Equal(0, through);
        Assert.Equal(1, back);
    }

    // ---------- what is put back ----------

    [Theory]
    [InlineData("Это мой дом.", "Это мой casa.")] // a Russian word translated
    [InlineData("Сейчас приду.", "Seichas приду.")] // a Russian word in Latin letters: not a word of the transcript
    [InlineData("Tengo, пуэдо завтра.", "Tengo, puede завтра.")] // the ending changed on the way
    [InlineData("Tengo una casa.", "Tengo un casa.")] // the speaker's Spanish "corrected"
    [InlineData("Sí, вале.", "Sí, vale!")] // the punctuation changed with the word
    [InlineData("Sí, вале, пошли.", "Sí, vale.")] // words dropped
    [InlineData("Sí, вале.", "Sí, vale, por favor.")] // words added
    public void Any_other_change_is_put_back(string original, string refined)
    {
        var kept = Checked(original, refined, out var through, out var back);

        Assert.Equal(Line(original), kept);
        Assert.Equal(0, through);
        Assert.True(back >= 1);
    }

    [Fact]
    public void A_respelling_next_to_another_change_is_kept_alone()
    {
        var decisions = new List<RespellingGuard.Decision>();

        var kept = RespellingGuard.ApplyToLine(Line("Вале, una casa."), Line("Vale, un casa."), Known, out var through, out var back, decisions: decisions);

        Assert.Equal(Line("Vale, una casa."), kept);
        Assert.Equal(1, through);
        Assert.Equal(1, back);
        Assert.Equal(new[] { ("Вале,", "Vale,", true, "respelled"), ("una", "un", false, "not a respelling") },
            decisions.Select(d => (d.Was, d.Now, d.Kept, d.Why)));
    }

    [Fact]
    public void The_time_and_the_speaker_are_never_taken_from_the_model()
    {
        var original = Line("Sí, вале.", time: "12.50", speaker: "Olga");
        var refined = Line("Sí, vale.", time: "13.00", speaker: "speaker_1");

        var kept = RespellingGuard.ApplyToLine(original, refined, Known, out var through, out _);

        Assert.Equal(Line("Sí, vale.", time: "12.50", speaker: "Olga"), kept);
        Assert.Equal(1, through);
    }

    [Fact]
    public void A_line_whose_form_the_model_broke_is_put_back()
    {
        var original = Line("Sí, вале.");

        var kept = RespellingGuard.ApplyToLine(original, "Sí, vale.\n", Known, out var through, out var back);

        Assert.Equal(original, kept);
        Assert.Equal(0, through);
        Assert.Equal(1, back);
    }

    [Fact]
    public void A_line_the_model_left_alone_comes_back_byte_for_byte()
    {
        var original = "- 12.50 speaker_0:  \"Да,  вале.\"  \n"; // odd spacing stays odd
        Assert.Equal(original, RespellingGuard.ApplyToLine(original, Line("Да, вале."), Known, out var through, out var back));
        Assert.Equal(0, through);
        Assert.Equal(0, back);
    }

    [Fact]
    public void A_plain_text_line_is_checked_whole()
    {
        var kept = RespellingGuard.ApplyToLine("Sí, вале, пошли.\n", "Sí, vale, идём.\n", Known, out var through, out var back);

        Assert.Equal("Sí, vale, пошли.\n", kept);
        Assert.Equal(1, through);
        Assert.Equal(1, back);
    }

    // ---------- a batch ----------

    [Fact]
    public void A_batch_with_another_number_of_lines_is_put_back_whole()
    {
        var original = new[] { Line("Sí, вале."), Line("Пошли.") };
        var refined = new[] { Line("Sí, vale. Пошли.") };

        var outcome = RespellingGuard.Apply(original, refined, Known);

        Assert.Equal(original, outcome.Lines);
        Assert.Equal(0, outcome.LetThrough);
        Assert.Equal(2, outcome.PutBack);
    }

    [Fact]
    public void Empty_lines_of_a_batch_stay_where_they_were()
    {
        var original = new[] { Line("Sí, вале."), "\n", Line("Это мой дом.") };
        var refined = new[] { Line("Sí, vale."), Line("Это мой casa.") }; // the client drops empty lines

        var outcome = RespellingGuard.Apply(original, refined, Known);

        Assert.Equal(new[] { Line("Sí, vale."), "\n", Line("Это мой дом.") }, outcome.Lines);
        Assert.Equal(1, outcome.LetThrough);
        Assert.Equal(1, outcome.PutBack);
    }

    // ---------- the Spanish of a transcript ----------

    [Fact]
    public void The_Spanish_of_a_transcript_is_its_Latin_words_without_accents_and_not_the_speakers()
    {
        var words = RespellingGuard.LatinWordsOf(new[]
        {
            "- 1.00 Maria: \"¿Cómo estás, Olga? Теngo время.\"\n",
            "plain line: Mañana\n",
        });

        Assert.Contains("como", words);
        Assert.Contains("estas", words);
        Assert.Contains("olga", words);
        Assert.Contains("manana", words);
        Assert.DoesNotContain("maria", words); // a speaker label is not speech
        Assert.DoesNotContain("tengo", words); // a word of both alphabets is not Spanish yet
    }
}
