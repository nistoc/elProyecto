using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>Settings of one job given at upload: checked, then written to <c>job_settings.json</c> in the job folder.</summary>
public sealed class JobSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "settings_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void TryBuild_without_settings_writes_no_file()
    {
        Assert.True(JobSettingsDocument.TryBuild(null, out var json, out var error));
        Assert.Null(json);
        Assert.Null(error);

        Assert.True(JobSettingsDocument.TryBuild("  ", out json, out _));
        Assert.Null(json);

        Assert.True(JobSettingsDocument.TryBuild("{}", out json, out _));
        Assert.Null(json);
    }

    [Fact]
    public void TryBuild_wraps_the_settings_into_the_file_the_transcriber_reads()
    {
        Assert.True(JobSettingsDocument.TryBuild(
            """{"silence_keep_sec":1.0,"silence_compress_enabled":true,"model":" whisper-1 "}""", out var json, out var error));

        Assert.Null(error);
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal(1, doc.RootElement.GetProperty("schema_version").GetInt32());
        var transcription = doc.RootElement.GetProperty("transcription");
        Assert.Equal(1.0, transcription.GetProperty("silence_keep_sec").GetDouble());
        Assert.True(transcription.GetProperty("silence_compress_enabled").GetBoolean());
        Assert.Equal("whisper-1", transcription.GetProperty("model").GetString());
        Assert.Equal(3, transcription.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("oops", "settings must be a JSON object")]
    [InlineData("[1]", "settings must be a JSON object")]
    [InlineData("""{"silence_keep_sec":0.01}""", "silence_keep_sec must be a number from 0.05 to 5")]
    [InlineData("""{"silence_keep_sec":9}""", "silence_keep_sec must be a number")]
    [InlineData("""{"silence_keep_sec":"1"}""", "silence_keep_sec must be a number")]
    [InlineData("""{"silence_compress_enabled":1}""", "silence_compress_enabled must be true or false")]
    [InlineData("""{"model":"a b"}""", "model must be a model id")]
    [InlineData("""{"openai_api_key":"x"}""", "settings.openai_api_key is not a setting of a job")]
    [InlineData("""{"target_chunk_mb":1}""", "settings.target_chunk_mb is not a setting of a job")]
    public void TryBuild_refuses_what_is_not_a_job_setting(string formValue, string expectedReason)
    {
        Assert.False(JobSettingsDocument.TryBuild(formValue, out var json, out var error));

        Assert.Null(json);
        Assert.Contains(expectedReason, error);
    }

    [Theory]
    [InlineData(null, """{"model":"gpt-transcribe"}""")]
    [InlineData("  ", """{"model":"gpt-transcribe"}""")]
    [InlineData("""{"silence_keep_sec":1}""", """{"silence_keep_sec":1,"model":"gpt-transcribe"}""")]
    [InlineData("""{"model":"whisper-1"}""", """{"model":"whisper-1"}""")]
    public void A_model_is_added_only_to_settings_that_name_none(string? formValue, string expected)
    {
        Assert.Equal(expected, JobSettingsDocument.WithModelWhenNoneIsNamed(formValue, "gpt-transcribe"));
    }

    [Theory]
    [InlineData("gpt-transcribe", true)]
    [InlineData("gpt-4o-transcribe-diarize", true)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    [InlineData(null, false)]
    public void A_model_id_is_letters_digits_and_a_few_marks(string? model, bool expected)
    {
        Assert.Equal(expected, JobSettingsDocument.IsModelId(model));
    }

    private (JobsController Controller, InMemoryJobStore Store, JobWorkspace Workspace, RecordingQueue Queue) CreateController()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:WorkspacePath"] = _root })
            .Build();
        var workspace = new JobWorkspace(config, new FakeHostEnvironment { ContentRootPath = Path.GetTempPath() }, NullLogger<JobWorkspace>.Instance);
        var store = new InMemoryJobStore();
        var queue = new RecordingQueue(_root);
        var controller = new JobsController(
            store, workspace, queue, new SilentBroadcaster(), new RecordingTranscriptionClient(), NullLogger<JobsController>.Instance);
        return (controller, store, workspace, queue);
    }

    private static IFormFile Upload(string name = "lesson.m4a")
    {
        var bytes = Encoding.UTF8.GetBytes("fake audio");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name);
    }

    [Fact]
    public async Task Create_with_settings_writes_them_into_the_job_folder_before_the_job_is_queued()
    {
        var (controller, _, workspace, queue) = CreateController();

        var result = await controller.Create(Upload(), tags: null, settings: """{"silence_keep_sec":1.0}""");

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var jobId = Assert.IsType<CreateJobResponse>(accepted.Value).JobId;
        Assert.Equal(new[] { jobId }, queue.Jobs);
        var path = Path.Combine(workspace.GetJobDirectoryPath(jobId), "job_settings.json");
        Assert.True(queue.FilesSeenWhenQueued.Contains("job_settings.json"), "the file must be there when the job is queued");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1.0, doc.RootElement.GetProperty("transcription").GetProperty("silence_keep_sec").GetDouble());
    }

    [Fact]
    public async Task Create_without_settings_writes_no_settings_file()
    {
        var (controller, _, workspace, queue) = CreateController();

        var result = await controller.Create(Upload(), tags: null);

        var jobId = Assert.IsType<CreateJobResponse>(Assert.IsType<AcceptedResult>(result.Result).Value).JobId;
        Assert.False(File.Exists(Path.Combine(workspace.GetJobDirectoryPath(jobId), "job_settings.json")));
        Assert.Single(queue.Jobs);
    }

    [Fact]
    public async Task Create_with_settings_it_cannot_take_stores_nothing_and_queues_nothing()
    {
        var (controller, store, _, queue) = CreateController();

        var result = await controller.Create(Upload(), tags: null, settings: """{"silence_keep_sec":60}""");

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("silence_keep_sec", Assert.IsType<ProblemDetails>(bad.Value).Detail);
        Assert.Empty(queue.Jobs);
        Assert.Empty(await store.ListAsync(new JobListFilter()));
        Assert.False(Directory.Exists(_root) && Directory.EnumerateFileSystemEntries(_root).Any());
    }

    /// <summary>
    /// Looks into the job folder of its own workspace. It used to walk the temp folder all test classes share;
    /// a folder another class deleted meanwhile failed that walk ("Access to the path … is denied"), and with it
    /// a test of this class now and then.
    /// </summary>
    private sealed class RecordingQueue(string workspace) : IPipelineQueue
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

        public void EnqueueRefiner(string jobId, string? inputFile = null) { }
    }

    private sealed class SilentBroadcaster : IBroadcaster
    {
        public IDisposable Subscribe(string jobId, System.Threading.Channels.ChannelWriter<BroadcastEvent> writer) => new MemoryStream();
        public void Publish(string jobId, BroadcastEvent evt) { }
    }
}
