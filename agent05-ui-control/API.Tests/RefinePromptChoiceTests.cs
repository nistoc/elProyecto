using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Features.Sessions.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// A refine run goes with the prompt the operator picks: the refiner's lesson prompt with the check of edits,
/// or the settings saved on the Refiner tab.
/// </summary>
public sealed class RefinePromptChoiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "refine_prompt_" + Guid.NewGuid().ToString("N"));

    public RefinePromptChoiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    // ---------- what a run is asked with ----------

    private static readonly RefinerSettings SavedForAnotherMaterial =
        new("OWN TEMPLATE {context} {batch}", BatchSize: 10, ContextLines: 5, Model: "gpt-4o", SystemPrompt: "OWN SYSTEM PROMPT");

    [Fact]
    public void The_lesson_prompt_takes_nothing_from_the_saved_settings_and_asks_for_the_check_of_edits()
    {
        var plan = RefinerRunPlan.Choose(SavedForAnotherMaterial, lessonPrompt: true);

        Assert.Null(plan.PromptFile); // built-in template
        Assert.Null(plan.SystemPrompt); // built-in system prompt
        Assert.Null(plan.Model); // the refiner's default model
        Assert.Equal(IRefinerSettingsStore.DefaultBatchSize, plan.BatchSize);
        Assert.Equal(IRefinerSettingsStore.DefaultContextLines, plan.ContextLines);
        Assert.True(plan.RespellingOnly);
    }

    [Fact]
    public void The_saved_settings_go_as_they_are_and_the_lines_are_the_models()
    {
        var plan = RefinerRunPlan.Choose(SavedForAnotherMaterial, lessonPrompt: false);

        Assert.Equal(IRefinerSettingsStore.PromptFileName, plan.PromptFile);
        Assert.Equal("OWN SYSTEM PROMPT", plan.SystemPrompt);
        Assert.Equal("gpt-4o", plan.Model);
        Assert.Equal(10, plan.BatchSize);
        Assert.Equal(5, plan.ContextLines);
        Assert.False(plan.RespellingOnly);
    }

    [Fact]
    public void Saved_settings_that_name_nothing_fall_back_to_the_defaults()
    {
        var plan = RefinerRunPlan.Choose(new RefinerSettings(null, null, null, null), lessonPrompt: false);

        Assert.Null(plan.PromptFile);
        Assert.Null(plan.SystemPrompt);
        Assert.Equal(IRefinerSettingsStore.DefaultBatchSize, plan.BatchSize);
        Assert.Equal(IRefinerSettingsStore.DefaultContextLines, plan.ContextLines);
        Assert.False(plan.RespellingOnly);
    }

    [Theory]
    [InlineData("lesson", true)]
    [InlineData(" Lesson ", true)]
    [InlineData("saved", false)]
    [InlineData("", false)] // a client from before the choice
    [InlineData(null, false)]
    public void The_prompt_is_named_by_one_of_two_words(string? word, bool lesson)
    {
        Assert.Equal(lesson, RefinerRunPlan.TryParsePrompt(word));
    }

    [Fact]
    public void Another_word_names_no_prompt()
    {
        Assert.Null(RefinerRunPlan.TryParsePrompt("interview"));
    }

    // ---------- the start of a run ----------

    private sealed class QueueSpy : IPipelineQueue
    {
        public List<(string JobId, string? InputFile, bool LessonPrompt)> Refines { get; } = new();
        public void Enqueue(string jobId) { }
        public void EnqueueRefiner(string jobId, string? inputFile = null, bool lessonPrompt = false) =>
            Refines.Add((jobId, inputFile, lessonPrompt));
    }

    private sealed class SilentBroadcaster : IBroadcaster
    {
        public IDisposable Subscribe(string jobId, System.Threading.Channels.ChannelWriter<BroadcastEvent> writer) => new MemoryStream();
        public void Publish(string jobId, BroadcastEvent evt) { }
    }

    private async Task<(JobsController Controller, WorkspaceAwareJobStore Store, QueueSpy Queue, string JobId)> JobWithTranscriptAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:WorkspacePath"] = _root })
            .Build();
        var workspace = new JobWorkspace(config, new FakeHostEnvironment { ContentRootPath = Path.GetTempPath() }, NullLogger<JobWorkspace>.Instance);
        var store = new WorkspaceAwareJobStore(new InMemoryJobStore(), workspace, NullLogger<WorkspaceAwareJobStore>.Instance);
        var queue = new QueueSpy();
        var controller = new JobsController(
            store, workspace, queue, new SilentBroadcaster(), new RecordingTranscriptionClient(), NullLogger<JobsController>.Instance);

        var jobId = await store.CreateAsync(new JobCreateInput("lesson.m4a"));
        await store.UpdateAsync(jobId, s => { s.Phase = "awaiting_refiner"; s.Status = "done"; });
        Put(jobId, "transcript.md", "- 1.00 speaker_0: \"hola\"");
        return (controller, store, queue, jobId);
    }

    private void Put(string jobId, string name, string content)
    {
        var dir = Path.Combine(_root, jobId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), content, new UTF8Encoding(false));
    }

    [Fact]
    public async Task A_run_started_with_the_lesson_prompt_is_queued_with_it()
    {
        var (controller, _, queue, jobId) = await JobWithTranscriptAsync();

        var result = await controller.StartRefine(jobId, new RefineActionRequest { InputFile = "transcript.md", Prompt = "lesson" });

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal((jobId, "transcript.md", true), Assert.Single(queue.Refines));
    }

    [Theory]
    [InlineData("saved")]
    [InlineData(null)]
    public async Task A_run_started_without_naming_the_lesson_prompt_goes_with_the_saved_settings(string? prompt)
    {
        var (controller, _, queue, jobId) = await JobWithTranscriptAsync();

        var result = await controller.StartRefine(jobId, new RefineActionRequest { Prompt = prompt });

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal((jobId, null, false), Assert.Single(queue.Refines));
    }

    [Fact]
    public async Task A_prompt_that_is_neither_is_refused_and_nothing_is_queued()
    {
        var (controller, store, queue, jobId) = await JobWithTranscriptAsync();

        var result = await controller.StartRefine(jobId, new RefineActionRequest { Prompt = "interview" });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(queue.Refines);
        Assert.Equal("awaiting_refiner", (await store.GetAsync(jobId))!.Phase);
    }

    // ---------- which job holds a lesson ----------

    [Fact]
    public async Task A_job_started_from_the_lessons_list_and_a_dialogue_hold_a_lesson_and_a_plain_upload_does_not()
    {
        var (_, store, _, plain) = await JobWithTranscriptAsync();
        var fromLessons = await store.CreateAsync(new JobCreateInput("track.m4a"));
        Put(fromLessons, TrackSourceFile.FileName, "{\"schema_version\":1,\"track\":\"2026.10.23/a/audio1.m4a\"}");
        var dialogue = await store.CreateAsync(new JobCreateInput("dialogue"));
        Put(dialogue, DialogueManifestFile.FileName, "{\"schemaVersion\":1,\"tracks\":[]}");

        Assert.False((await store.GetAsync(plain))!.Lesson);
        Assert.True((await store.GetAsync(fromLessons))!.Lesson);
        Assert.True((await store.GetAsync(dialogue))!.Lesson);
    }
}
