using XtractManager.Features.Jobs.Application;
using XtractManager.Features.LessonPackages.Application;
using XtractManager.Features.Sessions.Application;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// Whether a word of a package was heard by another hearing of the same speech: the piece of its line heard
/// alone, the piece asked again with the audio around it, the diarize model's run. Every text here is made up.
/// </summary>
public sealed class HeardElsewhereTests
{
    private static HeardLine Line(double start, string text, string? own = null, string? context = null, string? was = null) =>
        new(start, start + 3, text, own, context, was);

    private static LessonHearing Hearing(params HeardLine[] lines) => new(lines, Array.Empty<TimedLine>());

    [Fact]
    public void A_word_the_piece_heard_alone_in_Cyrillic_letters_was_heard()
    {
        var hearing = Hearing(Line(10, "Abre la ventana, por favor.", own: "абре ла вентана пор фавор"));

        Assert.True(HeardElsewhere.Check(hearing, "ventana", 11, "Abre la ventana, por favor."));
    }

    [Fact]
    public void A_word_of_a_line_the_piece_heard_in_Russian_was_not_heard()
    {
        var hearing = Hearing(Line(10, "Necesito comprar pan mañana.", own: "мне нужно купить хлеб завтра"));

        Assert.False(HeardElsewhere.Check(hearing, "comprar", 11, "Necesito comprar pan mañana."));
    }

    [Fact]
    public void The_other_model_near_the_line_confirms_a_word_the_piece_missed()
    {
        var line = Line(100, "Necesito comprar pan mañana.", own: "мне нужно купить хлеб завтра");
        var near = new LessonHearing(new[] { line }, new[] { new TimedLine(110, 113, "несесито компрар пан") });
        var far = new LessonHearing(new[] { line }, new[] { new TimedLine(160, 163, "несесито компрар пан") });

        Assert.True(HeardElsewhere.Check(near, "comprar", 101, "Necesito comprar pan mañana."));
        Assert.False(HeardElsewhere.Check(far, "comprar", 101, "Necesito comprar pan mañana."));
    }

    [Fact]
    public void A_lemma_of_the_list_is_looked_for_in_the_form_the_line_has()
    {
        var hearing = Hearing(Line(10, "Hablamos mucho hoy.", own: "абламос мучо ой"));

        Assert.True(HeardElsewhere.Check(hearing, "hablar", 10, "Hablamos mucho hoy."));
    }

    [Fact]
    public void A_line_replaced_after_a_second_look_is_found_by_the_text_it_had()
    {
        var hearing = Hearing(Line(
            10, "мне нужно купить хлеб", own: "мне нужно купить хлеб", context: "да мне нужно купить хлеб завтра", was: "Necesito comprar pan."));

        Assert.False(HeardElsewhere.Check(hearing, "comprar", 10, "Necesito comprar pan."));
    }

    [Fact]
    public void Nothing_is_told_without_a_word_to_check_a_line_or_another_hearing()
    {
        var hearing = Hearing(
            Line(10, "Abre la ventana, por favor.", own: "абре ла вентана пор фавор"),
            Line(30, "Cierra la puerta.")); // no piece

        Assert.Null(HeardElsewhere.Check(hearing, "sol", 11, "Abre la ventana, por favor.")); // shorter than four letters
        Assert.Null(HeardElsewhere.Check(hearing, "ventana", 11, "Una frase que no está aquí."));
        Assert.Null(HeardElsewhere.Check(hearing, "puerta", 31, "Cierra la puerta."));
        Assert.Null(HeardElsewhere.Check(hearing, "tejado", 11, "Abre la ventana, por favor.")); // the line has no form of it
    }

    [Fact]
    public void The_quote_is_tried_when_the_line_of_the_package_is_not_found()
    {
        var hearing = Hearing(Line(10, "Abre la ventana, por favor.", own: "абре ла вентана пор фавор"));

        Assert.True(HeardElsewhere.Check(hearing, "ventana", 11, "Otra cosa dicha antes. Y otra más larga aún.", "la ventana"));
    }

    [Fact]
    public void A_line_is_looked_for_near_the_time_of_the_word()
    {
        var hearing = Hearing(
            Line(10, "Abre la ventana.", own: "мне нужно окно"),
            Line(200, "Abre la ventana.", own: "абре ла вентана"));

        Assert.False(HeardElsewhere.Check(hearing, "ventana", 11, "Abre la ventana."));
        Assert.True(HeardElsewhere.Check(hearing, "ventana", 201, "Abre la ventana."));
    }

    // ---------- which jobs a recording is heard from ----------

    private static TrackRun Run(string job, string model, string createdAt) =>
        new(job, "done", "completed", createdAt, true, false) { Settings = new JobRunSettings("run", model, null, null, Array.Empty<string>()) };

    [Fact]
    public void A_recording_with_a_dialogue_is_heard_from_its_tracks_and_the_diarize_runs()
    {
        var mixed = new RecordingTrack("d/p/audio1.m4a", "mixed", null, "audio1.m4a", 10, 60)
        {
            Runs = new[] { Run("mixedOld", "gpt-4o-transcribe-diarize", "2026-01-01T10:00:00Z") },
        };
        var teacher = new RecordingTrack("d/p/Audio Record/audioA1.m4a", "participant", "A", "audioA1.m4a", 10, 60)
        {
            Runs = new[] { Run("teacherNew", "gpt-transcribe", "2026-01-02T10:00:00Z") },
        };
        var student = new RecordingTrack("d/p/Audio Record/audioB1.m4a", "participant", "B", "audioB1.m4a", 10, 60)
        {
            Runs = new[]
            {
                Run("studentOld", "gpt-4o-transcribe-diarize", "2026-01-01T10:00:00Z"),
                Run("studentNew", "gpt-transcribe", "2026-01-02T10:00:00Z"),
            },
        };
        var period = new RecordingPeriod("d/p", null, null, "ready", false, false, false, false, 0, new[] { mixed, teacher, student })
        {
            Dialogues = new[] { new PeriodDialogue("dialogue1", null, "2026-01-03T10:00:00Z", new[] { "teacherNew", "studentNew" }, 1) },
        };

        var source = LessonHearingSource.Of(period);

        Assert.Equal(new[] { "teacherNew", "studentNew" }, source.TextJobs);
        // the mixed file's own run, the teacher's stand-in (the same) and the student's own one
        Assert.Equal(new[] { "mixedOld", "studentOld" }, source.OtherJobs);
    }
}
