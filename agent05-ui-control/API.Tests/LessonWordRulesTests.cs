using XtractManager.Features.LessonPackages.Application;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// How a word of a lesson is looked for in the earlier lessons. Every line and word here is made up for
/// the test.
/// </summary>
public sealed class LessonWordRulesTests
{
    private static LessonWords Lesson(string date, params string[] lines) => new(date, lines);

    [Fact]
    public void Plain_is_lower_case_without_accents()
    {
        Assert.Equal("esta manana", LessonWordRules.Plain("Está MAÑANA"));
        Assert.Equal("", LessonWordRules.Plain(null));
    }

    [Fact]
    public void LatinWords_leaves_out_what_is_not_in_Latin_letters()
    {
        Assert.Equal(
            new[] { "la", "ventana", "esta" },
            LessonWordRules.LatinWords("Так, la ventana — 2 раза, ESTÁ."));
        Assert.Empty(LessonWordRules.LatinWords(null));
    }

    [Fact]
    public void Slug_is_the_word_in_plain_letters_with_dashes()
    {
        Assert.Equal("la-ventana", LessonWordRules.Slug("La  Ventána!"));
        Assert.Equal("x", LessonWordRules.Slug("окно"));
        Assert.Equal(120, LessonWordRules.Slug(new string('a', 300)).Length);
    }

    [Fact]
    public void A_form_of_the_same_word_counts_as_known()
    {
        var lesson = Lesson("2026-01-10", "Nosotros trabajamos mucho en las mesas.");

        Assert.True(LessonWordRules.KnownForm("trabajamos", lesson));
        Assert.True(LessonWordRules.KnownForm("trabajo", lesson));
        Assert.False(LessonWordRules.KnownForm("traducir", lesson));
        // A word of four letters is known only as it is written.
        Assert.False(LessonWordRules.KnownForm("mesa", lesson));
        Assert.True(LessonWordRules.KnownForm("mesas", lesson));
    }

    [Fact]
    public void SeenBefore_names_the_lessons_that_have_a_form_of_the_word_in_the_order_given()
    {
        var earlier = new[]
        {
            Lesson("2026-01-10", "Abre las ventanitas, por favor."),
            Lesson("2026-01-17", "Hoy hablamos del tejado."),
            Lesson("2026-01-24", "La ventana está cerrada."),
        };

        Assert.Equal(new[] { "2026-01-10", "2026-01-24" }, LessonWordRules.SeenBefore("ventana", null, earlier));
        Assert.Equal(new[] { "2026-01-17" }, LessonWordRules.SeenBefore("el tejado", null, earlier));
        Assert.Empty(LessonWordRules.SeenBefore("chimenea", null, earlier)!);
    }

    [Fact]
    public void SeenBefore_finds_a_verb_by_its_stem()
    {
        var earlier = new[] { Lesson("2026-01-10", "Me quedo aquí.") };

        Assert.Equal(new[] { "2026-01-10" }, LessonWordRules.SeenBefore("quedarse", null, earlier));
    }

    [Fact]
    public void SeenBefore_counts_the_form_said_in_the_quote_too()
    {
        var earlier = new[] { Lesson("2026-01-10", "Ellos leerán mañana.") };

        Assert.Empty(LessonWordRules.SeenBefore("leer", null, earlier)!);
        Assert.Equal(new[] { "2026-01-10" }, LessonWordRules.SeenBefore("leer", "Ella leerá mucho.", earlier));
    }

    [Fact]
    public void SeenBefore_looks_for_the_first_word_of_a_pattern()
    {
        var earlier = new[] { Lesson("2026-01-10", "Acabamos tarde.") };

        Assert.Equal(new[] { "2026-01-10" }, LessonWordRules.SeenBefore("acabar + por + comer", null, earlier));
    }

    [Fact]
    public void SeenBefore_finds_a_phrase_only_inside_one_line()
    {
        var oneLine = Lesson("2026-01-10", "No quiero tener ganas hoy.");
        var twoLines = Lesson("2026-01-17", "No quiero tener", "ganas de nada.");

        Assert.Equal(
            new[] { "2026-01-10" },
            LessonWordRules.SeenBefore("tener ganas (de algo)", null, new[] { oneLine, twoLines }));
    }

    [Fact]
    public void SeenBefore_of_a_pattern_with_no_word_of_its_own_is_null()
    {
        var earlier = new[] { Lesson("2026-01-10", "Se lo digo.") };

        Assert.Null(LessonWordRules.SeenBefore("se + lo", null, earlier));
        Assert.Null(LessonWordRules.SeenBefore("(algo)", null, earlier));
        Assert.Null(LessonWordRules.SeenBefore(null, null, earlier));
    }

    [Fact]
    public void KnownIn_names_the_lessons_that_have_the_form()
    {
        var earlier = new[]
        {
            Lesson("2026-01-10", "Abre las ventanitas."),
            Lesson("2026-01-17", "Hoy hablamos del tejado."),
        };

        Assert.Equal(new[] { "2026-01-10" }, LessonWordRules.KnownIn("Ventana", earlier));
        Assert.Empty(LessonWordRules.KnownIn("chimenea", earlier));
        Assert.Empty(LessonWordRules.KnownIn("", earlier));
    }
}
