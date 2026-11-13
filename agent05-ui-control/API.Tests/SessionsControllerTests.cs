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
        string? estimate = null)
    {
        var settings = new Dictionary<string, string?> { ["Jobs:WorkspacePath"] = _workspace };
        if (recordingsConfigured)
            settings[RecordingsCatalog.RootPathKey] = _recordings;
        if (estimate != null)
            settings[SessionsController.EstimateKey] = estimate;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var env = new FakeHostEnvironment();
        var workspace = new JobWorkspace(config, env, NullLogger<JobWorkspace>.Instance);
        var memory = new InMemoryJobStore();
        var store = new WorkspaceAwareJobStore(memory, workspace, NullLogger<WorkspaceAwareJobStore>.Instance);
        var queue = new QueueSpy();
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

        var studentRun = Assert.Single(period.Tracks.Single(t => t.Id == StudentTrack).Runs);
        Assert.Equal("jobstudent01", studentRun.JobId);
        Assert.False(studentRun.HasTranscript);
        Assert.False(studentRun.HasRefined);

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
        Assert.Equal(1.0, settings.RootElement.GetProperty("transcription").GetProperty("silence_keep_sec").GetDouble());

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
    public async Task StartRun_without_settings_writes_no_settings_file()
    {
        var (controller, _, queue) = Create();

        var result = await controller.StartRun(Body(MixedTrack));

        var jobId = Assert.IsType<CreateJobResponse>(Assert.IsType<AcceptedResult>(result.Result).Value).JobId;
        Assert.False(File.Exists(Path.Combine(_workspace, jobId, JobSettingsDocument.FileName)));
        Assert.Equal("mixed audio bytes", File.ReadAllText(Path.Combine(_workspace, jobId, "audio1844996676.m4a")));
        Assert.Single(queue.Jobs);
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

    private sealed class QueueSpy : IPipelineQueue
    {
        public List<string> Jobs { get; } = new();
        public List<string> FilesSeenWhenQueued { get; } = new();

        public void Enqueue(string jobId)
        {
            Jobs.Add(jobId);
            var dir = Directory.EnumerateDirectories(Path.Combine(Path.GetTempPath(), "XtractManagerTests"), jobId, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (dir != null)
                FilesSeenWhenQueued.AddRange(Directory.EnumerateFiles(dir).Select(Path.GetFileName)!);
        }

        public void EnqueueRefiner(string jobId, string? inputFile = null) { }
    }
}
