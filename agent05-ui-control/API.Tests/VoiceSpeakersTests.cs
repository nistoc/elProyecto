using XtractManager.Features.Jobs.Application;
using XtractManager.Features.LessonText.Application;
using Xunit;

namespace XtractManager.Tests;

/// <summary>Voices told by the local voice tool laid onto our lines and onto a diarize run. All words are made up.</summary>
public sealed class VoiceSpeakersTests
{
    private static readonly VoiceSegment[] TwoVoices =
    {
        new(10.0, 11.4, "S1"),
        new(11.5, 13.2, "S2"),
        new(20.0, 23.6, "S1"),
    };

    [Fact]
    public void A_voice_is_found_at_its_time_or_near_it_and_named_for_the_lesson_text()
    {
        Assert.Equal("voice_1", VoiceSpeakers.At(TwoVoices, 10.5));
        Assert.Equal("voice_2", VoiceSpeakers.At(TwoVoices, 13.6));
        Assert.Null(VoiceSpeakers.At(TwoVoices, 16.0));
        Assert.Equal("voice_12", VoiceSpeakers.Name("S12"));
        Assert.Equal("teacher", VoiceSpeakers.Name("teacher"));
    }

    [Fact]
    public void A_line_is_cut_where_the_voice_changes_and_a_pause_after_it_does_not_take_its_last_words()
    {
        // the line starts at 10.0, the next one at 20.0; the voices stop at 13.2
        var parts = VoiceSpeakers.Split("Hola, ¿qué tal? Bien, gracias, ¿y tú?", 10.0, 20.0, TwoVoices);

        Assert.Equal(
            new[] { ("voice_1", "Hola, ¿qué tal?"), ("voice_2", "Bien, gracias, ¿y tú?") },
            parts.Select(p => (p.Speaker!, p.Text)));
        Assert.Equal(10.0, parts[0].Start);
        Assert.InRange(parts[1].Start, 11.0, 12.0);
        Assert.All(parts, p => Assert.Equal(0, p.Matched));
    }

    [Fact]
    public void One_word_of_another_voice_is_no_turn_and_a_line_with_no_voice_near_it_has_no_speaker()
    {
        // the sixth word (at 3.3 s) falls alone into the second voice
        var voice = new VoiceSegment[] { new(0, 2.9, "S1"), new(2.9, 3.5, "S2"), new(3.5, 6.0, "S1") };
        var whole = Assert.Single(VoiceSpeakers.Split("uno dos tres cuatro cinco seis siete ocho nueve diez", 0, 6.0, voice));
        Assert.Equal("voice_1", whole.Speaker);

        var none = Assert.Single(VoiceSpeakers.Split("nadie lo oyó", 40.0, 45.0, voice));
        Assert.Null(none.Speaker);
    }

    [Fact]
    public void The_names_of_a_diarize_run_are_given_by_voice_and_a_person_named_anew_in_a_chunk_stays_one_person()
    {
        // chunk 0: A is the first voice, B the second; chunk 1: the model calls the first voice C and the second A
        var segments = new List<DiarizedSegment>
        {
            new(0, 0.0, 4.0, "A", "uno dos tres cuatro", false),
            new(0, 4.0, 6.0, "B", "cinco seis", false),
            new(1, 10.0, 14.0, "C", "siete ocho nueve diez", false),
            new(1, 14.0, 16.0, "A", "once doce", false),
        };
        var voice = new VoiceSegment[] { new(0, 4, "S1"), new(4, 6, "S2"), new(10, 14, "S1"), new(14, 16, "S2") };

        var tied = DiarizedSpeakers.TieByVoice(segments, voice);

        Assert.Equal(
            new[] { "voice_1", "voice_1", "voice_1", "voice_1", "voice_2", "voice_2", "voice_1", "voice_1", "voice_1", "voice_1", "voice_2", "voice_2" },
            tied.Words.Select(w => w.Speaker));
        Assert.Equal(new SpeakerTies(2, 0, 0, 2), tied.Ties);
    }

    [Fact]
    public void A_name_none_of_whose_words_has_a_voice_takes_the_voice_of_its_chunk()
    {
        var segments = new List<DiarizedSegment>
        {
            new(0, 0.0, 4.0, "A", "uno dos tres cuatro", false),
            new(0, 30.0, 31.0, "B", "cinco", false),
        };
        var voice = new VoiceSegment[] { new(0, 4, "S2") };

        var tied = DiarizedSpeakers.TieByVoice(segments, voice);

        Assert.All(tied.Words, w => Assert.Equal("voice_2", w.Speaker));
    }
}
