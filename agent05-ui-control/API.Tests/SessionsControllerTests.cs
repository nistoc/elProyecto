using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Features.Sessions.Application;
using XtractManager.Features.Sessions.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// Lessons list with the runs of every track, and the start of a run from a track. All names here are made up.
/// </summary>
public sealed class SessionsControllerTests : IDisposable
{
    private const string Day = "2026.10.23";
    private const string Period = Day + "/2026-10-23 19.01.16 Maria Lopez's Zoom Meeting";
    private const string TeacherTrack = Period + "/Audio Record/audioMariaLopez11844996676.m4a";
    private const string StudentTrack = Period + "/Audio Record/audioОльгаИва21844996676.m4a";
    private const string MixedTrack = Period + "/audio1844996676.m4a";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "sessions_" + Guid.NewGuid().ToString("N"));
    private readonly string _recordings;
    private readonly string _workspace;

    public SessionsControllerTests()
    {
        _recordings = Path.Combine(_base, "recordings");
        _workspace = Path.Combine(_base, "runtime");
        Directory.CreateDirectory(_recordings);
        Directory.CreateDirectory(_workspace);
        PutRecording(MixedTrack, "mixed audio bytes");
        PutRecording(TeacherTrack, "teacher voice");
        PutRecording(StudentTrack, "student voice, longer");
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, true); } catch { /* ignore */ }
    }

    private string PutRecording(string relativePath, string content)
    {
        var full = Path.Combine(_recordings, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return full;
    }

    private string PutJobFile(string jobId, string name, string content)
    {
        var dir = Path.Combine(_workspace, jobId);
        Directory.CreateDirectory(dir);
        var full = Path.Combine(dir, name);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return full;
    }

    private (SessionsController Controller, InMemoryJobStore Memory, QueueSpy Queue) Create(
        bool recordingsConfigured = true,
        string? estimate = null,
        string? participantModel = null,
        string? participantEstimate = null)
    {
        var settings = new Dictionary<string, string?> { ["Jobs:WorkspacePath"] = _workspace };
        if (recordingsConfigured)
            settings[RecordingsCatalog.RootPathKey] = _recordings;
        if (estimate != null)
            settings[SessionsController.EstimateKey] = estimate;
        if (participantModel != null)
            settings[SessionsController.ParticipantTrackModelKey] = participantModel;
        if (participantEstimate != null)
            settings[SessionsController.ParticipantTrackEstimateKey] = participantEstimate;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var env = new FakeHostEnvironment();
        var workspace = new JobWorkspace(config, env, NullLogger<JobWorkspace>.Instance);
        var memory = new InMemoryJobStore();
        var store = new WorkspaceAwareJobStore(memory, workspace, NullLogger<WorkspaceAwareJobStore>.Instance);
        var queue = new QueueSpy(_workspace);
        var catalog = new RecordingsCatalog(config, env, NullLogger<RecordingsCatalog>.Instance);
        var controller = new SessionsController(
            catalog, store, workspace, queue, config, NullLogger<SessionsController>.Instance);
        return (controller, memory, queue);
    }

    private static SessionsResponse Listed(ActionResult<SessionsResponse> result) =>
        Assert.IsType<SessionsResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static StartTrackRunBody Body(string? track, string? settingsJson = null) => new()
    {
        Track = track,
        Settings = settingsJson == null ? null : JsonDocument.Parse(settingsJson).RootElement.Clone(),
    };

    private List<string> RecordingsSnapshot() =>
        Directory.EnumerateFileSystemEntries(_recordings, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => File.Exists(p)
                ? $"{p}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p).Ticks}"
                : p)
            .ToList();

    [Fact]
    public async Task List_without_a_recordings_folder_is_switched_off()
    {
        var (controller, _, _) = Create(recordingsConfigured: false);

        var response = Listed(await controller.List());

        Assert.False(response.Enabled);
        Assert.Null(response.RootPath);
        Assert.Empty(response.Sessions);
        Assert.Equal(SessionsController.DefaultEstimateUsdPerMinute, response.EstimateUsdPerMinute);
    }

    [Fact]
    public async Task List_tells_the_model_participant_tracks_go_to_and_its_price()
    {
        var (byDefault, _, _) = Create();
        var (configured, _, _) = Create(participantModel: " whisper-1 ", participantEstimate: "0.02");

        var defaults = Listed(await byDefault.List());
        var own = Listed(await configured.List());

        Assert.Equal("gpt-transcribe", defaults.ParticipantTrackModel);
        Assert.Equal(SessionsController.DefaultParticipantTrackEstimateUsdPerMinute, defaults.ParticipantTrackEstimateUsdPerMinute);
        Assert.Equal("whisper-1", own.ParticipantTrackModel);
        Assert.Equal(0.02, own.ParticipantTrackEstimateUsdPerMinute);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a model")]
    public async Task List_names_no_model_of_participant_tracks_when_the_key_is_empty_or_not_a_model(string configured)
    {
        var (controller, _, _) = Create(participantModel: configured);

        Assert.Null(Listed(await controller.List()).ParticipantTrackModel);
    }

    [Fact]
    public async Task A_run_tells_how_much_of_its_speech_is_Spanish_and_how_much_Russian()
    {
        // Two runs of the student's track: one with chunk answers, one that has answered nothing yet.
        var source = JsonSerializer.Serialize(new { schema_version = 1, track = StudentTrack, file_name = "gone.m4a", size_bytes = 5 });
        PutJobFile("jobanswered1", TrackSourceFile.FileName, source);
        Directory.CreateDirectory(Path.Combine(_workspace, "jobanswered1", "chunks_json"));
        PutJobFile("jobanswered1", Path.Combine("chunks_json", "audio_part_000.json"),
            "{\"segments\":[{\"start\":0,\"end\":30,\"text\":\"Hola, buenos días.\"},{\"start\":40,\"end\":50,\"text\":\"Привет, как дела?\"}]}");
        PutJobFile("jobwaiting01", TrackSourceFile.FileName, source);

        var (controller, _, _) = Create();
        var period = Assert.Single(Assert.Single(Listed(await controller.List()).Sessions).Periods);
        var runs = period.Tracks.Single(t => t.Id == StudentTrack).Runs;

        var answered = runs.Single(r => r.JobId == "jobanswered1").Share;
        Assert.NotNull(answered);
        Assert.Equal(40, answered.SpeechSeconds, 6);
        Assert.Equal(30, answered.SpanishSeconds, 6);
        Assert.Equal(10, answered.RussianSeconds, 6);
        Assert.Null(runs.Single(r => r.JobId == "jobwaiting01").Share);
    }

    [Fact]
    public async Task List_shows_lessons_with_the_jobs_that_transcribed_each_track()
    {
        // A job made by a plain upload of the teacher's file: tied by the name and size of the audio.
        var teacherBytes = new FileInfo(Path.Combine(_recordings, TeacherTrack.Replace('/', Path.DirectorySeparatorChar))).Length;
        PutJobFile("jobteacher01", "audioMariaLopez11844996676.m4a", new string('x', (int)teacherBytes));
        PutJobFile("jobteacher01", "transcript.md", "- 1.00 speaker_0: \"hola\"");
        PutJobFile("jobteacher01", "transcript_fixed_1.md", "- 1.00 speaker_0: \"hola\"");
        // Same name, another size: some other recording, not this track.
        PutJobFile("jobother0001", "audioMariaLopez11844996676.m4a", "x");
        // A job started from the lessons list: tied by track_source.json even with no audio left in the folder.
        PutJobFile("jobstudent01", TrackSourceFile.FileName,
            JsonSerializer.Serialize(new { schema_version = 1, track = StudentTrack, file_name = "gone.m4a", size_bytes = 5 }));
        // ... and the transcriber recorded what it ran with.
        PutJobFile("jobstudent01", JobRunSettingsReader.RunSettingsFileName, JsonSerializer.Serialize(new
        {
            schema_version = 1,
            model = "gpt-4o-transcribe-diarize",
            silence_compress_enabled = true,
            silence_keep_sec = 1.0,
            from_job_settings = new[] { "silence_keep_sec" },
        }));
        // A dialogue merged from the two runs.
        PutJobFile("jobdialogue1", DialogueManifestFile.FileName, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            title = "Lesson of the 23rd",
            createdAt = "2026-10-24T10:00:00Z",
            tracks = new[]
            {
                new { jobId = "jobteacher01", speaker = "Maria" },
                new { jobId = "jobstudent01", speaker = "Olga" },
            },
            merges = new[] { new { file = "transcript_dialogue_1.md", entries = 3 } },
        }));
        // A dialogue of some other jobs does not belong to this period.
        PutJobFile("jobdialogue2", DialogueManifestFile.FileName, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            title = "Another lesson",
            tracks = new[] { new { jobId = "elsewhere001", speaker = "A" } },
        }));

        var (controller, _, _) = Create(estimate: "0.02");
        var response = Listed(await controller.List());

        Assert.True(response.Enabled);
        Assert.Equal(Path.GetFullPath(_recordings), response.RootPath);
        Assert.Equal(0.02, response.EstimateUsdPerMinute);

        var session = Assert.Single(response.Sessions);
        Assert.Equal(Day, session.Id);
        var period = Assert.Single(session.Periods);
        Assert.Equal(Period, period.Id);

        var mixed = period.Tracks.Single(t => t.Id == MixedTrack);
        Assert.Empty(mixed.Runs);

        var teacherRun = Assert.Single(period.Tracks.Single(t => t.Id == TeacherTrack).Runs);
        Assert.Equal("jobteacher01", teacherRun.JobId);
        Assert.Equal("completed", teacherRun.Status);
        Assert.True(teacherRun.HasTranscript);
        Assert.True(teacherRun.HasRefined);
        Assert.Null(teacherRun.Settings); // an upload from before the record: its folder tells nothing

        var studentRun = Assert.Single(period.Tracks.Single(t => t.Id == StudentTrack).Runs);
        Assert.Equal("jobstudent01", studentRun.JobId);
        Assert.False(studentRun.HasTranscript);
        Assert.False(studentRun.HasRefined);
        Assert.NotNull(studentRun.Settings);
        Assert.Equal(JobRunSettings.SourceRun, studentRun.Settings.Source);
        Assert.Equal("gpt-4o-transcribe-diarize", studentRun.Settings.Model);
        Assert.True(studentRun.Settings.SilenceCompress);
        Assert.Equal(1.0, studentRun.Settings.SilenceKeepSec);
        Assert.Equal(new[] { "silence_keep_sec" }, studentRun.Settings.FromJobSettings);

        var dialogue = Assert.Single(period.Dialogues);
        Assert.Equal("jobdialogue1", dialogue.JobId);
        Assert.Equal("Lesson of the 23rd", dialogue.Title);
        Assert.Equal(new[] { "jobteacher01", "jobstudent01" }, dialogue.TrackJobIds);
        Assert.Equal(1, dialogue.Merges);
    }

    [Fact]
    public async Task Upload_of_a_track_under_another_name_is_found_by_its_size_and_first_bytes()
    {
        // The operator uploaded a copy of the student track by hand, under a name of their own.
        PutJobFile("jobrenamed01", "2026-10-23 student track.m4a", "student voice, longer");
        // Same number of bytes, other content: another recording.
        PutJobFile("jobsamesize1", "something else.m4a", "STUDENT VOICE, LONGER");

        var (controller, _, _) = Create();
        var period = Assert.Single(Assert.Single(Listed(await controller.List()).Sessions).Periods);

        Assert.Equal("jobrenamed01", Assert.Single(period.Tracks.Single(t => t.Id == StudentTrack).Runs).JobId);
        Assert.Empty(period.Tracks.Single(t => t.Id == TeacherTrack).Runs);
        Assert.Empty(period.Tracks.Single(t => t.Id == MixedTrack).Runs);
    }

    [Fact]
    public async Task Run_of_a_moved_track_is_still_found_by_the_name_and_size_of_its_file()
    {
        var (controller, _, _) = Create();
        var started = Assert.IsType<AcceptedResult>((await controller.StartRun(Body(StudentTrack))).Result);
        var jobId = Assert.IsType<CreateJobResponse>(started.Value).JobId;

        // The operator moves the period into another day folder: the id of the track changes.
        var movedDay = Path.Combine(_recordings, "2026.10.24");
        Directory.CreateDirectory(movedDay);
        Directory.Move(
            Path.Combine(_recordings, Period.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(movedDay, "2026-10-23 19.01.16 Maria Lopez's Zoom Meeting"));

        var response = Listed(await controller.List());

        var tracks = response.Sessions.SelectMany(s => s.Periods).SelectMany(p => p.Tracks).ToList();
        var student = tracks.Single(t => t.Participant == "Ольга Ива");
        Assert.StartsWith("2026.10.24/", student.Id);
        Assert.Equal(jobId, Assert.Single(student.Runs).JobId);
        Assert.All(tracks.Where(t => t != student), t => Assert.Empty(t.Runs));
    }

    [Fact]
    public async Task StartRun_copies_the_track_into_a_new_job_and_queues_it()
    {
        var before = RecordingsSnapshot();
        var (controller, memory, queue) = Create();

        var result = await controller.StartRun(Body(StudentTrack, """{"silence_keep_sec":1.0,"silence_compress_enabled":true}"""));

        var jobId = Assert.IsType<CreateJobResponse>(Assert.IsType<AcceptedResult>(result.Result).Value).JobId;
        Assert.Equal(new[] { jobId }, queue.Jobs);

        var jobDir = Path.Combine(_workspace, jobId);
        Assert.Equal("student voice, longer", File.ReadAllText(Path.Combine(jobDir, "audioОльгаИва21844996676.m4a")));
        Assert.Equal(
            new[] { "audioОльгаИва21844996676.m4a", "job_settings.json", "track_source.json" },
            queue.FilesSeenWhenQueued.OrderBy(n => n, StringComparer.Ordinal));

        var source = TrackSourceFile.TryLoad(jobDir);
        Assert.NotNull(source);
        Assert.Equal(StudentTrack, source!.Track);
        Assert.Equal("audioОльгаИва21844996676.m4a", source.FileName);
        Assert.Equal(new FileInfo(Path.Combine(jobDir, source.FileName)).Length, source.SizeBytes);
        Assert.Equal(Day, source.Session);
        Assert.Equal(Period, source.Period);
        Assert.Equal(RecordingsScanner.KindParticipant, source.Kind);
        Assert.Equal("Ольга Ива", source.Participant);
        Assert.Contains("\"schema_version\": 1", File.ReadAllText(Path.Combine(jobDir, TrackSourceFile.FileName)));

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(jobDir, JobSettingsDocument.FileName)));
        var transcription = settings.RootElement.GetProperty("transcription");
        Assert.Equal(1.0, transcription.GetProperty("silence_keep_sec").GetDouble());
        Assert.True(transcription.GetProperty("silence_compress_enabled").GetBoolean());
        // The run named no model: a participant track gets the one of participant tracks.
        Assert.Equal("gpt-transcribe", transcription.GetProperty("model").GetString());

        var snapshot = await memory.GetAsync(jobId);
        Assert.Equal("audioОльгаИва21844996676.m4a", snapshot!.OriginalFilename);
        Assert.Equal(new[] { "lesson:2026-10-23", "track:participant" }, snapshot.Tags);

        // The recordings folder is only read.
        Assert.Equal(before, RecordingsSnapshot());

        // The new job is listed as a run of that track and of no other.
        var period = Assert.Single(Assert.Single(Listed(await controller.List()).Sessions).Periods);
        Assert.Equal(jobId, Assert.Single(period.Tracks.Single(t => t.Id == StudentTrack).Runs).JobId);
        Assert.Empty(period.Tracks.Single(t => t.Id == TeacherTrack).Runs);
        Assert.Empty(period.Tracks.Single(t => t.Id == MixedTrack).Runs);
    }

    [Fact]
    public async Task StartRun_of_the_mixed_track_without_settings_writes_no_settings_file()
    {
        var (controller, _, queue) = Create();

        var result = await controller.StartRun(Body(MixedTrack));

        var jobId = Assert.IsType<CreateJobResponse>(Assert.IsType<AcceptedResult>(result.Result).Value).JobId;
        Assert.False(File.Exists(Path.Combine(_workspace, jobId, JobSettingsDocument.FileName)));
        Assert.Equal("mixed audio bytes", File.ReadAllText(Path.Combine(_workspace, jobId, "audio1844996676.m4a")));
        Assert.Single(queue.Jobs);
    }

    private async Task<JsonElement?> SettingsOfStartedRunAsync(SessionsController controller, string track, string? settingsJson = null)
    {
        var result = await controller.StartRun(Body(track, settingsJson));
        var jobId = Assert.IsType<CreateJobResponse>(Assert.IsType<AcceptedResult>(result.Result).Value).JobId;
        var path = Path.Combine(_workspace, jobId, JobSettingsDocument.FileName);
        if (!File.Exists(path))
            return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("transcription").Clone();
    }

    [Fact]
    public async Task A_participant_track_started_without_a_model_goes_to_the_model_of_participant_tracks()
    {
        var (controller, _, _) = Create();

        var transcription = await SettingsOfStartedRunAsync(controller, TeacherTrack);

        Assert.NotNull(transcription);
        Assert.Equal("gpt-transcribe", transcription.Value.GetProperty("model").GetString());
        Assert.Single(transcription.Value.EnumerateObject());
    }

    [Fact]
    public async Task A_participant_track_keeps_the_model_its_run_names()
    {
        var (controller, _, _) = Create();

        var transcription = await SettingsOfStartedRunAsync(
            controller, TeacherTrack, """{"model":"gpt-4o-transcribe-diarize","silence_keep_sec":0.5}""");

        Assert.NotNull(transcription);
        Assert.Equal("gpt-4o-transcribe-diarize", transcription.Value.GetProperty("model").GetString());
        Assert.Equal(0.5, transcription.Value.GetProperty("silence_keep_sec").GetDouble());
    }

    [Fact]
    public async Task The_mixed_track_keeps_the_shared_model_whatever_its_settings()
    {
        var (controller, _, _) = Create();

        var transcription = await SettingsOfStartedRunAsync(controller, MixedTrack, """{"silence_keep_sec":0.5}""");

        Assert.NotNull(transcription);
        Assert.False(transcription.Value.TryGetProperty("model", out _));
    }

    [Fact]
    public async Task The_model_of_participant_tracks_is_the_one_the_config_names()
    {
        var (controller, _, _) = Create(participantModel: "whisper-1");

        var transcription = await SettingsOfStartedRunAsync(controller, StudentTrack);

        Assert.NotNull(transcription);
        Assert.Equal("whisper-1", transcription.Value.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a model")]
    public async Task With_no_model_of_participant_tracks_a_participant_track_stays_on_the_shared_model(string configured)
    {
        var (controller, _, _) = Create(participantModel: configured);

        Assert.Null(await SettingsOfStartedRunAsync(controller, StudentTrack));
    }

    [Theory]
    [InlineData("2026.10.23/nope.m4a")]
    [InlineData("../outside.m4a")]
    [InlineData("2026.10.23/../../outside.m4a")]
    [InlineData("2026.10.23/2026-10-23 19.01.16 Maria Lopez's Zoom Meeting/recording.conf")]
    [InlineData("C:/Windows/win.ini")]
    public async Task StartRun_takes_only_a_track_the_scan_lists(string track)
    {
        File.WriteAllText(Path.Combine(_base, "outside.m4a"), "not a recording");
        PutRecording(Period + "/recording.conf", "conf");
        var (controller, memory, queue) = Create();

        var result = await controller.StartRun(Body(track));

        var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Contains("is not in the recordings folder", Assert.IsType<ProblemDetails>(notFound.Value).Detail);
        Assert.Empty(queue.Jobs);
        Assert.Empty(await memory.ListAsync(new JobListFilter()));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task StartRun_needs_a_track(string? track)
    {
        var (controller, _, queue) = Create();

        var result = await controller.StartRun(Body(track));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(queue.Jobs);
    }

    [Theory]
    [InlineData("""{"silence_keep_sec":99}""", "silence_keep_sec")]
    [InlineData("""{"openai_api_key":"x"}""", "is not a setting of a job")]
    [InlineData("""[1]""", "settings must be a JSON object")]
    public async Task StartRun_with_bad_settings_starts_nothing(string settingsJson, string expectedReason)
    {
        var (controller, memory, queue) = Create();

        var result = await controller.StartRun(Body(StudentTrack, settingsJson));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(expectedReason, Assert.IsType<ProblemDetails>(bad.Value).Detail);
        Assert.Empty(queue.Jobs);
        Assert.Empty(await memory.ListAsync(new JobListFilter()));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_workspace));
    }

    [Fact]
    public async Task StartRun_without_a_recordings_folder_is_refused()
    {
        var (controller, _, queue) = Create(recordingsConfigured: false);

        var result = await controller.StartRun(Body(StudentTrack));

        Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Empty(queue.Jobs);
    }

    /// <summary>
    /// Looks into the job folder of its own workspace. It used to walk the temp folder all test classes share;
    /// a folder another class deleted meanwhile failed that walk, and with it a test of this class now and then.
    /// </summary>
    private sealed class QueueSpy(string workspace) : IPipelineQueue
    {
        public List<string> Jobs { get; } = new();
        public List<string> FilesSeenWhenQueued { get; } = new();

        public void Enqueue(string jobId)
        {
            Jobs.Add(jobId);
            var dir = Path.Combine(workspace, jobId);
            if (Directory.Exists(dir))
                FilesSeenWhenQueued.AddRange(Directory.EnumerateFiles(dir).Select(Path.GetFileName)!);
        }

        public void EnqueueRefiner(string jobId, string? inputFile = null, bool lessonPrompt = false) { }
    }
}
