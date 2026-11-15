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
    public void A_Spanish_word_sounds_the_same_from_either_alphabet(string cyrillic, string latin)
    {
        Assert.Equal(RespellingGuard.SoundForm(latin), RespellingGuard.SoundForm(cyrillic));
    }

    [Theory]
    [InlineData("дом", "casa")] // a translation
    [InlineData("пуэдо", "puede")] // another ending
    [InlineData("жара", "shara")] // ж is not the sound of sh
    public void Another_word_does_not_sound_alike(string was, string now)
    {
        Assert.True(RespellingGuard.Similarity(RespellingGuard.SoundForm(was), RespellingGuard.SoundForm(now)) < RespellingGuard.SoundsAlikeFrom);
    }

    // ---------- what is let through ----------

    [Fact]
    public void A_Spanish_word_in_Cyrillic_is_respelled_and_the_rest_of_the_line_stays()
    {
        var kept = Checked("Да, вале, пошли домой.", "Да, vale, пошли домой.", out var through, out var back);

        Assert.Equal(Line("Да, vale, пошли домой."), kept);
        Assert.Equal(1, through);
        Assert.Equal(0, back);
    }

    [Fact]
    public void A_phrase_is_respelled_word_for_word_and_the_opening_question_mark_may_come()
    {
        Assert.Equal(Line("Ну, por favor, скажи."), Checked("Ну, пор фавор, скажи.", "Ну, por favor, скажи.", out var through, out _));
        Assert.Equal(2, through);

        Assert.Equal(Line("¿Cómo estás? Хорошо."), Checked("Комо эстас? Хорошо.", "¿Cómo estás? Хорошо.", out through, out _));
        Assert.Equal(2, through);
    }

    [Fact]
    public void A_word_of_both_alphabets_is_respelled()
    {
        Assert.Equal(Line("Теперь Tengo время."), Checked("Теперь Теngo время.", "Теперь Tengo время.", out var through, out _));
        Assert.Equal(1, through);
    }

    // ---------- what is put back ----------

    [Theory]
    [InlineData("Это мой дом.", "Это мой casa.")] // a Russian word translated
    [InlineData("Сейчас приду.", "Seichas приду.")] // a Russian word in Latin letters: not a word of the transcript
    [InlineData("Я пуэдо завтра.", "Я puede завтра.")] // the ending changed on the way
    [InlineData("Tengo una casa.", "Tengo un casa.")] // the speaker's Spanish "corrected"
    [InlineData("Он пришёл.", "On пришёл.")] // too short to judge, though "on" is a word of the transcript
    [InlineData("Да, вале.", "Да, vale!")] // the punctuation changed with the word
    [InlineData("Да, вале, пошли.", "Да, vale.")] // words dropped
    [InlineData("Да, вале.", "Да, vale, por favor.")] // words added
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
        var kept = Checked("Вале, una casa.", "Vale, un casa.", out var through, out var back);

        Assert.Equal(Line("Vale, una casa."), kept);
        Assert.Equal(1, through);
        Assert.Equal(1, back);
    }

    [Fact]
    public void The_time_and_the_speaker_are_never_taken_from_the_model()
    {
        var original = Line("Да, вале.", time: "12.50", speaker: "Olga");
        var refined = Line("Да, vale.", time: "13.00", speaker: "speaker_1");

        var kept = RespellingGuard.ApplyToLine(original, refined, Known, out var through, out _);

        Assert.Equal(Line("Да, vale.", time: "12.50", speaker: "Olga"), kept);
        Assert.Equal(1, through);
    }

    [Fact]
    public void A_line_whose_form_the_model_broke_is_put_back()
    {
        var original = Line("Да, вале.");

        var kept = RespellingGuard.ApplyToLine(original, "Да, vale.\n", Known, out var through, out var back);

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
        var kept = RespellingGuard.ApplyToLine("Да, вале, пошли.\n", "Да, vale, идём.\n", Known, out var through, out var back);

        Assert.Equal("Да, vale, пошли.\n", kept);
        Assert.Equal(1, through);
        Assert.Equal(1, back);
    }

    // ---------- a batch ----------

    [Fact]
    public void A_batch_with_another_number_of_lines_is_put_back_whole()
    {
        var original = new[] { Line("Да, вале."), Line("Пошли.") };
        var refined = new[] { Line("Да, vale. Пошли.") };

        var outcome = RespellingGuard.Apply(original, refined, Known);

        Assert.Equal(original, outcome.Lines);
        Assert.Equal(0, outcome.LetThrough);
        Assert.Equal(2, outcome.PutBack);
    }

    [Fact]
    public void Empty_lines_of_a_batch_stay_where_they_were()
    {
        var original = new[] { Line("Да, вале."), "\n", Line("Это мой дом.") };
        var refined = new[] { Line("Да, vale."), Line("Это мой casa.") }; // the client drops empty lines

        var outcome = RespellingGuard.Apply(original, refined, Known);

        Assert.Equal(new[] { Line("Да, vale."), "\n", Line("Это мой дом.") }, outcome.Lines);
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
