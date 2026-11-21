using System.Text.Json;
using XtractManager.Features.LessonPackages.Application;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// A lesson package as it is shown: the ids of its items, the owner's edits put on the builder's text and
/// the words counted against the earlier lessons. Every text here is made up for the test.
/// </summary>
public sealed class LessonPackageViewBuilderTests
{
    private const string Day = "2026-03-10";
    private const string At = "2026-03-11T08:00:00Z";

    private static LessonPackageDocument Package() => new()
    {
        Lesson = Day,
        Periods =
        {
            new LessonPackagePeriod { N = 1, Job = "jobdialogue1" },
            new LessonPackagePeriod { N = 2, Job = "jobdialogue2" },
        },
        Topics = { new LessonTopic { Period = 1, Title = "Weather", Summary = "Talk about the weather", FromLine = 3 } },
        Grammar =
        {
            new LessonGrammar
            {
                Topic = "Past tense",
                Rule = "Used for finished actions",
                Period = 1,
                Examples = { new LessonPlace { Period = 1, Quote = "Ayer comí." } },
                Parts =
                {
                    new LessonGrammarPart
                    {
                        Topic = "Endings",
                        Rule = "-é, -aste",
                        Examples = { new LessonPlace { Period = 1, Quote = "Hablé." } },
                    },
                },
            },
        },
        WordNotes = { new LessonGrammar { Topic = "ventana", Rule = "A window", Period = 2 } },
        Examples =
        {
            new LessonExample { Period = 1, Line = 7, Quote = "Me gusta la ventana.", Ru = "Мне нравится окно.", Shows = "gustar" },
        },
        NewWords =
        {
            new LessonWord { Period = 1, Es = "ventana", Ru = "окно", Quote = "la ventana" },
            new LessonWord { Period = 1, Es = "tejado", Ru = "крыша", Quote = "el tejado" },
        },
        Vocabulary = { new LessonWord { Period = 2, Es = "ventana", Ru = "окно" } },
        OtherWords = { new LessonOtherWord { Word = "chimenea", Raw = "Chimenea", Count = 2, Period = 1, Kind = "spanish" } },
    };

    private static LessonWords Lesson(string date, params string[] lines) => new(date, lines);

    private static IReadOnlyDictionary<string, LessonMark> Marks(params (string Id, LessonMark Mark)[] marks) =>
        marks.ToDictionary(m => m.Id, m => m.Mark, StringComparer.Ordinal);

    private static LessonMark Fields(params (string Field, string Text)[] fields) => new()
    {
        Fields = fields.ToDictionary(f => f.Field, f => f.Text, StringComparer.Ordinal),
        At = At,
    };

    [Fact]
    public void Items_gives_every_item_an_id_made_of_its_own_text()
    {
        var package = Package();

        var items = LessonPackageViewBuilder.Items(package);

        Assert.Equal("n-ventana", package.NewWords[0].Id);
        Assert.Equal("n-tejado", package.NewWords[1].Id);
        Assert.Equal("v-ventana", package.Vocabulary[0].Id);
        Assert.Equal("o-chimenea", package.OtherWords[0].Id);
        Assert.Matches("^t-[0-9a-f]{10}$", package.Topics[0].Id!);
        Assert.Matches("^g-[0-9a-f]{10}$", package.Grammar[0].Id!);
        Assert.Matches("^w-[0-9a-f]{10}$", package.WordNotes[0].Id!);
        Assert.Matches("^e-[0-9a-f]{10}$", package.Examples[0].Id!);
        Assert.Matches($"^{package.Grammar[0].Id}-e-[0-9a-f]{{10}}$", package.Grammar[0].Examples[0].Id!);
        Assert.Matches($"^{package.Grammar[0].Id}-p-[0-9a-f]{{10}}$", package.Grammar[0].Parts[0].Examples[0].Id!);
        Assert.Equal(10, items.Count);
        Assert.Equal(new[] { "es", "ru", "note", "quote" }, items["n-ventana"].Fields);
        Assert.Equal(new[] { "raw", "ru", "lemma", "said" }, items["o-chimenea"].Fields);
        Assert.Equal(new[] { "quote", "ru", "shows" }, items[package.Examples[0].Id!].Fields);
        Assert.Equal(new[] { "topic", "rule" }, items[package.Grammar[0].Id!].Fields);
        Assert.Equal(new[] { "title", "summary" }, items[package.Topics[0].Id!].Fields);
        Assert.Equal(new[] { "quote" }, items[package.Grammar[0].Examples[0].Id!].Fields);
    }

    [Fact]
    public void An_id_does_not_depend_on_the_order_of_the_list()
    {
        var first = Package();
        var turned = Package();
        turned.NewWords.Reverse();
        turned.Grammar.Insert(0, new LessonGrammar { Topic = "Articles", Rule = "el, la", Period = 2 });

        LessonPackageViewBuilder.Items(first);
        LessonPackageViewBuilder.Items(turned);

        Assert.Equal("n-tejado", turned.NewWords[0].Id);
        Assert.Equal(first.Grammar[0].Id, turned.Grammar[1].Id);
        Assert.Equal(first.Grammar[0].Examples[0].Id, turned.Grammar[1].Examples[0].Id);
    }

    [Fact]
    public void An_example_of_a_theme_keeps_its_id_when_the_examples_come_in_another_order()
    {
        LessonGrammar Theme(params string[] quotes)
        {
            var theme = new LessonGrammar { Topic = "Past tense", Rule = "Used for finished actions", Period = 1 };
            theme.Examples.AddRange(quotes.Select(q => new LessonPlace { Period = 1, Quote = q }));
            theme.Parts.Add(new LessonGrammarPart { Topic = "Endings", Rule = "-é, -aste" });
            theme.Parts.Add(new LessonGrammarPart { Topic = "Stems", Rule = "tuv-, estuv-" });
            theme.Parts[0].Examples.AddRange(quotes.Select(q => new LessonPlace { Period = 1, Quote = q }));
            theme.Parts[1].Examples.Add(new LessonPlace { Period = 1, Quote = "Ayer comí." });
            return theme;
        }

        var first = new LessonPackageDocument { Lesson = Day, Grammar = { Theme("Ayer comí.", "Hablé mucho.") } };
        var turned = new LessonPackageDocument { Lesson = Day, Grammar = { Theme("Hablé mucho.", "Ayer comí.") } };
        turned.Grammar[0].Parts.Reverse();

        var items = LessonPackageViewBuilder.Items(first);
        LessonPackageViewBuilder.Items(turned);

        Assert.Equal(first.Grammar[0].Examples[0].Id, turned.Grammar[0].Examples[1].Id);
        Assert.Equal(first.Grammar[0].Examples[1].Id, turned.Grammar[0].Examples[0].Id);
        Assert.Equal(first.Grammar[0].Parts[0].Examples[0].Id, turned.Grammar[0].Parts[1].Examples[1].Id);
        Assert.Equal(first.Grammar[0].Parts[1].Examples[0].Id, turned.Grammar[0].Parts[0].Examples[0].Id);
        // The same phrase under the theme, under one part and under another are three items.
        Assert.Equal(6, items.Count);
        Assert.Equal(
            3,
            new[] { first.Grammar[0].Examples[0].Id, first.Grammar[0].Parts[0].Examples[0].Id, first.Grammar[0].Parts[1].Examples[0].Id }
                .Distinct().Count());
    }

    [Fact]
    public void Two_words_of_one_name_are_told_apart_by_the_rest_of_their_text_in_any_order()
    {
        var package = Package();
        package.NewWords.Add(new LessonWord { Period = 2, Es = "Ventana", Ru = "окошко" });
        var turned = Package();
        turned.NewWords.Insert(0, new LessonWord { Period = 2, Es = "Ventana", Ru = "окошко" });

        var items = LessonPackageViewBuilder.Items(package);
        LessonPackageViewBuilder.Items(turned);

        Assert.Matches("^n-ventana-[0-9a-f]{6}$", package.NewWords[0].Id!);
        Assert.Matches("^n-ventana-[0-9a-f]{6}$", package.NewWords[2].Id!);
        Assert.NotEqual(package.NewWords[0].Id, package.NewWords[2].Id);
        Assert.False(items.ContainsKey("n-ventana"));
        Assert.Equal(package.NewWords[0].Id, turned.NewWords[1].Id);
        Assert.Equal(package.NewWords[2].Id, turned.NewWords[0].Id);
        // A word met once keeps the plain id.
        Assert.Equal("n-tejado", package.NewWords[1].Id);
        Assert.Equal("v-ventana", package.Vocabulary[0].Id);
    }

    [Fact]
    public void Only_items_the_same_in_everything_get_a_numbered_id()
    {
        var package = Package();
        package.NewWords.Add(new LessonWord { Period = 1, Es = "ventana", Ru = "окно", Quote = "la ventana" });

        var items = LessonPackageViewBuilder.Items(package);

        Assert.Matches("^n-ventana-[0-9a-f]{6}$", package.NewWords[0].Id!);
        Assert.Equal(package.NewWords[0].Id + "-2", package.NewWords[2].Id);
        Assert.Equal(11, items.Count);
    }

    [Fact]
    public void Build_puts_the_edits_on_and_remembers_what_the_builder_had()
    {
        var package = Package();
        LessonPackageViewBuilder.Items(package);
        var exampleId = package.Grammar[0].Examples[0].Id!;

        var view = LessonPackageViewBuilder.Build(
            Day,
            package,
            Marks(("n-ventana", Fields(("ru", "окошко"))), (exampleId, Fields(("quote", "Ayer comí pan.")))),
            Array.Empty<LessonWords>());

        var word = view.Package.NewWords[0];
        Assert.Equal("окошко", word.Ru);
        Assert.Equal("окно", Assert.Single(word.Edited!).Value);
        Assert.Equal("ru", word.Edited!.Keys.Single());
        Assert.Equal(At, word.EditedAt);
        Assert.Equal("Ayer comí pan.", view.Package.Grammar[0].Examples[0].Quote);
        Assert.Equal("Ayer comí.", view.Package.Grammar[0].Examples[0].Edited!["quote"]);
        Assert.Null(view.Package.NewWords[1].Edited);
        Assert.Equal(2, view.Counts.Edited);
        Assert.Equal(0, view.Counts.Stale);
    }

    [Fact]
    public void An_edit_that_repeats_the_builders_text_is_not_an_edit()
    {
        var view = LessonPackageViewBuilder.Build(
            Day, Package(), Marks(("n-ventana", Fields(("ru", "окно"), ("colour", "red")))), Array.Empty<LessonWords>());

        Assert.Null(view.Package.NewWords[0].Edited);
        Assert.Equal(0, view.Counts.Edited);
    }

    [Fact]
    public void An_edit_of_an_item_the_package_no_longer_has_is_counted_as_stale()
    {
        var view = LessonPackageViewBuilder.Build(
            Day, Package(), Marks(("n-balcon", Fields(("ru", "балкон")))), Array.Empty<LessonWords>());

        Assert.Equal(1, view.Counts.Stale);
        Assert.Equal(0, view.Counts.Edited);
    }

    private static IReadOnlyDictionary<int, LessonHearing> HeardInPeriodOne() => new Dictionary<int, LessonHearing>
    {
        [1] = new(
            new[]
            {
                new XtractManager.Features.Jobs.Application.HeardLine(5, 8, "Abre la ventana.", "абре ла вентана", null, null),
                new XtractManager.Features.Jobs.Application.HeardLine(20, 23, "Mira el tejado.", "смотри на крышу", null, null),
            },
            Array.Empty<XtractManager.Features.Jobs.Application.TimedLine>()),
    };

    [Fact]
    public void Build_tells_which_words_another_hearing_has()
    {
        var view = LessonPackageViewBuilder.Build(Day, Package(), Marks(), Array.Empty<LessonWords>(), hearingsByPeriod: HeardInPeriodOne());

        Assert.True(view.Package.NewWords[0].HeardElsewhere);
        Assert.False(view.Package.NewWords[1].HeardElsewhere);
        Assert.Null(view.Package.Vocabulary[0].HeardElsewhere); // period 2 has no hearing
        Assert.Null(view.Package.OtherWords[0].HeardElsewhere); // no line to find it in
        Assert.Equal(1, view.Counts.NotHeardElsewhere);
    }

    [Fact]
    public void A_word_the_owner_wrote_anew_or_took_out_is_not_counted_as_unheard()
    {
        var edited = LessonPackageViewBuilder.Build(
            Day, Package(), Marks(("n-tejado", Fields(("es", "tejado rojo")))), Array.Empty<LessonWords>(), hearingsByPeriod: HeardInPeriodOne());
        var takenOut = LessonPackageViewBuilder.Build(
            Day, Package(), Marks(("n-tejado", new LessonMark { LeftOut = true, At = At })), Array.Empty<LessonWords>(), hearingsByPeriod: HeardInPeriodOne());
        var unknown = LessonPackageViewBuilder.Build(Day, Package(), Marks(), Array.Empty<LessonWords>());

        Assert.Null(edited.Package.NewWords[1].HeardElsewhere);
        Assert.Equal(0, edited.Counts.NotHeardElsewhere);
        Assert.False(takenOut.Package.NewWords[1].HeardElsewhere);
        Assert.Equal(0, takenOut.Counts.NotHeardElsewhere);
        Assert.All(unknown.Package.NewWords, w => Assert.Null(w.HeardElsewhere));
    }

    [Fact]
    public void A_word_taken_out_is_marked_and_not_counted()
    {
        var view = LessonPackageViewBuilder.Build(
            Day, Package(), Marks(("n-tejado", new LessonMark { LeftOut = true, At = At })), Array.Empty<LessonWords>());

        Assert.True(view.Package.NewWords[1].LeftOut);
        Assert.Equal(1, view.Counts.NewWords);
        Assert.Equal(1, view.Counts.NewWordsNeverSeen);
        Assert.Equal(1, view.Counts.LeftOut);
    }

    [Fact]
    public void Words_are_compared_only_with_the_lessons_before_this_one()
    {
        var lessons = new[]
        {
            Lesson("2026-01-10", "Abre las ventanitas."),
            Lesson("2026-02-14", "Una chimenea grande."),
            Lesson(Day, "El tejado y la ventana."),
            Lesson("2026-05-01", "El tejado es rojo."),
        };

        var view = LessonPackageViewBuilder.Build(Day, Package(), Marks(), lessons);

        Assert.Equal(new[] { "2026-01-10" }, view.Package.NewWords[0].SeenIn);
        Assert.Empty(view.Package.NewWords[1].SeenIn!);
        Assert.Equal(new[] { "2026-01-10" }, view.Package.Vocabulary[0].SeenIn);
        Assert.Equal(new[] { "2026-02-14" }, view.Package.OtherWords[0].SeenIn);
        Assert.Equal(2, view.Counts.NewWords);
        Assert.Equal(1, view.Counts.NewWordsNeverSeen);
        Assert.Equal(0, view.Counts.VocabularyNeverSeen);
        Assert.Equal(0, view.Counts.OtherWordsNeverSeen);
        Assert.Equal(2, view.Compared.Lessons);
        Assert.Equal("2026-01-10", view.Compared.From);
        Assert.Equal("2026-02-14", view.Compared.To);
        Assert.Equal(new[] { "2026-01-10", "2026-02-14" }, view.Compared.Dates);
    }

    [Fact]
    public void A_word_written_anew_is_compared_as_it_is_written_now()
    {
        var lessons = new[] { Lesson("2026-01-10", "Un ventanal y una estufa.") };

        var view = LessonPackageViewBuilder.Build(
            Day,
            Package(),
            Marks(("n-tejado", Fields(("es", "ventanal"))), ("o-chimenea", Fields(("raw", "estufa")))),
            lessons);

        Assert.Equal("ventanal", view.Package.NewWords[1].Es);
        Assert.Equal(new[] { "2026-01-10" }, view.Package.NewWords[1].SeenIn);
        Assert.Equal(new[] { "2026-01-10" }, view.Package.OtherWords[0].SeenIn);
    }

    [Fact]
    public void A_period_gets_the_mixed_track_of_the_recording_its_job_belongs_to()
    {
        var view = LessonPackageViewBuilder.Build(
            Day,
            Package(),
            Marks(),
            Array.Empty<LessonWords>(),
            new Dictionary<string, string> { ["jobdialogue2"] = "track-two" });

        Assert.Null(view.Package.Periods[0].AudioTrack);
        Assert.Equal("track-two", view.Package.Periods[1].AudioTrack);
    }

    private static LessonItemAccess Word(string id = "n-ventana") => LessonPackageViewBuilder.Items(Package())[id];

    private static Dictionary<string, string?> Change(params (string Field, string? Text)[] fields) =>
        fields.ToDictionary(f => f.Field, f => f.Text, StringComparer.Ordinal);

    [Fact]
    public void Changed_keeps_the_new_text_and_what_the_builder_had()
    {
        var mark = LessonPackageViewBuilder.Changed(Word(), null, Change(("ru", "  окошко \n малое ")), null, At);

        Assert.NotNull(mark);
        Assert.Equal("окошко малое", mark!.Fields!["ru"]);
        Assert.Equal("окно", mark.Was!["ru"]);
        Assert.False(mark.LeftOut);
        Assert.Equal("new_words: ventana", mark.Of);
        Assert.Equal(At, mark.At);
    }

    [Fact]
    public void Changed_takes_a_field_back_when_given_null_or_the_builders_own_text()
    {
        var known = LessonPackageViewBuilder.Changed(Word(), null, Change(("ru", "окошко"), ("note", "small")), null, At);

        var oneLeft = LessonPackageViewBuilder.Changed(Word(), known, Change(("ru", null)), null, At);
        var noneLeft = LessonPackageViewBuilder.Changed(Word(), oneLeft, Change(("note", "")), null, At);
        var sameText = LessonPackageViewBuilder.Changed(Word(), null, Change(("ru", " окно ")), null, At);

        Assert.Equal(new[] { "note" }, oneLeft!.Fields!.Keys);
        Assert.Equal(new[] { "note" }, oneLeft.Was!.Keys);
        Assert.Null(noneLeft);
        Assert.Null(sameText);
    }

    [Fact]
    public void Changed_keeps_the_fields_that_were_not_named()
    {
        var known = LessonPackageViewBuilder.Changed(Word(), null, Change(("ru", "окошко")), null, At);

        var both = LessonPackageViewBuilder.Changed(Word(), known, Change(("quote", "esa ventana")), null, At);

        Assert.Equal("окошко", both!.Fields!["ru"]);
        Assert.Equal("esa ventana", both.Fields["quote"]);
        Assert.Equal("la ventana", both.Was!["quote"]);
    }

    [Fact]
    public void Changed_lets_a_translation_be_emptied_but_not_the_name_of_the_item()
    {
        var emptied = LessonPackageViewBuilder.Changed(Word(), null, Change(("ru", "  ")), null, At);

        Assert.Equal("", emptied!.Fields!["ru"]);
        Assert.Throws<ArgumentException>(() => LessonPackageViewBuilder.Changed(Word(), null, Change(("es", " ")), null, At));
    }

    [Fact]
    public void Changed_refuses_a_field_the_item_does_not_have_and_a_text_too_long()
    {
        Assert.Throws<ArgumentException>(() =>
            LessonPackageViewBuilder.Changed(Word(), null, Change(("shows", "x")), null, At));
        Assert.Throws<ArgumentException>(() =>
            LessonPackageViewBuilder.Changed(
                Word(), null, Change(("ru", new string('я', LessonPackageViewBuilder.MaxFieldLength + 1))), null, At));
    }

    [Fact]
    public void Changed_takes_an_item_out_and_puts_it_back()
    {
        var out_ = LessonPackageViewBuilder.Changed(Word(), null, null, true, At);
        var stillOut = LessonPackageViewBuilder.Changed(Word(), out_, Change(("ru", "окошко")), null, At);
        var back = LessonPackageViewBuilder.Changed(Word(), out_, null, false, At);

        Assert.True(out_!.LeftOut);
        Assert.Null(out_.Fields);
        Assert.True(stillOut!.LeftOut);
        Assert.Equal("окошко", stillOut.Fields!["ru"]);
        Assert.Null(back);
    }

    [Fact]
    public void Changed_keeps_what_a_hand_added_to_the_edit()
    {
        var known = new LessonMark
        {
            LeftOut = true,
            Other = new Dictionary<string, JsonElement> { ["why"] = JsonDocument.Parse("\"heard wrong\"").RootElement.Clone() },
        };

        var mark = LessonPackageViewBuilder.Changed(Word(), known, Change(("ru", "окошко")), null, At);

        Assert.Equal("heard wrong", mark!.Other!["why"].GetString());
    }

    [Fact]
    public void Changed_leaves_what_a_hand_added_when_the_last_edit_is_taken_back()
    {
        var known = new LessonMark
        {
            Fields = new Dictionary<string, string> { ["ru"] = "окошко" },
            Was = new Dictionary<string, string?> { ["ru"] = "окно" },
            At = At,
            Other = new Dictionary<string, JsonElement> { ["why"] = JsonDocument.Parse("\"heard wrong\"").RootElement.Clone() },
        };

        var left = LessonPackageViewBuilder.Changed(Word(), known, Change(("ru", null)), null, At);

        Assert.NotNull(left);
        Assert.False(left!.HasEdit);
        Assert.False(left.IsEmpty);
        Assert.Null(left.Fields);
        Assert.Null(left.At);
        Assert.Equal("heard wrong", left.Other!["why"].GetString());
        Assert.Equal("new_words: ventana", left.Of);
    }

    [Theory]
    [InlineData("n-ventana", true)]
    [InlineData("g-0a1b2c3d4e-p1e2", true)]
    [InlineData("N-ventana", false)]
    [InlineData("n ventana", false)]
    [InlineData("../marks", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryMarkId_takes_small_Latin_letters_digits_and_dashes(string? raw, bool expected)
    {
        Assert.Equal(expected, LessonPackageViewBuilder.TryMarkId(raw, out _));
        Assert.False(LessonPackageViewBuilder.TryMarkId(new string('a', LessonPackageViewBuilder.MaxIdLength + 1), out _));
    }
}
