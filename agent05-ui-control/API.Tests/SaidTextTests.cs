using XtractManager.Features.LessonPackages.Application;
using Xunit;

namespace XtractManager.Tests;

public class SaidTextTests
{
    [Theory]
    [InlineData("грасиас", "gracias")]
    [InlineData("кьеро", "quiero")]
    [InlineData("трабахо", "trabajo")]
    [InlineData("сьюдад", "ciudad")]
    [InlineData("эспаньол", "español")]
    [InlineData("ой", "hoy")]
    [InlineData("вале", "vale")]
    [InlineData("мучо", "mucho")]
    public void A_word_sounds_the_same_from_either_alphabet(string cyrillic, string latin)
    {
        Assert.Equal(SaidText.SoundForm(latin), SaidText.SoundForm(cyrillic));
    }

    [Fact]
    public void A_soft_l_before_a_vowel_is_heard_as_li_and_as_ll()
    {
        Assert.Equal(SaidText.SoundForm("familia"), SaidText.SoundForm("фамилья"));
        Assert.Equal(SaidText.SoundForm("bombilla"), SaidText.SoundForm("бомбилья", softLAsLl: true));
    }

    [Fact]
    public void A_translated_word_is_not_like_the_word_it_translates()
    {
        Assert.NotEqual(SaidText.SoundForm("ventana"), SaidText.SoundForm("окно"));
        Assert.NotEqual(SaidText.SoundForm("puedo"), SaidText.SoundForm("могу"));
    }

    [Fact]
    public void The_raw_lines_are_all_kept_and_of_the_refined_ones_what_the_raw_text_has()
    {
        var raw = new[] { "Тенго ке ир.", "Я не могу, perdón.", "Бомбилья нуэва." };
        var refined = new[] { "Tengo que ir.", "Yo no puedo, perdón.", "Bombilla nueva." };

        var lines = SaidText.Lines(raw, refined);

        Assert.Equal(raw, lines.Take(3));
        Assert.Contains("Tengo que ir", lines);
        Assert.Contains("Bombilla nueva", lines);
        Assert.Contains("perdón", lines);
        Assert.DoesNotContain(lines, l => l.Contains("puedo", StringComparison.Ordinal));
    }

    [Fact]
    public void A_word_the_raw_text_does_not_have_breaks_the_line_so_no_phrase_is_found_across_it()
    {
        var raw = new[] { "Тенго мучо трабахо." };
        var refined = new[] { "Tengo siempre mucho trabajo." };

        var lines = SaidText.Lines(raw, refined);

        Assert.Contains("Tengo", lines);
        Assert.Contains("mucho trabajo", lines);
        Assert.DoesNotContain(lines, l => l.Contains("Tengo mucho", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("siempre", StringComparison.Ordinal));
    }

    [Fact]
    public void A_word_the_raw_text_has_in_latin_letters_is_kept_whatever_its_accents_and_case()
    {
        var lines = SaidText.Lines(new[] { "ESTA bien" }, new[] { "Está bien." });

        Assert.Contains("Está bien", lines);
    }

    [Fact]
    public void A_refined_line_that_is_a_raw_line_adds_nothing()
    {
        var lines = SaidText.Lines(new[] { "El tejado." }, new[] { "El tejado." });

        Assert.Equal(new[] { "El tejado." }, lines);
    }

    [Fact]
    public void With_no_raw_text_the_refined_one_is_taken_as_it_is()
    {
        var refined = new[] { "No puedo utilizar la ventana." };

        Assert.Equal(refined, SaidText.Lines(Array.Empty<string>(), refined));
    }

    [Fact]
    public void With_no_refined_text_the_raw_one_is_the_text()
    {
        var raw = new[] { "El tejado.", "Да, сейчас." };

        Assert.Equal(raw, SaidText.Lines(raw, Array.Empty<string>()));
    }
}
