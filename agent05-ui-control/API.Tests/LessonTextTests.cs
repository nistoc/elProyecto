using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Features.LessonText.Application;
using XtractManager.Features.LessonText.Infrastructure;
using XtractManager.Features.Sessions.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// The text of a lesson with its speakers: speakers of another service laid onto our lines, the guess of the
/// teacher, the roles a person gives. All names and lines here are made up.
/// </summary>
public sealed class LessonTextTests : IDisposable
{
    private const string Day = "2025.08.06";
    private const string Audio = "Voice 250806_191245.m4a";
    private const string AnswerName = "Voice_250806_191245_rus.json";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "text_" + Guid.NewGuid().ToString("N"));
    private readonly string _recordings;
    private readonly string _workspace;
    private readonly string _voices;

    public LessonTextTests()
    {
        _recordings = Path.Combine(_base, "recordings");
        _workspace = Path.Combine(_base, "runtime");
        _voices = Path.Combine(_base, "voice-speakers");
        Directory.CreateDirectory(Path.Combine(_recordings, Day));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, true); } catch { /* ignore */ }
    }

    private static HeardWord W(double start, string text, string speaker) => new(start, start + 0.3, text, speaker);

    // ---- SpeakerOverlay ----

    [Fact]
    public void A_line_is_cut_where_the_heard_speaker_changes_and_a_cyrillic_word_is_found_by_its_sound()
    {
        var heard = new[]
        {
            W(10.0, "Hola,", "speaker_0"), W(10.4, "¿qué", "speaker_0"), W(10.8, "tal?", "speaker_0"),
            W(11.6, "Bien,", "speaker_1"), W(12.0, "gracias,", "speaker_1"), W(12.4, "¿y", "speaker_1"), W(12.7, "tú?", "speaker_1"),
        };

        var parts = SpeakerOverlay.Split("Hola, ¿qué tal? Bien, грасиас, y tú?", 10.0, 20.0, heard);

        Assert.Equal(2, parts.Count);
        Assert.Equal(("speaker_0", "Hola, ¿qué tal?"), (parts[0].Speaker, parts[0].Text));
        Assert.Equal(("speaker_1", "Bien, грасиас, y tú?"), (parts[1].Speaker, parts[1].Text));
        Assert.Equal(10.0, parts[0].Start);
        Assert.InRange(parts[1].Start, 11.0, 12.5);
        Assert.Equal(3, parts[1].Matched);
    }

    [Fact]
    public void A_line_one_speaker_says_stays_whole_and_a_line_nobody_heard_has_no_speaker()
    {
        var heard = new[] { W(30.0, "Vamos", "speaker_0"), W(30.4, "a", "speaker_0"), W(30.6, "empezar.", "speaker_0") };

        var one = Assert.Single(SpeakerOverlay.Split("Vamos a empezar.", 30.0, 40.0, heard));
        var none = Assert.Single(SpeakerOverlay.Split("Nadie lo oyó.", 100.0, 110.0, heard));

        Assert.Equal("speaker_0", one.Speaker);
        Assert.Equal("Vamos a empezar.", one.Text);
        Assert.Null(none.Speaker);
        Assert.Equal(0, none.Matched);
    }

    [Fact]
    public void A_short_stretch_with_no_found_word_does_not_become_a_turn_of_its_own()
    {
        // The other service gave one stray word in the middle to another label: no word of ours is it.
        var heard = new[]
        {
            W(50.0, "Mañana", "speaker_0"), W(50.4, "vamos", "speaker_0"), W(50.8, "eh", "speaker_3"),
            W(51.2, "al", "speaker_0"), W(51.6, "mercado", "speaker_0"), W(52.0, "juntos.", "speaker_0"),
        };

        var only = Assert.Single(SpeakerOverlay.Split("Mañana vamos al mercado juntos.", 50.0, 60.0, heard));

        Assert.Equal("speaker_0", only.Speaker);
    }

    // ---- LessonRoleGuess ----

    [Fact]
    public void The_speaker_with_more_words_is_guessed_the_teacher_and_a_close_or_lone_speaker_gets_no_guess()
    {
        var guess = LessonRoleGuess.Guess(new Dictionary<string, int> { ["a"] = 1200, ["b"] = 600, ["c"] = 20 });
        var close = LessonRoleGuess.Guess(new Dictionary<string, int> { ["a"] = 1000, ["b"] = 950 });
        var lone = LessonRoleGuess.Guess(new Dictionary<string, int> { ["a"] = 1000, ["b"] = 40 });

        Assert.Equal(LessonRole.Teacher, guess["a"]);
        Assert.Equal(LessonRole.Student, guess["b"]);
        Assert.False(guess.ContainsKey("c"));
        Assert.Empty(close);
        Assert.Empty(lone);
        Assert.Equal(LessonRole.Teacher, LessonRoleGuess.RoleNamed("Преподаватель"));
        Assert.Equal(LessonRole.Student, LessonRoleGuess.RoleNamed(" ученик "));
        Assert.Null(LessonRoleGuess.RoleNamed("Ana Ruiz"));
    }

    // ---- OtherServiceAnswer ----

    [Fact]
    public void The_answer_of_an_audio_file_is_found_by_its_name()
    {
        var day = new DirectoryInfo(Path.Combine(_recordings, Day));
        var answers = new[] { "Voice_250806_191245_rus.json", "Voice 250806_194742.m4a.json" }
            .Select(n => new FileInfo(Path.Combine(day.FullName, n)))
            .ToList();

        Assert.Equal(answers[0].Name, OtherServiceAnswer.For("Voice 250806_191245.m4a", answers, 2)?.Name);
        Assert.Equal(answers[1].Name, OtherServiceAnswer.For("Voice 250806_194742.m4a", answers, 2)?.Name);
        Assert.Null(OtherServiceAnswer.For("Voice 250806_200000.m4a", answers, 3));
        Assert.Equal(answers[0].Name, OtherServiceAnswer.For("audio1.m4a", answers.Take(1).ToList(), 1)?.Name);
    }

    // ---- FileLessonSpeakersStore ----

    private FileLessonSpeakersStore Store()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:WorkspacePath"] = _workspace })
            .Build();
        var env = new FakeHostEnvironment();
        return new FileLessonSpeakersStore(config, env, new JobWorkspace(config, env, NullLogger<JobWorkspace>.Instance));
    }

    private string SpeakersFile(string date) =>
        Path.Combine(_base, "lesson-packages", date, FileLessonSpeakersStore.FileName);

    [Fact]
    public void A_role_is_kept_next_to_the_package_taken_away_again_and_hand_written_fields_stay()
    {
        var store = Store();
        store.SetRole("2025-08-06", "rec-1", "speaker_0", LessonRole.Teacher);
        var path = SpeakersFile("2025-08-06");
        var doc = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        doc["note"] = "written by hand";
        File.WriteAllText(path, doc.ToJsonString());

        store.SetRole("2025-08-06", "rec-1", "speaker_1", LessonRole.Student);
        store.SetRole("2025-08-06", "rec-1", "speaker_0", null);

        var roles = store.Roles("2025-08-06");
        Assert.Equal(LessonRole.Student, roles["rec-1"]["speaker_1"]);
        Assert.False(roles["rec-1"].ContainsKey("speaker_0"));
        Assert.Equal("written by hand", JsonNode.Parse(File.ReadAllText(path))!["note"]!.GetValue<string>());
        Assert.Empty(store.Roles("2025-08-13"));
    }

    [Fact]
    public void A_roles_file_that_cannot_be_read_is_never_written_over()
    {
        var store = Store();
        var path = SpeakersFile("2025-08-06");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        Assert.Throws<InvalidOperationException>(() => store.Roles("2025-08-06"));
        Assert.Throws<InvalidOperationException>(() => store.SetRole("2025-08-06", "rec-1", "speaker_0", LessonRole.Teacher));
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    // ---- The whole way: folders on disk, the controller ----

    /// <summary>
    /// A flat old day: one audio file, the answer of another service next to it, and a job of ours that
    /// transcribed the file without speakers.
    /// </summary>
    private void PutLesson()
    {
        var audio = Path.Combine(_recordings, Day, Audio);
        File.WriteAllText(audio, "made-up audio bytes", new UTF8Encoding(false));

        var segments = new JsonArray
        {
            Segment("speaker_0", (10.0, "Hola,"), (10.4, "¿qué"), (10.8, "tal?")),
            Segment("speaker_1", (11.6, "Bien,"), (12.0, "gracias,"), (12.4, "¿y"), (12.7, "tú?")),
            Segment("speaker_0", (20.0, "Muy"), (20.3, "bien."), (20.6, "Hoy"), (20.9, "hablamos"), (21.2, "del"),
                (21.5, "pasado"), (21.8, "y"), (22.1, "del"), (22.4, "futuro"), (22.7, "de"), (23.0, "los"), (23.3, "verbos.")),
        };
        File.WriteAllText(
            Path.Combine(_recordings, Day, AnswerName),
            new JsonObject { ["language_code"] = "ru", ["segments"] = segments }.ToJsonString(),
            new UTF8Encoding(false));

        var job = Path.Combine(_workspace, "job-plain-1");
        Directory.CreateDirectory(job);
        File.Copy(audio, Path.Combine(job, Audio));
        File.WriteAllText(
            Path.Combine(job, "transcript.md"),
            "- 10.0 speaker_0: Hola, ¿qué tal? Bien, грасиас, y tú?\n- 20.0 speaker_0: Muy bien. Hoy hablamos del pasado y del futuro de los verbos.\n",
            new UTF8Encoding(false));
    }

    private static JsonObject Segment(string speaker, params (double Start, string Text)[] words) => new()
    {
        ["start_time"] = words[0].Start,
        ["end_time"] = words[^1].Start + 0.3,
        ["text"] = string.Join(' ', words.Select(w => w.Text)),
        ["speaker"] = new JsonObject { ["id"] = speaker, ["name"] = speaker },
        ["words"] = new JsonArray(words
            .Select(w => (JsonNode)new JsonObject { ["start_time"] = w.Start, ["end_time"] = w.Start + 0.3, ["text"] = w.Text })
            .ToArray()),
    };

    private LessonTextController Controller()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:WorkspacePath"] = _workspace,
                [RecordingsCatalog.RootPathKey] = _recordings,
            })
            .Build();
        var env = new FakeHostEnvironment();
        var workspace = new JobWorkspace(config, env, NullLogger<JobWorkspace>.Instance);
        var store = new WorkspaceAwareJobStore(new InMemoryJobStore(), workspace, NullLogger<WorkspaceAwareJobStore>.Instance);
        var catalog = new RecordingsCatalog(config, env, NullLogger<RecordingsCatalog>.Instance);
        var speakers = new FileLessonSpeakersStore(config, env, workspace);
        var voices = new FileVoiceSpeakersStore(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [FileVoiceSpeakersStore.RootPathKey] = _voices }).Build(),
            env, workspace);
        var reader = new LessonTextReader(catalog, store, workspace, speakers, voices);
        return new LessonTextController(reader, speakers, catalog, NullLogger<LessonTextController>.Instance);
    }

    private static LessonTextResponse Body(ActionResult<LessonTextResponse> result) =>
        Assert.IsType<LessonTextResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task The_text_of_a_mixed_recording_gets_the_speakers_of_the_other_service_and_a_guessed_teacher()
    {
        PutLesson();

        var text = Body(await Controller().Get("2025-08-06"));

        var recording = Assert.Single(text.Recordings);
        Assert.Equal(LessonTextSource.Tracks, recording.Source);
        Assert.Equal(LessonSpeakersFrom.OtherService, recording.SpeakersFrom);
        Assert.Equal(new[] { "job-plain-1" }, recording.JobIds);
        Assert.Contains(AnswerName, recording.Files);
        Assert.Equal(
            new[] { ("speaker_0", "Hola, ¿qué tal?"), ("speaker_1", "Bien, грасиас, y tú?") },
            recording.Lines.Take(2).Select(l => (l.Speaker!, l.Text)));
        Assert.Equal(3, text.Lines);
        Assert.Equal(3, text.LinesWithSpeaker);
        Assert.Equal(LessonRole.Teacher, recording.Speakers.Single(s => s.Id == "speaker_0").Role);
        Assert.Equal(LessonRole.Student, recording.Speakers.Single(s => s.Id == "speaker_1").Role);
        Assert.All(recording.Speakers, s => Assert.False(s.RoleGiven));
        Assert.True(recording.OverlayWordsMatched > recording.OverlayWords / 2);
    }

    /// <summary>
    /// The same day with no answer of another service: the file was transcribed by the plain model (our text,
    /// Spanish in Latin letters) and by the diarize model (speakers, Spanish in Cyrillic), newest last.
    /// </summary>
    private void PutLessonWithDiarizeRun()
    {
        var audio = Path.Combine(_recordings, Day, Audio);
        File.WriteAllText(audio, "made-up audio bytes", new UTF8Encoding(false));

        var plain = Path.Combine(_workspace, "job-plain-1");
        Directory.CreateDirectory(plain);
        File.Copy(audio, Path.Combine(plain, Audio));
        File.WriteAllText(Path.Combine(plain, "run_settings.json"), """{ "schema_version": 1, "model": "gpt-transcribe" }""");
        File.WriteAllText(
            Path.Combine(plain, "transcript.md"),
            "- 10.0 speaker_0: Hola, ¿qué tal? Bien, грасиас, y tú?\n- 20.0 speaker_0: Muy bien. Hoy hablamos del pasado y del futuro de los verbos.\n",
            new UTF8Encoding(false));

        var diarize = Path.Combine(_workspace, "job-diarize-1");
        Directory.CreateDirectory(Path.Combine(diarize, "timeline"));
        Directory.CreateDirectory(Path.Combine(diarize, "chunks_json"));
        File.Copy(audio, Path.Combine(diarize, Audio));
        File.WriteAllText(Path.Combine(diarize, "run_settings.json"), """{ "schema_version": 1, "model": "gpt-4o-transcribe-diarize" }""");
        File.WriteAllText(
            Path.Combine(diarize, "transcript.md"),
            "- 10.0 speaker_0: Ола, ке таль?\n- 11.6 speaker_1: Бьен, грасиас, и ту?\n- 20.0 speaker_0: Муй бьен. Ой абламос дель пасадо и дель футуро де лос вербос.\n",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(diarize, "timeline", "chunks.json"),
            """{ "schema_version": 1, "chunks": [ { "index": 0, "file": "voice_part_000.m4a", "offset_sec": 0, "emit_guard_sec": 0 } ] }""");
        File.WriteAllText(
            Path.Combine(diarize, "chunks_json", "voice_part_000.json"),
            """
            { "segments": [
              { "start": 10.0, "end": 11.2, "speaker": "A", "text": "Ола, ке таль?" },
              { "start": 11.6, "end": 13.0, "speaker": "B", "text": "Бьен, грасиас, и ту?" },
              { "start": 20.0, "end": 23.6, "speaker": "A", "text": "Муй бьен. Ой абламос дель пасадо и дель футуро де лос вербос." }
            ] }
            """,
            new UTF8Encoding(false));
    }

    [Fact]
    public async Task A_mixed_recording_takes_its_text_from_the_plain_run_and_its_speakers_from_the_diarize_run()
    {
        PutLessonWithDiarizeRun();

        var text = Body(await Controller().Get("2025-08-06"));

        var recording = Assert.Single(text.Recordings);
        Assert.Equal(LessonSpeakersFrom.ModelOverlay, recording.SpeakersFrom);
        Assert.Equal(new[] { "job-plain-1", "job-diarize-1" }, recording.JobIds);
        // our Latin text, cut where the diarize model heard the other voice
        Assert.Equal(
            new[] { ("speaker_1", "Hola, ¿qué tal?"), ("speaker_2", "Bien, грасиас, y tú?") },
            recording.Lines.Take(2).Select(l => (l.Speaker!, l.Text)));
        Assert.Equal("speaker_1", recording.Lines[2].Speaker);
        Assert.StartsWith("Muy bien.", recording.Lines[2].Text);
        Assert.Equal(LessonRole.Teacher, recording.Speakers.Single(s => s.Id == "speaker_1").Role);
        Assert.Equal(LessonRole.Student, recording.Speakers.Single(s => s.Id == "speaker_2").Role);
        Assert.Equal(new SpeakerTies(1, 0, 0), recording.SpeakerTies);
        Assert.True(recording.OverlayWordsMatched > recording.OverlayWords / 2);
    }

    [Fact]
    public async Task A_file_that_lies_in_the_day_twice_gives_its_text_once()
    {
        PutLessonWithDiarizeRun();
        // the same bytes under another name: the batch plan calls it a duplicate, one transcription serves both
        File.Copy(Path.Combine(_recordings, Day, Audio), Path.Combine(_recordings, Day, "Voice 250806_191245 copy.m4a"));

        var text = Body(await Controller().Get("2025-08-06"));

        var recording = Assert.Single(text.Recordings);
        Assert.Equal(new[] { "job-plain-1", "job-diarize-1" }, recording.JobIds);
        Assert.Equal(3, text.Lines);
    }

    /// <summary>The answer of the voice tool for a job: the first voice 10.0–11.4 and 20.0–23.6, the second 11.5–13.2.</summary>
    private void PutVoices(string jobId)
    {
        Directory.CreateDirectory(_voices);
        File.WriteAllText(
            Path.Combine(_voices, jobId + ".json"),
            """
            { "speakers": 2, "segments": [
              { "start": 10.0, "end": 11.4, "speaker": "S1" },
              { "start": 11.5, "end": 13.2, "speaker": "S2" },
              { "start": 20.0, "end": 23.6, "speaker": "S1" }
            ] }
            """,
            new UTF8Encoding(false));
    }

    [Fact]
    public async Task A_mixed_recording_with_only_its_voices_gets_its_lines_cut_by_voice()
    {
        PutLessonWithDiarizeRun();
        Directory.Delete(Path.Combine(_workspace, "job-diarize-1"), true);
        PutVoices("job-plain-1");

        var text = Body(await Controller().Get("2025-08-06"));

        var recording = Assert.Single(text.Recordings);
        Assert.Equal(LessonSpeakersFrom.Voice, recording.SpeakersFrom);
        Assert.Equal(new[] { "job-plain-1" }, recording.JobIds);
        Assert.Contains("job-plain-1.json", recording.Files);
        Assert.Equal(
            new[] { ("voice_1", "Hola, ¿qué tal?"), ("voice_2", "Bien, грасиас, y tú?") },
            recording.Lines.Take(2).Select(l => (l.Speaker!, l.Text)));
        Assert.Equal("voice_1", recording.Lines[2].Speaker);
        Assert.Equal(LessonRole.Teacher, recording.Speakers.Single(s => s.Id == "voice_1").Role);
        Assert.Equal(LessonRole.Student, recording.Speakers.Single(s => s.Id == "voice_2").Role);
        Assert.Null(recording.SpeakerTies);
    }

    [Fact]
    public async Task A_diarize_run_of_a_recording_whose_voices_are_known_is_named_by_voice()
    {
        PutLessonWithDiarizeRun();
        PutVoices("job-diarize-1");

        var text = Body(await Controller().Get("2025-08-06"));

        var recording = Assert.Single(text.Recordings);
        Assert.Equal(LessonSpeakersFrom.ModelOverlay, recording.SpeakersFrom);
        Assert.Equal(new[] { "job-plain-1", "job-diarize-1" }, recording.JobIds);
        Assert.Contains("job-diarize-1.json", recording.Files);
        Assert.Equal(
            new[] { ("voice_1", "Hola, ¿qué tal?"), ("voice_2", "Bien, грасиас, y tú?") },
            recording.Lines.Take(2).Select(l => (l.Speaker!, l.Text)));
        Assert.Equal(new SpeakerTies(1, 0, 0, 1), recording.SpeakerTies);
    }

    [Fact]
    public async Task A_role_a_person_gives_wins_and_the_other_speaker_takes_the_opposite_one()
    {
        PutLesson();
        var controller = Controller();
        var recording = Body(await controller.Get("2025-08-06")).Recordings.Single().Id;

        var after = Body(await controller.SetRole(
            "2025-08-06", new LessonSpeakerRoleBody { Recording = recording, Speaker = "speaker_1", Role = "teacher" }));

        var speakers = after.Recordings.Single().Speakers;
        var one = speakers.Single(s => s.Id == "speaker_1");
        var zero = speakers.Single(s => s.Id == "speaker_0");
        Assert.Equal((LessonRole.Teacher, true), (one.Role, one.RoleGiven));
        Assert.Equal((LessonRole.Student, false), (zero.Role, zero.RoleGiven));
        Assert.All(after.Recordings.Single().Lines.Where(l => l.Speaker == "speaker_1"), l => Assert.Equal(LessonRole.Teacher, l.Role));
    }

    [Fact]
    public async Task Wrong_requests_are_refused_and_a_day_without_a_lesson_is_not_found()
    {
        PutLesson();
        var controller = Controller();

        Assert.IsType<BadRequestObjectResult>((await controller.Get("06.08.2025")).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.Get("2025-01-01")).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.SetRole(
            "2025-08-06", new LessonSpeakerRoleBody { Recording = "*", Speaker = "speaker_0", Role = "boss" })).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.SetRole(
            "2025-08-06", new LessonSpeakerRoleBody { Recording = "no such recording", Speaker = "speaker_0", Role = "teacher" })).Result);
        Assert.IsType<OkObjectResult>((await controller.SetRole(
            "2025-08-06", new LessonSpeakerRoleBody { Recording = "*", Speaker = "speaker_0", Role = "teacher" })).Result);
    }
}
