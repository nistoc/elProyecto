using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// A job the service did not create in this run of the process — its folder was found on disk, as every job is
/// after a restart — keeps what is done to it, so its phase follows a refine run.
/// </summary>
public sealed class JobFoundOnDiskUpdateTests : IDisposable
{
    private const string JobId = "a1b2c3d4e5f6";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "job_on_disk_" + Guid.NewGuid().ToString("N"));

    public JobFoundOnDiskUpdateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    private void Put(string name, string content)
    {
        var path = Path.Combine(_root, JobId, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    /// <summary>A folder as a finished transcription leaves it, and a store that has never heard of the job.</summary>
    private (WorkspaceAwareJobStore Store, JobWorkspace Workspace) StoreOverFolder(string phase = "awaiting_refiner", string status = "done")
    {
        Put("transcript.md", "- 1.00 speaker_0: \"hola\"");
        Put(JobSnapshotDiskEnricher.XtractUiStateFileName, $"{{\"phase\":\"{phase}\",\"status\":\"{status}\"}}");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:WorkspacePath"] = _root })
            .Build();
        var workspace = new JobWorkspace(config, new FakeHostEnvironment { ContentRootPath = Path.GetTempPath() }, NullLogger<JobWorkspace>.Instance);
        return (new WorkspaceAwareJobStore(new InMemoryJobStore(), workspace, NullLogger<WorkspaceAwareJobStore>.Instance), workspace);
    }

    // ---------- the store ----------

    [Fact]
    public async Task An_update_of_a_job_found_on_disk_is_kept()
    {
        var (store, _) = StoreOverFolder();

        var applied = await store.UpdateAsync(JobId, s => { s.Phase = "refiner"; s.Status = "running"; });

        Assert.True(applied);
        var snap = await store.GetAsync(JobId);
        Assert.Equal("refiner", snap!.Phase);
        Assert.Equal("running", snap.Status);

        await store.UpdateAsync(JobId, s => { s.Phase = "completed"; s.Status = "done"; s.CompletedAt = "2026-01-02T03:04:05Z"; });
        snap = await store.GetAsync(JobId);
        Assert.Equal("completed", snap!.Phase);
        Assert.Equal("done", snap.Status);
        Assert.Equal("2026-01-02T03:04:05Z", snap.CompletedAt);
    }

    [Fact]
    public async Task What_an_update_does_not_touch_stays_as_the_folder_said()
    {
        var (store, _) = StoreOverFolder();

        await store.UpdateAsync(JobId, s => s.TranscriptionFooterHint = "a note");

        var snap = await store.GetAsync(JobId);
        Assert.Equal("awaiting_refiner", snap!.Phase);
        Assert.Equal("done", snap.Status);
        Assert.Equal("a note", snap.TranscriptionFooterHint);
    }

    [Fact]
    public async Task A_job_with_no_folder_is_not_made_up()
    {
        var (store, _) = StoreOverFolder();

        Assert.False(await store.UpdateAsync("ffffffffffff", s => s.Phase = "refiner"));
        Assert.Null(await store.GetAsync("ffffffffffff"));
    }

    [Fact]
    public async Task The_job_is_listed_once_and_with_its_phase()
    {
        var (store, _) = StoreOverFolder();
        await store.UpdateAsync(JobId, s => { s.Phase = "refiner"; s.Status = "running"; });

        var list = await store.ListAsync(new JobListFilter(null, null, null, null, Limit: 50, Offset: 0));

        var item = Assert.Single(list, j => j.Id == JobId);
        Assert.Equal("refiner", item.Phase);
        Assert.Equal("running", item.Status);
    }

    [Fact]
    public async Task Its_chunks_are_still_read_from_the_folder_on_every_request()
    {
        var (store, _) = StoreOverFolder();
        Put("chunks/0.wav", "");
        Put("chunks/1.wav", "");
        await store.UpdateAsync(JobId, s => { s.Phase = "refiner"; s.Status = "running"; });
        Assert.Equal(2, (await store.GetAsync(JobId))!.Chunks!.Total);

        Put("chunks/2.wav", "");

        Assert.Equal(3, (await store.GetAsync(JobId))!.Chunks!.Total);
    }

    [Fact]
    public async Task A_refine_cut_short_by_a_stop_of_the_service_reads_as_failed()
    {
        // The file says a refine is running, and this process holds no such job: the run was lost with the old process.
        var (store, _) = StoreOverFolder(phase: "refiner", status: "running");

        var snap = await store.GetAsync(JobId);

        Assert.Equal("awaiting_refiner", snap!.Phase);
        Assert.Equal("failed", snap.Status);
    }

    // ---------- the start of a refine ----------

    private sealed class QueueSpy : IPipelineQueue
    {
        public List<string> Refines { get; } = new();
        public void Enqueue(string jobId) { }
        public void EnqueueRefiner(string jobId, string? inputFile = null, bool lessonPrompt = false) => Refines.Add(jobId);
    }

    private sealed class SilentBroadcaster : IBroadcaster
    {
        public IDisposable Subscribe(string jobId, System.Threading.Channels.ChannelWriter<BroadcastEvent> writer) => new MemoryStream();
        public void Publish(string jobId, BroadcastEvent evt) { }
    }

    private (JobsController Controller, WorkspaceAwareJobStore Store, QueueSpy Queue) ControllerOverFolder(string phase = "awaiting_refiner", string status = "done")
    {
        var (store, workspace) = StoreOverFolder(phase, status);
        var queue = new QueueSpy();
        var controller = new JobsController(
            store, workspace, queue, new SilentBroadcaster(), new RecordingTranscriptionClient(), NullLogger<JobsController>.Instance);
        return (controller, store, queue);
    }

    [Fact]
    public async Task A_second_start_of_a_refine_is_refused_while_the_first_runs()
    {
        var (controller, store, queue) = ControllerOverFolder();

        var first = await controller.StartRefine(JobId, new RefineActionRequest());
        Assert.IsType<AcceptedResult>(first);
        Assert.Equal("refiner", (await store.GetAsync(JobId))!.Phase);

        var second = await controller.StartRefine(JobId, new RefineActionRequest());

        Assert.IsType<ConflictObjectResult>(second);
        Assert.Single(queue.Refines);
    }

    [Fact]
    public async Task A_skipped_refine_leaves_the_job_completed()
    {
        var (controller, store, queue) = ControllerOverFolder();

        var result = await controller.StartRefine(JobId, new RefineActionRequest { Skip = true });

        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(queue.Refines);
        var snap = await store.GetAsync(JobId);
        Assert.Equal("completed", snap!.Phase);
        Assert.Equal("done", snap.Status);
    }

    [Fact]
    public async Task A_refine_cut_short_by_a_stop_of_the_service_may_be_started_again()
    {
        var (controller, store, queue) = ControllerOverFolder(phase: "refiner", status: "running");

        var result = await controller.StartRefine(JobId, new RefineActionRequest());

        Assert.IsType<AcceptedResult>(result);
        Assert.Single(queue.Refines);
        var snap = await store.GetAsync(JobId);
        Assert.Equal("refiner", snap!.Phase);
        Assert.Equal("running", snap.Status);
    }
}
