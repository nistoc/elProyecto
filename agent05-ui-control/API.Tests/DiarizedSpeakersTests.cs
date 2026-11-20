using System.Text;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Features.LessonText.Application;
using XtractManager.Features.Sessions.Application;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// The speakers of a diarize run tied across its chunks, the reading of its chunk answers, and which run of a
/// track gives the text and which the speakers. All lines here are made up.
/// </summary>
public sealed class DiarizedSpeakersTests : IDisposable
{
    private readonly string _job = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "diarized_" + Guid.NewGuid().ToString("N"));

    public DiarizedSpeakersTests() => Directory.CreateDirectory(_job);

    public void Dispose()
    {
        try { Directory.Delete(_job, true); } catch { /* ignore */ }
    }

    private static DiarizedSegment S(int unit, double start, double end, string speaker, string text, bool repeat = false) =>
        new(unit, start, end, speaker, text, repeat);

    private static string NameOf(DiarizedSpeakers.Tied tied, string word) =>
        tied.Words.First(w => w.Text.Trim(',', '.', '?', '¿') == word).Speaker;

    // ---- tying the names ----

    [Fact]
    public void The_names_are_tied_by_the_words_both_chunks_heard_also_where_the_other_speaker_says_more()
    {
        var tied = DiarizedSpeakers.Tie(new[]
        {
            // chunk 0: A speaks most, B a little; the last words lie in the stretch chunk 1 hears too
            S(0, 0, 40, "A", "uno dos tres cuatro cinco seis siete ocho nueve diez"),
            S(0, 40, 60, "B", "sí claro vale"),
            S(0, 88, 99, "A", "la casa grande es bonita"),
            // chunk 1 names the same voice "X": it repeats the words of A; "Y" tells a long story
            S(1, 88.4, 99.2, "X", "la casa grande es bonita", repeat: true),
            S(1, 100, 150, "Y", "ayer fui al mercado con mi hermana y compramos pan queso fruta leche"),
            S(1, 150, 160, "X", "muy bien"),
        });

        // By the words said in chunk 1 alone, Y would be taken for A: the shared words say otherwise.
        Assert.Equal(NameOf(tied, "uno"), NameOf(tied, "muy"));
        Assert.Equal(NameOf(tied, "sí"), NameOf(tied, "ayer"));
        Assert.NotEqual(NameOf(tied, "uno"), NameOf(tied, "ayer"));
        Assert.Equal("speaker_1", NameOf(tied, "uno")); // 17 words against 16
        Assert.Equal(new SpeakerTies(2, 1, 0), tied.Ties);
        // the repeated words are given once
        Assert.Single(tied.Words, w => w.Text == "bonita");
        Assert.Equal(tied.Words.OrderBy(w => w.Start).ToList(), tied.Words);
    }

    [Fact]
    public void Without_shared_words_the_one_who_says_most_in_a_chunk_takes_the_name_of_the_one_who_said_most_so_far()
    {
        var tied = DiarizedSpeakers.Tie(new[]
        {
            S(0, 0, 40, "A", "uno dos tres cuatro cinco seis siete ocho nueve diez"),
            S(0, 40, 50, "B", "sí claro vale"),
            // unit 1 gave no line (a chunk the plain model answered): unit 2 does not follow unit 0,
            // so its repeated words are not compared with unit 0
            S(2, 45.2, 49.8, "B", "sí claro vale", repeat: true),
            S(2, 300, 330, "B", "once doce trece catorce quince dieciséis"),
            S(2, 330, 335, "A", "bueno vale"),
        });

        Assert.Equal(NameOf(tied, "uno"), NameOf(tied, "once"));
        Assert.Equal(NameOf(tied, "sí"), NameOf(tied, "bueno"));
        Assert.Equal(new SpeakerTies(2, 0, 1), tied.Ties);
    }

    [Fact]
    public void A_third_voice_gets_a_name_of_its_own_only_when_no_name_is_left()
    {
        var tied = DiarizedSpeakers.Tie(new[]
        {
            S(0, 0, 40, "A", "uno dos tres cuatro cinco seis"),
            S(0, 40, 50, "B", "sí claro"),
            S(1, 100, 140, "A", "siete ocho nueve diez"),
            S(1, 140, 150, "B", "vale"),
            S(1, 150, 151, "C", "eh"),
        });

        Assert.Equal(new[] { "speaker_1", "speaker_2", "speaker_3" }, tied.Words.Select(w => w.Speaker).Distinct().OrderBy(n => n));
        Assert.Equal("speaker_3", NameOf(tied, "eh"));
    }

    // ---- reading the chunk answers ----

    private void Put(string relativePath, string content)
    {
        var full = Path.Combine(_job, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    private static string Answer(params (double Start, double End, string? Speaker, string Text)[] lines) =>
        "{\"segments\":["
        + string.Join(",", lines.Select(l => FormattableString.Invariant(
            $"{{\"start\":{l.Start},\"end\":{l.End},{(l.Speaker == null ? "" : $"\"speaker\":\"{l.Speaker}\",")}\"text\":\"{l.Text}\"}}")))
        + "]";

    [Fact]
    public void The_lines_of_a_diarize_run_keep_their_names_their_chunk_and_the_repeated_start()
    {
        Put("timeline/chunks.json",
            "{\"schema_version\":1,\"chunks\":["
            + "{\"index\":0,\"file\":\"rec_part_000.m4a\",\"offset_sec\":0,\"emit_guard_sec\":0},"
            + "{\"index\":1,\"file\":\"rec_part_001.m4a\",\"offset_sec\":100,\"emit_guard_sec\":12},"
            + "{\"index\":2,\"file\":\"rec_part_002.m4a\",\"offset_sec\":200,\"emit_guard_sec\":12},"
            + "{\"index\":3,\"file\":\"rec_part_003.m4a\",\"offset_sec\":300,\"emit_guard_sec\":12},"
            + "{\"index\":4,\"file\":\"rec_part_004.m4a\",\"offset_sec\":400,\"emit_guard_sec\":12}]}");
        Put("chunks_json/rec_part_000.json", Answer((1, 3, "A", "Hola, ¿qué tal?"), (5, 6, null, "Sin nombre.")) + "}");
        Put("chunks_json/rec_part_001.json", Answer((3, 5, "B", "Repetido."), (20, 22, "A", "Привет.")) + "}");
        // refused whole by the server and asked in two parts: each part named its speakers on its own
        Put("chunks_json/rec_part_002.json", Answer((15, 16, "A", "Primera parte."), (50, 52, "A", "Segunda parte.")) +
            ",\"auto_split\":{\"reason\":\"server_error\",\"parts\":[{\"index\":0,\"start_sec\":0},{\"index\":1,\"start_sec\":40}]}}");
        // the plain model answered: no time, no speaker
        Put("chunks_json/rec_part_003.json", "{\"text\":\"Texto sin tiempo.\"}");
        Put("chunks_json/rec_part_004.json", Answer((20, 21, "B", "Al final.")) + "}");

        var segments = JobTimedLinesReader.ReadDiarized(_job)!;

        Assert.Equal(
            new[]
            {
                (0, 1.0, "A", false), (1, 103.0, "B", true), (1, 120.0, "A", false),
                (2, 215.0, "A", false), (3, 250.0, "A", false), (5, 420.0, "B", false),
            },
            segments.Select(s => (s.Unit, s.Start, s.Speaker, s.Repeat)).ToArray());
        Assert.Null(JobTimedLinesReader.ReadDiarized(Path.Combine(_job, "no_such_job")));
    }

    [Fact]
    public void A_run_whose_lines_name_nobody_gives_no_diarized_lines()
    {
        Put("timeline/chunks.json", "{\"schema_version\":1,\"chunks\":[{\"index\":0,\"file\":\"rec_part_000.m4a\",\"offset_sec\":0,\"emit_guard_sec\":0}]}");
        Put("chunks_json/rec_part_000.json", Answer((1, 3, null, "Hola.")) + "}");

        Assert.Null(JobTimedLinesReader.ReadDiarized(_job));
    }

    // ---- which run gives the text ----

    private static TrackRun Run(string id, string? model, bool transcript = true) =>
        new(id, "completed", "completed", null, transcript, false)
        {
            Settings = model == null ? null : new JobRunSettings(JobRunSettings.SourceRun, model, true, 1.0, Array.Empty<string>()),
        };

    [Fact]
    public void The_text_is_of_the_newest_plain_run_and_the_speakers_of_the_newest_diarize_run()
    {
        var plain = Run("plain", "gpt-transcribe");
        var oldDiarize = Run("old-diarize", "gpt-4o-transcribe-diarize");
        var diarize = Run("diarize", "gpt-4o-transcribe-diarize");
        var unknown = Run("unknown", null);
        var runs = new[] { unknown, plain, oldDiarize, diarize }; // oldest first

        Assert.Equal(new[] { "plain", "diarize", "old-diarize", "unknown" }, TrackTextRuns.TextOrder(runs).Select(r => r.JobId));
        Assert.Equal(new[] { "diarize", "old-diarize" }, TrackTextRuns.SpeakerSources(runs, plain).Select(r => r.JobId));
        Assert.Empty(TrackTextRuns.SpeakerSources(runs, diarize));
        // without a plain run the newest run gives the text, as before
        Assert.Equal("diarize", TrackTextRuns.TextOrder(new[] { unknown, diarize }).First().JobId);
        Assert.Equal("unknown", TrackTextRuns.TextOrder(new[] { diarize, unknown }).First().JobId);
    }
}
