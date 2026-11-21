using System.Text;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>The lines of a job on the timeline of the recording, and the report of a dialogue built from them.</summary>
public sealed class JobTimedLinesReaderTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "timed_lines_" + Guid.NewGuid().ToString("N"));

    public JobTimedLinesReaderTests() => Directory.CreateDirectory(_workspace);

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

    private static string Chunks(params (string File, double Offset, double Guard)[] chunks) =>
        "{\"schema_version\":1,\"chunks\":["
        + string.Join(",", chunks.Select((c, i) => FormattableString.Invariant(
            $"{{\"index\":{i},\"file\":\"{c.File}\",\"offset_sec\":{c.Offset},\"emit_guard_sec\":{c.Guard}}}")))
        + "]}";

    private static string SpeechMap(params (double SourceStart, double SourceEnd, double OutputStart)[] pieces) =>
        "{\"schema_version\":1,\"pieces\":["
        + string.Join(",", pieces.Select(p => FormattableString.Invariant(
            $"{{\"source_start_sec\":{p.SourceStart},\"source_end_sec\":{p.SourceEnd},\"output_start_sec\":{p.OutputStart}}}")))
        + "]}";

    // ---------- lines of one job ----------

    [Fact]
    public void A_job_without_the_chunk_timeline_cannot_be_placed()
    {
        Put("job1", "chunks_json/track_part_000.json", Answer((0, 4, "Hola, buenos días.")));

        Assert.Null(JobTimedLinesReader.Read(JobDir("job1")));
        Assert.Null(JobTimedLinesReader.Read(JobDir("no_such_job")));
    }

    [Fact]
    public void A_line_stands_at_the_offset_of_its_chunk_and_the_repeated_start_is_left_out()
    {
        Put("job1", "timeline/chunks.json", Chunks(("track_part_000.wav", 0, 0), ("track_part_001.wav", 100, 1)));
        Put("job1", "chunks_json/track_part_000.json", Answer((2, 4, "Hola, buenos días."), (95, 100.5, "Vamos a empezar.")));
        Put("job1", "chunks_json/track_part_001.json", Answer(
            (0.5, 0.9, "Vamos a empezar."), // inside the guard: the previous chunk already has it
            (2, 4.5, "Привет, как дела?"),
            (0, 0, "Texto sin tiempo."), // a plain model answered: no time, no place
            (6, 7, "12")));

        var lines = JobTimedLinesReader.Read(JobDir("job1"))!;

        Assert.Equal(
            new[] { (2.0, 4.0, "Hola, buenos días."), (95.0, 100.5, "Vamos a empezar."), (102.0, 104.5, "Привет, как дела?") },
            lines.Select(l => (l.Start, l.End, l.Text)).ToArray());
    }

    [Fact]
    public void Times_of_compressed_audio_are_put_back_on_the_recording()
    {
        // Kept pieces: 10..20 of the recording at 0..10 of the compressed file, 30..40 at 11..21 (1 s of pause kept).
        Put("job1", "timeline/chunks.json", Chunks(("track_silc_part_000.wav", 0, 0)));
        Put("job1", "timeline/speech_map.json", SpeechMap((10, 20, 0), (30, 40, 11)));
        Put("job1", "chunks_json/track_silc_part_000.json", Answer(
            (2, 5, "Hola, buenos días."),
            (8, 10.5, "Vamos a empezar."), // ends in the kept pause: the sound stopped with the piece
            (9.8, 13, "Привет, как дела?"), // begins on the last moments of a piece and goes on: starts with the next
            (12, 15, "Muy bien, gracias.")));

        var lines = JobTimedLinesReader.Read(JobDir("job1"))!;

        var byText = lines.ToDictionary(l => l.Text, l => (l.Start, l.End));
        Assert.Equal((12.0, 15.0), byText["Hola, buenos días."]);
        Assert.Equal((18.0, 20.0), byText["Vamos a empezar."]);
        Assert.Equal((30.0, 32.0), byText["Привет, как дела?"]);
        Assert.Equal((31.0, 34.0), byText["Muy bien, gracias."]);
    }

    [Fact]
    public void Compressed_audio_without_a_speech_map_cannot_be_placed()
    {
        Put("job1", "timeline/chunks.json", Chunks(("track_silc_part_000.wav", 0, 0)));
        Put("job1", "converted_wav/track.silence_report.json", "{}");
        Put("job1", "chunks_json/track_silc_part_000.json", Answer((2, 5, "Hola, buenos días.")));

        Assert.Null(JobTimedLinesReader.Read(JobDir("job1")));
    }

    [Fact]
    public void A_chunk_the_operator_split_and_merged_cannot_be_placed()
    {
        Put("job1", "timeline/chunks.json", Chunks(("track_part_000.wav", 0, 0)));
        Put("job1", "chunks_json/track_part_000.json", Answer((2, 5, "Hola, buenos días.")));
        Put("job1", "split_chunks/chunk_0/chunk_0_merged.md", "- 0.00 speaker: \"...\"\n");
        Put("job1", "split_chunks/chunk_0/results/sub_chunk_00_result.json", "{\"raw_response\":" + Answer((0, 2, "Hola.")) + "}");

        Assert.Null(JobTimedLinesReader.Read(JobDir("job1")));
    }

    // ---------- the share respects the same guard ----------

    [Fact]
    public void The_share_leaves_out_the_repeated_start_of_a_chunk_too()
    {
        Put("job1", "timeline/chunks.json", Chunks(("track_part_000.wav", 0, 0), ("track_part_001.wav", 100, 1)));
        Put("job1", "chunks_json/track_part_000.json", Answer((95, 100.5, "Vamos a empezar.")));
        Put("job1", "chunks_json/track_part_001.json", Answer((0.5, 0.9, "Vamos a empezar."), (2, 4.5, "Привет, как дела?"), (0, 0, "Texto sin tiempo.")));

        var share = JobLanguageShareReader.Read(JobDir("job1"))!;

        Assert.Equal(3, share.Lines); // the repeated one is not a line; the one without time is, without seconds
        Assert.Equal(1, share.UntimedLines);
        Assert.Equal(5.5, share.SpanishSeconds, 6);
        Assert.Equal(2.5, share.RussianSeconds, 6);
    }

    // ---------- a dialogue ----------

    private void PutDialogue(params string[] trackJobIds) =>
        Put("dialogue", "dialogue.json",
            "{\"schemaVersion\":1,\"tracks\":["
            + string.Join(",", trackJobIds.Select(id => $"{{\"jobId\":\"{id}\",\"speaker\":\"{char.ToUpperInvariant(id[0]) + id[1..]}\"}}"))
            + "],\"merges\":[]}");

    [Fact]
    public void A_dialogue_tells_how_the_talk_goes()
    {
        Put("teacher", "timeline/chunks.json", Chunks(("a_part_000.wav", 0, 0)));
        Put("teacher", "chunks_json/a_part_000.json", Answer((0, 10, "Vamos a empezar la clase de hoy."), (20, 22, "Muy bien, sigue.")));
        Put("student", "timeline/chunks.json", Chunks(("b_part_000.wav", 0, 0)));
        Put("student", "chunks_json/b_part_000.json", Answer((5, 8, "Espera, tengo una pregunta."), (14, 16, "Привет, как дела?")));
        PutDialogue("teacher", "student");

        var report = JobLanguageShareReader.ReadReport(JobDir("dialogue"), JobDir);

        var talk = report.Conversation!;
        Assert.Equal(new[] { "Teacher", "Student" }, talk.Tracks.Select(t => t.Speaker).ToArray());
        Assert.Equal(1, talk.Tracks[1].Interruptions); // 5 s, over the teacher's first line
        Assert.Equal(1, talk.Tracks[1].LongPausesBeforeAnswer); // the teacher ends at 10, the student speaks at 14
        Assert.Equal(0, talk.Tracks[1].LongPausesInOwnSpeech);
        Assert.Equal(1, talk.Tracks[0].LongPausesBeforeAnswer); // 16 → 20
        Assert.Equal(3, talk.OverlapSeconds, 6);
        Assert.Equal(22, talk.SpanSeconds, 6);
        Assert.Equal(8, talk.SilenceSeconds, 6); // 10..14 and 16..20
        Assert.Equal(17, report.Total!.SpeechSeconds, 6);
    }

    [Fact]
    public void A_dialogue_with_a_track_that_cannot_be_placed_tells_the_shares_only()
    {
        Put("teacher", "timeline/chunks.json", Chunks(("a_part_000.wav", 0, 0)));
        Put("teacher", "chunks_json/a_part_000.json", Answer((0, 10, "Vamos a empezar la clase de hoy.")));
        Put("student", "chunks_json/b_part_000.json", Answer((5, 8, "Espera, tengo una pregunta."))); // no timeline
        PutDialogue("teacher", "student");

        var report = JobLanguageShareReader.ReadReport(JobDir("dialogue"), JobDir);

        Assert.Null(report.Conversation);
        Assert.Equal(13, report.Total!.SpeechSeconds, 6);
    }

    [Fact]
    public void A_transcription_job_has_no_talk()
    {
        Put("job1", "timeline/chunks.json", Chunks(("a_part_000.wav", 0, 0)));
        Put("job1", "chunks_json/a_part_000.json", Answer((0, 10, "Vamos a empezar la clase de hoy.")));

        Assert.Null(JobLanguageShareReader.ReadReport(JobDir("job1"), JobDir).Conversation);
    }

    // ---------- the other hearings of a line of the plain-text model ----------

    [Fact]
    public void A_line_of_the_plain_model_carries_what_its_piece_heard()
    {
        Put("job1", "timeline/chunks.json", Chunks(("track_part_000.wav", 0, 0), ("track_part_001.wav", 100, 2)));
        Put("job1", "chunks_json/track_part_000.json", """
            {"segments":[
              {"id":0,"start":1.0,"end":4.0,"text":"Abre la ventana."},
              {"id":1,"start":9.5,"end":12.25,"text":"мне нужно купить хлеб"}],
             "speech_pieces":{"items":[
              {"start":1.0,"end":4.0,"own_text":"абре ла вентана"},
              {"start":5.0,"end":8.0,"own_text":"  "},
              {"start":9.5,"end":12.25,"own_text":"мне нужно купить хлеб","doubt":"translated",
               "context_text":"да мне нужно купить хлеб","whole_text_line":"Necesito comprar pan.","second_look":"line"}]}}
            """);
        // A line inside the guard is left out; the line after it still finds its own piece.
        Put("job1", "chunks_json/track_part_001.json", """
            {"segments":[
              {"id":0,"start":0.5,"end":1.5,"text":"Repetido."},
              {"id":1,"start":3.0,"end":6.0,"text":"Cierra la puerta."}],
             "speech_pieces":{"items":[
              {"start":0.5,"end":1.5,"own_text":"репетидо"},
              {"start":3.0,"end":6.0,"own_text":"сьерра ла пуэрта"}]}}
            """);

        var lines = JobTimedLinesReader.ReadHeard(JobDir("job1"));

        Assert.NotNull(lines);
        Assert.Equal(3, lines!.Count);
        Assert.Equal(new HeardLine(1, 4, "Abre la ventana.", "абре ла вентана", null, null), lines[0]);
        Assert.Equal(new HeardLine(9.5, 12.25, "мне нужно купить хлеб", "мне нужно купить хлеб", "да мне нужно купить хлеб", "Necesito comprar pan."), lines[1]);
        Assert.Equal(new HeardLine(103, 106, "Cierra la puerta.", "сьерра ла пуэрта", null, null), lines[2]);
        // the plain reading of the same job is what it was
        Assert.Equal(lines.Select(l => (l.Start, l.End, l.Text)), JobTimedLinesReader.Read(JobDir("job1"))!.Select(l => (l.Start, l.End, l.Text)));
    }

    [Fact]
    public void A_line_of_another_model_has_no_piece()
    {
        Put("job1", "timeline/chunks.json", Chunks(("track_part_000.wav", 0, 0)));
        Put("job1", "chunks_json/track_part_000.json", Answer((1, 4, "Abre la ventana.")));

        var line = Assert.Single(JobTimedLinesReader.ReadHeard(JobDir("job1"))!);

        Assert.Equal(new HeardLine(1, 4, "Abre la ventana.", null, null, null), line);
    }
}
