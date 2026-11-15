using XtractManager.Features.Jobs.Application;
using Xunit;

namespace XtractManager.Tests;

/// <summary>How the talk of a dialogue goes, counted from the lines of its tracks on one timeline.</summary>
public sealed class ConversationStatsTests
{
    private static ConversationTrackInput Track(string speaker, params (double Start, double End, string Text)[] lines) =>
        new(speaker.ToLowerInvariant(), speaker, lines.Select(l => new TimedLine(l.Start, l.End, l.Text)).ToList());

    private static TrackConversation Of(ConversationReport report, string speaker) =>
        report.Tracks.Single(t => t.Speaker == speaker);

    [Fact]
    public void One_speaker_is_no_talk()
    {
        Assert.Null(ConversationStats.Compute(new[] { Track("A", (0, 5, "Hola, buenos días.")) }));
        Assert.Null(ConversationStats.Compute(new[] { Track("A", (0, 5, "Hola, buenos días.")), Track("B") }));
        Assert.Null(ConversationStats.Compute(Array.Empty<ConversationTrackInput>()));
    }

    [Fact]
    public void A_line_begun_while_the_other_still_speaks_is_an_interruption()
    {
        var report = ConversationStats.Compute(new[]
        {
            Track("A", (0, 10, "Vamos a empezar la clase de hoy.")),
            Track("B", (5, 8, "Espera, tengo una pregunta.")),
        })!;

        Assert.Equal(1, Of(report, "B").Interruptions);
        Assert.Equal(0, Of(report, "B").Backchannels);
        Assert.Equal(0, Of(report, "A").Interruptions);
        Assert.Equal(3, report.OverlapSeconds, 6);
        Assert.Equal(3, Of(report, "A").OverlapSeconds, 6);
        Assert.Equal(3, Of(report, "B").OverlapSeconds, 6);
        Assert.Equal(10, report.SpanSeconds, 6);
        Assert.Equal(0, report.SilenceSeconds, 6);
    }

    [Theory]
    [InlineData("Mhm.", 0.6, true)]
    [InlineData("Sí, sí.", 1.2, true)]
    [InlineData("Sí, sí, claro.", 1.2, false)] // three words
    [InlineData("Vale.", 2.0, false)] // too long for an assent
    public void A_short_assent_over_the_others_speech_is_not_an_interruption(string text, double seconds, bool assent)
    {
        var report = ConversationStats.Compute(new[]
        {
            Track("A", (0, 10, "Vamos a empezar la clase de hoy.")),
            Track("B", (4, 4 + seconds, text)),
        })!;

        Assert.Equal(assent ? 1 : 0, Of(report, "B").Backchannels);
        Assert.Equal(assent ? 0 : 1, Of(report, "B").Interruptions);
    }

    [Fact]
    public void A_line_begun_just_before_the_other_stops_is_a_quick_answer()
    {
        var report = ConversationStats.Compute(new[]
        {
            Track("A", (0, 10, "¿Cómo se dice esto en español?")),
            Track("B", (9.4, 12, "Se dice la mesa, creo.")), // the two speak together for 0.6 s
        })!;

        Assert.Equal(0, Of(report, "B").Interruptions);
        Assert.Equal(0, Of(report, "B").Backchannels);
        Assert.Equal(0.0, Of(report, "B").MedianAnswerGapSeconds!.Value, 6);
        Assert.Equal(0.6, report.OverlapSeconds, 6);
    }

    [Fact]
    public void Two_lines_begun_together_interrupt_nobody()
    {
        var report = ConversationStats.Compute(new[]
        {
            Track("A", (0, 5, "Hola, buenos días."), (10.0, 15, "Entonces empezamos con los verbos.")),
            Track("B", (10.2, 12, "Yo quería preguntar una cosa.")),
        })!;

        Assert.Equal(0, Of(report, "B").Interruptions);
        Assert.Equal(0, Of(report, "A").Interruptions);
    }

    [Fact]
    public void A_line_begun_on_the_last_sound_of_the_other_is_an_answer_not_an_interruption()
    {
        var report = ConversationStats.Compute(new[]
        {
            Track("A", (0, 10.1, "¿Cómo se dice esto en español?")),
            Track("B", (10.0, 12, "Se dice la mesa, creo.")),
        })!;

        Assert.Equal(0, Of(report, "B").Interruptions);
        Assert.Equal(0.0, Of(report, "B").MedianAnswerGapSeconds!.Value, 6);
        Assert.Equal(1, report.SpeakerChanges);
    }

    [Fact]
    public void Pauses_are_told_apart_before_an_answer_and_inside_ones_own_speech()
    {
        var report = ConversationStats.Compute(new[]
        {
            Track("A", (0, 5, "¿Qué hiciste ayer por la tarde?"), (12.5, 14, "Muy bien, sigue."), (18, 20, "Otra pregunta para ti.")),
            Track("B", (9, 12, "Ayer fui al mercado."), (21, 22, "Vale, dime.")),
        })!;

        var a = Of(report, "A");
        var b = Of(report, "B");
        // B waited 4 s and 1 s before answering A
        Assert.Equal(1, b.LongPausesBeforeAnswer);
        Assert.Equal(4, b.LongestPauseBeforeAnswerSeconds, 6);
        Assert.Equal(2.5, b.MedianAnswerGapSeconds!.Value, 6);
        Assert.Equal(0, b.LongPausesInOwnSpeech);
        // A answered B after 0.5 s, then paused 4 s inside their own speech
        Assert.Equal(0, a.LongPausesBeforeAnswer);
        Assert.Equal(0.5, a.MedianAnswerGapSeconds!.Value, 6);
        Assert.Equal(1, a.LongPausesInOwnSpeech);
        Assert.Equal(4, a.LongestPauseInOwnSpeechSeconds, 6);

        Assert.Equal(3, report.SpeakerChanges); // A B A A B
        Assert.Equal(22, report.SpanSeconds, 6);
        Assert.Equal(9.5, report.SilenceSeconds, 6);
        Assert.Equal(0, report.OverlapSeconds, 6);
        Assert.Equal(ConversationStats.LongPauseSec, report.LongPauseSeconds);
    }

    [Fact]
    public void With_three_speakers_time_counts_as_overlap_once_however_many_speak()
    {
        var report = ConversationStats.Compute(new[]
        {
            Track("A", (0, 10, "Vamos a empezar la clase de hoy.")),
            Track("B", (2, 6, "Yo tengo una pregunta antes.")),
            Track("C", (4, 8, "Y yo tengo otra pregunta.")),
        })!;

        Assert.Equal(6, report.OverlapSeconds, 6); // 2..8
        Assert.Equal(6, Of(report, "A").OverlapSeconds, 6);
        Assert.Equal(4, Of(report, "B").OverlapSeconds, 6);
        Assert.Equal(4, Of(report, "C").OverlapSeconds, 6);
        Assert.Equal(1, Of(report, "B").Interruptions);
        Assert.Equal(1, Of(report, "C").Interruptions);
    }

    [Fact]
    public void A_participant_who_never_answered_has_no_usual_wait()
    {
        var report = ConversationStats.Compute(new[]
        {
            Track("A", (0, 5, "Hola, buenos días."), (6, 9, "Hoy hablamos de los verbos.")),
            Track("B", (20, 22, "Vale, de acuerdo.")),
        })!;

        Assert.Null(Of(report, "A").MedianAnswerGapSeconds);
        Assert.Equal(11, Of(report, "B").MedianAnswerGapSeconds!.Value, 6);
        Assert.Equal(1, Of(report, "B").LongPausesBeforeAnswer);
    }
}
