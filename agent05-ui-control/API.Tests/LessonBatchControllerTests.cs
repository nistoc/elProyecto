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
/// A batch of days against folders on disk: the plan it answers with, what a start writes, and that a
/// second call does only what is still missing. All names here are made up.
/// </summary>
public sealed class LessonBatchControllerTests : IDisposable
{
    private const string MixedDay = "2025.08.06";
    private const string MixedFirst = MixedDay + "/2025-08-06 19.01.16 Ana Ruiz's Zoom Meeting/audio1844990001.m4a";
    private const string MixedSecond = MixedDay + "/2025-08-06 19.45.02 Ana Ruiz's Zoom Meeting/audio1844990002.m4a";

    private const string TwiceDay = "2025.08.13";
    private const string TwiceFirst = TwiceDay + "/2025-08-13 19.00.00 Ana Ruiz's Zoom Meeting/audio1844990003.m4a";
    private const string TwiceCopy = TwiceDay + "/2025-08-13 19.00.00 Ana Ruiz's Zoom Meeting copy/audio1844990003.m4a";

    private const string VoicesDay = "2026.10.23";
    private const string VoicesPeriod = VoicesDay + "/2026-10-23 19.01.16 Ana Ruiz's Zoom Meeting";
    private const string VoiceOne = VoicesPeriod + "/Audio Record/audioAnaRuiz11844990004.m4a";
    private const string VoiceTwo = VoicesPeriod + "/Audio Record/audioLuisVega21844990004.m4a";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "batch_" + Guid.NewGuid().ToString("N"));
    private readonly string _recordings;
    private readonly string _workspace;

    public LessonBatchControllerTests()
    {
        _recordings = Path.Combine(_base, "recordings");
        _workspace = Path.Combine(_base, "runtime");
        Directory.CreateDirectory(_recordings);
        Directory.CreateDirectory(_workspace);
        PutRecording(MixedFirst, "the first forty minutes");
        PutRecording(MixedSecond, "the last twenty minutes, a little longer");
        PutRecording(TwiceFirst, "one recording that lies twice");
        PutRecording(TwiceCopy, "one recording that lies twice");
        PutRecording(VoiceOne, "the first voice");
        PutRecording(VoiceTwo, "the second voice, longer");
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, true); } catch { /* ignore */ }
    }

    private void PutRecording(string relativePath, string content)
    {
        var full = Path.Combine(_recordings, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    /// <summary>A job folder as a finished run of the track leaves it: the copy of the audio and a transcript.</summary>
    private void PutFinishedRun(string jobId, string track)
    {
        var dir = Path.Combine(_workspace, jobId);
        Directory.CreateDirectory(dir);
        File.Copy(
            Path.Combine(_recordings, track.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(dir, Path.GetFileName(track)));
        File.WriteAllText(Path.Combine(dir, "transcript.md"), "- 00:00:01 speaker_0: hola", new UTF8Encoding(false));
    }

    private (LessonBatchController Controller, QueueSpy Queue, DialogueSpy Dialogues) Create(double minutesPerTrack = 10)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jobs:WorkspacePath"] = _workspace,
            [RecordingsCatalog.RootPathKey] = _recordings,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var env = new FakeHostEnvironment();
        var workspace = new JobWorkspace(config, env, NullLogger<JobWorkspace>.Instance);
        var store = new WorkspaceAwareJobStore(new InMemoryJobStore(), workspace, NullLogger<WorkspaceAwareJobStore>.Instance);
        var queue = new QueueSpy();
        var dialogues = new DialogueSpy();
        var catalog = new TimedCatalog(new RecordingsCatalog(config, env, NullLogger<RecordingsCatalog>.Instance), minutesPerTrack * 60);
        var controller = new LessonBatchController(
            catalog, store, workspace, queue, dialogues, config, NullLogger<LessonBatchController>.Instance);
        return (controller, queue, dialogues);
    }

    private static LessonBatchBody Body(bool? dryRun, params string[] dates) => new() { Dates = dates.ToList(), DryRun = dryRun };

    private static LessonBatchResponse Planned(ActionResult<LessonBatchResponse> result) =>
        Assert.IsType<LessonBatchResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static LessonBatchResponse Started(ActionResult<LessonBatchResponse> result) =>
        Assert.IsType<LessonBatchResponse>(Assert.IsType<AcceptedResult>(result.Result).Value);

    private List<string> JobFolders() =>
        Directory.EnumerateDirectories(_workspace).Select(d => Path.GetFileName(d)!).OrderBy(d => d, StringComparer.Ordinal).ToList();

    [Fact]
    public async Task Without_a_word_to_start_the_answer_is_only_the_plan()
    {
        var (controller, queue, _) = Create();

        var response = Planned(await controller.Run(Body(null, "2025-08-06")));

        Assert.True(response.DryRun);
        Assert.Equal(2, response.Plan.Runs);
        Assert.Equal(20, response.Plan.Minutes);
        Assert.Equal(20 * RecordingRunDefaults.PlainMixedEstimateUsdPerMinute, response.Plan.EstimateUsd, 4);
        Assert.All(response.Plan.Lessons.Single().Steps, s => Assert.Equal(RecordingRunDefaults.MixedOnlyTrackModel, s.Model));
        Assert.Empty(response.StartedJobs);
        Assert.Empty(queue.Jobs);
        Assert.Empty(JobFolders());
    }

    [Fact]
    public async Task A_start_queues_a_job_for_every_run_of_the_plan_with_the_model_of_its_track()
    {
        var (controller, queue, _) = Create();

        var response = Started(await controller.Run(Body(false, "2025-08-06")));

        Assert.False(response.DryRun);
        Assert.Equal(2, response.StartedJobs.Count);
        Assert.Equal(response.StartedJobs, queue.Jobs);
        Assert.Equal(response.StartedJobs.OrderBy(j => j, StringComparer.Ordinal), JobFolders());
        Assert.All(response.Plan.Lessons.Single().Steps, s => Assert.Equal(LessonBatchStepKind.Started, s.Kind));
        foreach (var jobId in response.StartedJobs)
        {
            var dir = Path.Combine(_workspace, jobId);
            using var jobSettings = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, JobSettingsDocument.FileName)));
            Assert.Equal("gpt-transcribe", jobSettings.RootElement.GetProperty("transcription").GetProperty("model").GetString());
            var source = TrackSourceFile.TryLoad(dir);
            Assert.NotNull(source);
            Assert.Contains(source!.Track, new[] { MixedFirst, MixedSecond });
        }
    }

    [Fact]
    public async Task A_second_call_starts_nothing_that_is_already_queued()
    {
        var (controller, queue, _) = Create();
        Started(await controller.Run(Body(false, "2025-08-06")));

        var again = Started(await controller.Run(Body(false, "2025-08-06")));

        Assert.Empty(again.StartedJobs);
        Assert.Equal(0, again.Plan.Runs);
        Assert.All(again.Plan.Lessons.Single().Steps, s => Assert.Equal(LessonBatchStepKind.Wait, s.Kind));
        Assert.Equal(LessonStage.Transcribing, again.Plan.Lessons.Single().Stage);
        Assert.Equal(2, queue.Jobs.Count);
        Assert.Equal(2, JobFolders().Count);
    }

    [Fact]
    public async Task The_same_file_lying_twice_in_a_day_is_started_once()
    {
        var (controller, queue, _) = Create();

        var response = Started(await controller.Run(Body(false, "2025-08-13")));

        var steps = response.Plan.Lessons.Single().Steps;
        Assert.Single(steps, s => s.Kind == LessonBatchStepKind.Started);
        Assert.Single(steps, s => s.Kind == LessonBatchStepKind.Duplicate);
        Assert.Single(queue.Jobs);
        Assert.Equal(10, response.Plan.Minutes);
    }

    [Fact]
    public async Task Above_the_named_ceiling_nothing_is_started()
    {
        var (controller, queue, _) = Create();
        var body = Body(false, "2025-08-06");
        body.MaxUsd = 0.1;

        var result = await controller.Run(body);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Contains("nothing was started", Assert.IsType<ProblemDetails>(conflict.Value).Detail);
        Assert.Empty(queue.Jobs);
        Assert.Empty(JobFolders());

        body.MaxUsd = 0.5;
        Assert.Equal(2, Started(await controller.Run(body)).StartedJobs.Count);
    }

    [Fact]
    public async Task The_settings_of_the_call_go_to_every_job_and_a_model_named_there_wins()
    {
        var (controller, _, _) = Create();
        var body = Body(false, "2025-08-06");
        body.Settings = JsonDocument.Parse("""{"model":"other-model","silence_keep_sec":1.0}""").RootElement.Clone();

        var response = Started(await controller.Run(body));

        Assert.Equal(20 * RecordingRunDefaults.EstimateUsdPerMinute, response.Plan.EstimateUsd, 4);
        foreach (var jobId in response.StartedJobs)
        {
            using var jobSettings = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(_workspace, jobId, JobSettingsDocument.FileName)));
            var transcription = jobSettings.RootElement.GetProperty("transcription");
            Assert.Equal("other-model", transcription.GetProperty("model").GetString());
            Assert.Equal(1.0, transcription.GetProperty("silence_keep_sec").GetDouble());
        }
    }

    [Fact]
    public async Task Participant_tracks_that_all_have_a_transcript_are_merged_into_a_dialogue_once_asked_to_start()
    {
        PutFinishedRun("job-voice-one", VoiceOne);
        PutFinishedRun("job-voice-two", VoiceTwo);
        var (controller, queue, dialogues) = Create();

        var plan = Planned(await controller.Run(Body(true, "2026-10-23")));
        Assert.Equal(1, plan.Plan.Dialogues);
        Assert.Equal(0, plan.Plan.Runs);
        Assert.Equal(LessonStage.DialoguePending, plan.Plan.Lessons.Single().Stage);
        Assert.Empty(dialogues.Requests);

        var response = Started(await controller.Run(Body(false, "2026-10-23")));

        var request = Assert.Single(dialogues.Requests);
        Assert.Equal(new[] { "job-voice-one", "job-voice-two" }, request.Tracks.Select(t => t.JobId).OrderBy(j => j, StringComparer.Ordinal));
        Assert.All(request.Tracks, t => Assert.False(string.IsNullOrWhiteSpace(t.Speaker)));
        Assert.Equal(2, request.Tracks.Select(t => t.Speaker).Distinct().Count());
        Assert.Equal(new[] { "dialogue-1" }, response.Dialogues);
        Assert.Single(response.Plan.Lessons.Single().Steps, s => s.Kind == LessonBatchStepKind.Merged);
        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public async Task Dates_are_asked_for_and_checked_and_a_day_without_a_lesson_is_told()
    {
        var (controller, queue, _) = Create();

        Assert.IsType<BadRequestObjectResult>((await controller.Run(null)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Run(Body(false))).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Run(Body(false, "06.08.2025"))).Result);
        var unknown = Started(await controller.Run(Body(false, "2025-01-01")));

        Assert.Equal(new[] { "2025-01-01" }, unknown.Plan.UnknownDates);
        Assert.Empty(unknown.Plan.Lessons);
        Assert.Empty(queue.Jobs);
    }

    /// <summary>The recordings folder with a length given to every track: the files here are no real audio.</summary>
    private sealed class TimedCatalog(IRecordingsCatalog inner, double seconds) : IRecordingsCatalog
    {
        public bool Enabled => inner.Enabled;

        public string? RootPath => inner.RootPath;

        public IReadOnlyList<RecordingSession> Scan() => inner.Scan()
            .Select(s => s with
            {
                Periods = s.Periods
                    .Select(p => p with { Tracks = p.Tracks.Select(t => t with { DurationSec = seconds }).ToList() })
                    .ToList(),
            })
            .ToList();

        public RecordingTrackFile? FindTrack(string? trackId) => inner.FindTrack(trackId);
    }

    private sealed class QueueSpy : IPipelineQueue
    {
        public List<string> Jobs { get; } = new();

        public void Enqueue(string jobId) => Jobs.Add(jobId);

        public void EnqueueRefiner(string jobId, string? inputFile = null, bool lessonPrompt = false) { }
    }

    private sealed class DialogueSpy : IDialogueBuilder
    {
        public List<DialogueCreateRequest> Requests { get; } = new();

        public Task<DialogueBuildResult> CreateAsync(DialogueCreateRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new DialogueBuildResult(
                "dialogue-" + Requests.Count, "transcript_dialogue.md", 0, Array.Empty<DialogueTrackResult>(), Array.Empty<string>()));
        }

        public Task<DialogueBuildResult> RebuildAsync(string dialogueJobId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
