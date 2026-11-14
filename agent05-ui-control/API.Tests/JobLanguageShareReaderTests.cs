using System.Text;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>How much of a job's speech is Spanish and how much Russian, counted from its chunk answers.</summary>
public sealed class JobLanguageShareReaderTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "language_share_" + Guid.NewGuid().ToString("N"));

    public JobLanguageShareReaderTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        try { Directory.Delete(_workspace, true); } catch { /* ignore */ }
    }

    private string JobDir(string jobId) => Path.Combine(_workspace, jobId);

    private void Put(string jobId, string relativePath, string content)
    {
        var full = Path.Combine(JobDir(jobId), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    private static string Answer(params (double Start, double End, string Text)[] lines) =>
        "{\"segments\":["
        + string.Join(",", lines.Select(l => FormattableString.Invariant(
            $"{{\"start\":{l.Start},\"end\":{l.End},\"text\":\"{l.Text}\"}}")))
        + "]}";

    // ---------- the rule of one line ----------

    [Theory]
    [InlineData("Hola, buenos días.", LineLanguage.Spanish)]
    [InlineData("Привет, как дела?", LineLanguage.Russian)]
    [InlineData("Да, claro que sí, vamos", LineLanguage.Spanish)] // 4 of 5 words are Latin
    [InlineData("Это значит mesa и silla", LineLanguage.Mixed)] // 2 of 5
    [InlineData("la casa — дом, а el perro — собака", LineLanguage.Mixed)] // 4 of 7; the dashes are not words
    [InlineData("Слово mesa значит стол, запомни это", LineLanguage.Russian)] // 1 of 6
    [InlineData("12 – 15.", LineLanguage.None)]
    [InlineData("   ", LineLanguage.None)]
    [InlineData(null, LineLanguage.None)]
    public void A_line_is_Spanish_Russian_or_mixed_by_the_script_of_its_words(string? text, LineLanguage expected)
    {
        Assert.Equal(expected, LanguageShare.Classify(text));
    }

    [Fact]
    public void A_word_with_both_scripts_counts_as_Russian()
    {
        // "дaвай" with a Latin "a" inside is still a Russian word
        Assert.Equal(LineLanguage.Russian, LanguageShare.Classify("дaвай"));
    }

    // ---------- a transcription job ----------

    [Fact]
    public void Folder_without_chunk_answers_gives_nothing()
    {
        Directory.CreateDirectory(JobDir("job1"));
        Assert.Null(JobLanguageShareReader.Read(JobDir("job1")));
        Assert.Null(JobLanguageShareReader.Read(JobDir("no_such_job")));
        Assert.Null(JobLanguageShareReader.ReadReport(JobDir("job1"), JobDir).Total);
    }

    [Fact]
    public void Seconds_of_the_lines_are_summed_by_language_over_all_chunks()
    {
        Put("job1", "chunks_json/track_part_000.json", Answer(
            (0, 4, "Hola, buenos días."),
            (5, 7.5, "Привет, как дела?"),
            (8, 9, "Это значит mesa и silla")));
        Put("job1", "chunks_json/track_part_001.json", Answer(
            (0.5, 6.5, "Vamos a empezar la clase."),
            (7, 7, "12"))); // no word: not a line

        var share = JobLanguageShareReader.Read(JobDir("job1"));

        Assert.NotNull(share);
        Assert.Equal(4, share!.Lines);
        Assert.Equal(2, share.SpanishLines);
        Assert.Equal(1, share.RussianLines);
        Assert.Equal(1, share.MixedLines);
        Assert.Equal(0, share.UntimedLines);
        Assert.Equal(10, share.SpanishSeconds, 6);
        Assert.Equal(2.5, share.RussianSeconds, 6);
        Assert.Equal(1, share.MixedSeconds, 6);
        Assert.Equal(13.5, share.SpeechSeconds, 6);
    }

    [Fact]
    public void An_answer_without_time_marks_is_a_line_without_seconds()
    {
        Put("job1", "chunks_json/track_part_000.json", Answer((0, 3, "Hola, buenos días.")));
        Put("job1", "chunks_json/track_part_001.json", "{\"text\":\"Сегодня повторяем глаголы.\"}");

        var share = JobLanguageShareReader.Read(JobDir("job1"))!;

        Assert.Equal(2, share.Lines);
        Assert.Equal(1, share.RussianLines);
        Assert.Equal(1, share.UntimedLines);
        Assert.Equal(0, share.RussianSeconds, 6);
        Assert.Equal(3, share.SpeechSeconds, 6);
    }

    [Fact]
    public void A_chunk_the_operator_split_and_merged_is_counted_by_its_parts()
    {
        // The chunk's own answer is what the transcript no longer holds.
        Put("job1", "chunks_json/track_part_002.json", Answer((0, 60, "Привет, как дела?")));
        Put("job1", "split_chunks/chunk_2/chunk_2_merged.md", "- 0.00 speaker: \"...\"\n");
        Put("job1", "split_chunks/chunk_2/results/sub_chunk_00_result.json",
            "{\"raw_response\":" + Answer((0, 10, "Hola, buenos días.")) + "}");
        Put("job1", "split_chunks/chunk_2/results/sub_chunk_01_result.json",
            "{\"raw_response\":" + Answer((0, 5, "Vamos a empezar.")) + "}");
        // A split that was not merged leaves its chunk as it is.
        Put("job1", "chunks_json/track_part_003.json", Answer((0, 7, "Привет, как дела?")));
        Put("job1", "split_chunks/chunk_3/results/sub_chunk_00_result.json",
            "{\"raw_response\":" + Answer((0, 2, "Hola.")) + "}");

        var share = JobLanguageShareReader.Read(JobDir("job1"))!;

        Assert.Equal(15, share.SpanishSeconds, 6);
        Assert.Equal(7, share.RussianSeconds, 6);
        Assert.Equal(3, share.Lines);
    }

    [Fact]
    public void A_file_that_cannot_be_read_adds_nothing_and_the_rest_is_counted()
    {
        Put("job1", "chunks_json/track_part_000.json", "{ not json");
        Put("job1", "chunks_json/track_part_001.json", Answer((1, 3, "Hola, buenos días.")));

        var share = JobLanguageShareReader.Read(JobDir("job1"))!;

        Assert.Equal(1, share.Lines);
        Assert.Equal(2, share.SpanishSeconds, 6);
    }

    // ---------- a dialogue job ----------

    [Fact]
    public void A_transcription_job_reports_its_total_and_no_tracks()
    {
        Put("job1", "chunks_json/track_part_000.json", Answer((0, 4, "Hola, buenos días.")));

        var report = JobLanguageShareReader.ReadReport(JobDir("job1"), JobDir);

        Assert.Empty(report.Tracks);
        Assert.Equal(4, report.Total!.SpanishSeconds, 6);
    }

    [Fact]
    public void A_dialogue_reports_each_track_and_their_sum()
    {
        Put("student", "chunks_json/a_part_000.json", Answer((0, 6, "Привет, как дела?"), (7, 9, "Hola.")));
        Put("teacher", "chunks_json/b_part_000.json", Answer((0, 12, "Vamos a empezar la clase.")));
        Put("dialogue", "dialogue.json",
            "{\"schemaVersion\":1,\"title\":\"Lesson\",\"tracks\":["
            + "{\"jobId\":\"student\",\"speaker\":\"Student\"},"
            + "{\"jobId\":\"teacher\",\"speaker\":\"Teacher\"},"
            + "{\"jobId\":\"gone\",\"speaker\":\"Guest\"}],\"merges\":[]}");

        var report = JobLanguageShareReader.ReadReport(JobDir("dialogue"), JobDir);

        Assert.Equal(new[] { "Student", "Teacher", "Guest" }, report.Tracks.Select(t => t.Speaker).ToArray());
        Assert.Equal(8, report.Tracks[0].Share!.SpeechSeconds, 6);
        Assert.Equal(6, report.Tracks[0].Share!.RussianSeconds, 6);
        Assert.Equal(12, report.Tracks[1].Share!.SpanishSeconds, 6);
        Assert.Null(report.Tracks[2].Share); // the job of the track is gone
        Assert.Equal(20, report.Total!.SpeechSeconds, 6);
        Assert.Equal(14, report.Total.SpanishSeconds, 6);
        Assert.Equal(3, report.Total.Lines);
    }

    [Fact]
    public void A_track_id_that_is_not_a_folder_name_is_not_followed()
    {
        Put("outside", "chunks_json/a_part_000.json", Answer((0, 6, "Hola, buenos días.")));
        Put("dialogue", "dialogue.json",
            "{\"schemaVersion\":1,\"tracks\":[{\"jobId\":\"../outside\",\"speaker\":\"Student\"}],\"merges\":[]}");

        var asked = new List<string>();
        var report = JobLanguageShareReader.ReadReport(JobDir("dialogue"), id =>
        {
            asked.Add(id);
            return JobDir(id);
        });

        Assert.Empty(asked);
        Assert.Null(report.Tracks.Single().Share);
        Assert.Null(report.Total);
    }
}
