using XtractManager.Features.Jobs.Application;
using Xunit;

namespace XtractManager.Tests;

public class TrackDialogueMergerTests
{
    private const string Teacher =
        ">>>>>>>\n" +
        "- 8.66 speaker_0: \"Ага,\"\n" +
        "- 17.36 speaker_0: \"entonces,\"\n" +
        "- 40.45 speaker_1: \"la cama de matrimonio\"\n" +
        "<<<<<\n";

    private const string Student =
        ">>>>>>>\n" +
        "- 0.00 speaker_0: \"Olha.\n" +
        "Hm.\n" +
        "Киноистоямы очень далеко отсюда.\"\n" +
        "- 17.36 speaker_0: \"Понял, да.\"\n" +
        "- 161.75 speaker_0: \"донде, но сейчас demasiado dinero.\"\n" +
        "<<<<<\n";

    [Fact]
    public void Parse_reads_entries_between_wrappers_and_keeps_continuation_lines()
    {
        var entries = TrackDialogueMerger.Parse(Student);

        Assert.Equal(3, entries.Count);
        Assert.Equal("0.00", entries[0].TimeText);
        Assert.Equal(0.0, entries[0].TimeSec);
        Assert.Equal("speaker_0", entries[0].Speaker);
        Assert.Equal("\"Olha.\nHm.\nКиноистоямы очень далеко отсюда.\"", entries[0].Body);
        Assert.Equal(161.75, entries[2].TimeSec);
        Assert.Equal("\"донде, но сейчас demasiado dinero.\"", entries[2].Body);
    }

    [Fact]
    public void Parse_without_wrappers_reads_the_whole_text_and_drops_lines_before_the_first_entry()
    {
        var entries = TrackDialogueMerger.Parse("# heading\r\n\r\n- 1.50 speaker_0: \"uno\"\r\n- 2.00 speaker_1: \"dos\"\r\n");

        Assert.Equal(2, entries.Count);
        Assert.Equal("\"uno\"", entries[0].Body);
        Assert.Equal("speaker_1", entries[1].Speaker);
    }

    [Fact]
    public void Parse_of_empty_text_gives_no_entries()
    {
        Assert.Empty(TrackDialogueMerger.Parse(null));
        Assert.Empty(TrackDialogueMerger.Parse(""));
        Assert.Empty(TrackDialogueMerger.Parse(">>>>>>>\n<<<<<\n"));
    }

    [Fact]
    public void Merge_puts_lines_in_time_order_under_participant_names()
    {
        var merged = TrackDialogueMerger.Merge(new[]
        {
            new DialogueTrack("Преподаватель", TrackDialogueMerger.Parse(Teacher)),
            new DialogueTrack("Ученик", TrackDialogueMerger.Parse(Student)),
        });

        Assert.Equal(
            ">>>>>>>\n" +
            "- 0.00 Ученик: \"Olha.\n" +
            "Hm.\n" +
            "Киноистоямы очень далеко отсюда.\"\n" +
            "- 8.66 Преподаватель: \"Ага,\"\n" +
            // equal time: the track named first goes first
            "- 17.36 Преподаватель: \"entonces,\"\n" +
            "- 17.36 Ученик: \"Понял, да.\"\n" +
            // speaker_1 of a one-voice track is the same participant
            "- 40.45 Преподаватель: \"la cama de matrimonio\"\n" +
            "- 161.75 Ученик: \"донде, но сейчас demasiado dinero.\"\n" +
            "<<<<<\n",
            merged);
    }

    [Fact]
    public void Merge_keeps_one_line_per_source_line()
    {
        var teacher = TrackDialogueMerger.Parse(Teacher);
        var student = TrackDialogueMerger.Parse(Student);

        var merged = TrackDialogueMerger.Merge(new[]
        {
            new DialogueTrack("A", teacher),
            new DialogueTrack("B", student),
        });

        Assert.Equal(teacher.Count + student.Count, TrackDialogueMerger.Parse(merged).Count);
    }

    [Fact]
    public void Merge_labels_entries_that_have_no_time_marks()
    {
        var merged = TrackDialogueMerger.Merge(new[]
        {
            new DialogueTrack("Преподаватель", TrackDialogueMerger.Parse(Teacher)),
            new DialogueTrack("Ученик", TrackDialogueMerger.Parse(Student), new HashSet<int> { 0 }),
        });

        Assert.Contains("- 0.00 Ученик (без времени): \"Olha.\n", merged);
        Assert.Contains("- 17.36 Ученик: \"Понял, да.\"\n", merged);
    }

    [Fact]
    public void Merge_of_one_track_under_its_own_label_reproduces_the_source()
    {
        var merged = TrackDialogueMerger.Merge(new[]
        {
            new DialogueTrack("speaker_0", TrackDialogueMerger.Parse(Student)),
        });

        Assert.Equal(Student, merged);
    }

    [Fact]
    public void Merge_of_no_tracks_is_an_empty_wrapped_transcript()
    {
        Assert.Equal(">>>>>>>\n<<<<<\n", TrackDialogueMerger.Merge(Array.Empty<DialogueTrack>()));
    }
}
