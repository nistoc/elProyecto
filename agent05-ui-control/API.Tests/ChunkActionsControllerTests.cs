using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>Fake workspace: only paths used by JobsController chunk-actions tests.</summary>
internal sealed class StubJobWorkspace : IJobWorkspace
{
    public string WorkspaceRootPath { get; } = Path.GetTempPath();

    public string GetJobDirectoryPath(string jobId) =>
        Path.Combine(WorkspaceRootPath, "xtract-test-jobs", jobId);

    public Task EnsureJobDirectoryAsync(string jobId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<string> SaveUploadedFileAsync(string jobId, Stream source, string originalFileName, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<(string JobId, DateTime CreatedUtc)>> ListJobDirectoriesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(string JobId, DateTime CreatedUtc)>>(Array.Empty<(string, DateTime)>());

    public Task<bool> TryDeleteJobDirectoryAsync(string jobId, CancellationToken ct = default) =>
        Task.FromResult(false);
}

internal sealed class RecordingTranscriptionClient : ITranscriptionServiceClient
{
    public List<(string Agent04JobId, TranscriptionChunkAction Action, int ChunkIndex, string? JobDirectoryRelative, int SplitParts, int SubChunkIndex)> ChunkCalls { get; } = new();

    public ChunkCommandResult? NextChunkResult { get; set; } = new(true, "cancel_requested");

    public Task<ChunkCommandResult> ChunkCommandAsync(
        string agent04JobId,
        TranscriptionChunkAction action,
        int chunkIndex,
        string? jobDirectoryRelative = null,
        int splitParts = 0,
        int subChunkIndex = 0,
        CancellationToken ct = default)
    {
        ChunkCalls.Add((agent04JobId, action, chunkIndex, jobDirectoryRelative, splitParts, subChunkIndex));
        return Task.FromResult(NextChunkResult ?? new ChunkCommandResult(false, "unset"));
    }

    public Task<SubmitJobResult> SubmitJobAsync(
        string configPath,
        string inputFilePath,
        IReadOnlyList<string>? tags,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<JobStatusUpdate> StreamJobStatusAsync(
        string jobId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var u in StreamJobStatusAsync(jobId, null, ct))
            yield return u;
    }

    public async IAsyncEnumerable<JobStatusUpdate> StreamJobStatusAsync(
        string jobId,
        IReadOnlyList<ChunkVirtualModelEntry>? clientChunkVirtualModel,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<JobStatusUpdate?> GetJobStatusAsync(string agent04JobId, CancellationToken ct = default) =>
        GetJobStatusAsync(agent04JobId, null, ct);

    public Task<JobStatusUpdate?> GetJobStatusAsync(
        string agent04JobId,
        IReadOnlyList<ChunkVirtualModelEntry>? clientChunkVirtualModel,
        CancellationToken ct) =>
        Task.FromResult<JobStatusUpdate?>(null);

    public ChunkArtifactGroupsResult? NextChunkGroups { get; set; }

    public Task<ChunkArtifactGroupsResult?> GetChunkArtifactGroupsAsync(
        string agent04JobId,
        string jobDirectoryRelative,
        int totalChunks,
        CancellationToken ct = default) =>
        Task.FromResult(NextChunkGroups);

    public Task<JobProjectFiles?> GetProjectFilesAsync(
        string agent04JobId,
        string jobDirectoryRelative,
        CancellationToken ct = default) =>
        Task.FromResult<JobProjectFiles?>(null);
}

public class ChunkActionsControllerTests
{
    private static async Task<string> CreateRunningTranscriberJobAsync(
        InMemoryJobStore store,
        string agent04JobId = "agent04-test-job")
    {
        var id = await store.CreateAsync(new JobCreateInput("x.m4a", null));
        await store.UpdateAsync(id, s =>
        {
            s.Phase = "transcriber";
            s.Status = "running";
            s.Agent04JobId = agent04JobId;
        });
        return id;
    }

    [Fact]
    public async Task PostChunkAction_valid_job_calls_transcription_client_with_cancel()
    {
        var store = new InMemoryJobStore();
        var jobId = await CreateRunningTranscriberJobAsync(store, "a04-xyz");
        var grpc = new RecordingTranscriptionClient();
        var controller = new JobsController(
            store,
            new StubJobWorkspace(),
            MockPipeline.Instance,
            MockBroadcaster.Instance,
            grpc,
            NullLogger<JobsController>.Instance);

        var result = await controller.PostChunkAction(
            jobId,
            new ChunkActionRequest { Action = "cancel", ChunkIndex = 2 },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ChunkActionResponse>(ok.Value);
        Assert.True(body.Ok);
        Assert.Single(grpc.ChunkCalls);
        Assert.Equal("a04-xyz", grpc.ChunkCalls[0].Agent04JobId);
        Assert.Equal(TranscriptionChunkAction.Cancel, grpc.ChunkCalls[0].Action);
        Assert.Equal(2, grpc.ChunkCalls[0].ChunkIndex);
        Assert.Equal(jobId, grpc.ChunkCalls[0].JobDirectoryRelative);
        Assert.Equal(-1, grpc.ChunkCalls[0].SubChunkIndex);
    }

    [Fact]
    public async Task PostChunkAction_cancel_with_subChunkIndex_forwards_sub_index()
    {
        var store = new InMemoryJobStore();
        var jobId = await CreateRunningTranscriberJobAsync(store, "a04-sub");
        var grpc = new RecordingTranscriptionClient();
        var controller = new JobsController(
            store,
            new StubJobWorkspace(),
            MockPipeline.Instance,
            MockBroadcaster.Instance,
            grpc,
            NullLogger<JobsController>.Instance);

        var result = await controller.PostChunkAction(
            jobId,
            new ChunkActionRequest { Action = "cancel", ChunkIndex = 4, SubChunkIndex = 2 },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(Assert.IsType<ChunkActionResponse>(ok.Value).Ok);
        Assert.Single(grpc.ChunkCalls);
        Assert.Equal(4, grpc.ChunkCalls[0].ChunkIndex);
        Assert.Equal(2, grpc.ChunkCalls[0].SubChunkIndex);
    }

    [Fact]
    public async Task PostChunkAction_wrong_phase_returns_conflict_and_does_not_call_client()
    {
        var store = new InMemoryJobStore();
        var id = await store.CreateAsync(new JobCreateInput("x.m4a", null));
        await store.UpdateAsync(id, s =>
        {
            s.Phase = "refiner";
            s.Status = "running";
            s.Agent04JobId = "a04-1";
        });
        var grpc = new RecordingTranscriptionClient();
        var controller = new JobsController(
            store,
            new StubJobWorkspace(),
            MockPipeline.Instance,
            MockBroadcaster.Instance,
            grpc,
            NullLogger<JobsController>.Instance);

        var result = await controller.PostChunkAction(
            id,
            new ChunkActionRequest { Action = "cancel", ChunkIndex = 0 },
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Empty(grpc.ChunkCalls);
    }

    [Fact]
    public async Task PostChunkAction_missing_agent04_id_returns_conflict()
    {
        var store = new InMemoryJobStore();
        var id = await store.CreateAsync(new JobCreateInput("x.m4a", null));
        await store.UpdateAsync(id, s =>
        {
            s.Phase = "transcriber";
            s.Status = "running";
            s.Agent04JobId = null;
        });
        var grpc = new RecordingTranscriptionClient();
        var controller = new JobsController(
            store,
            new StubJobWorkspace(),
            MockPipeline.Instance,
            MockBroadcaster.Instance,
            grpc,
            NullLogger<JobsController>.Instance);

        var result = await controller.PostChunkAction(
            id,
            new ChunkActionRequest { Action = "cancel", ChunkIndex = 0 },
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Empty(grpc.ChunkCalls);
    }

    [Fact]
    public async Task PostChunkAction_client_returns_ok_false_still_http_200_with_body()
    {
        var store = new InMemoryJobStore();
        var jobId = await CreateRunningTranscriberJobAsync(store);
        var grpc = new RecordingTranscriptionClient
        {
            NextChunkResult = new ChunkCommandResult(false, "not_implemented")
        };
        var controller = new JobsController(
            store,
            new StubJobWorkspace(),
            MockPipeline.Instance,
            MockBroadcaster.Instance,
            grpc,
            NullLogger<JobsController>.Instance);

        var result = await controller.PostChunkAction(
            jobId,
            new ChunkActionRequest { Action = "skip", ChunkIndex = 1 },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ChunkActionResponse>(ok.Value);
        Assert.False(body.Ok);
        Assert.Equal("not_implemented", body.Message);
        Assert.Single(grpc.ChunkCalls);
        Assert.Equal(TranscriptionChunkAction.Skip, grpc.ChunkCalls[0].Action);
    }

    [Fact]
    public async Task PostChunkAction_unknown_action_bad_request()
    {
        var store = new InMemoryJobStore();
        var jobId = await CreateRunningTranscriberJobAsync(store);
        var grpc = new RecordingTranscriptionClient();
        var controller = new JobsController(
            store,
            new StubJobWorkspace(),
            MockPipeline.Instance,
            MockBroadcaster.Instance,
            grpc,
            NullLogger<JobsController>.Instance);

        var result = await controller.PostChunkAction(
            jobId,
            new ChunkActionRequest { Action = "nope", ChunkIndex = 0 },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(grpc.ChunkCalls);
    }

    [Fact]
    public async Task PostChunkAction_split_without_split_parts_bad_request()
    {
        var store = new InMemoryJobStore();
        var jobId = await CreateRunningTranscriberJobAsync(store);
        var grpc = new RecordingTranscriptionClient();
        var controller = new JobsController(
            store,
            new StubJobWorkspace(),
            MockPipeline.Instance,
            MockBroadcaster.Instance,
            grpc,
            NullLogger<JobsController>.Instance);

        var result = await controller.PostChunkAction(
            jobId,
            new ChunkActionRequest { Action = "split", ChunkIndex = 0 },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(grpc.ChunkCalls);
    }

    [Fact]
    public async Task PostChunkAction_split_forwards_split_parts_to_client()
    {
        var store = new InMemoryJobStore();
        var jobId = await CreateRunningTranscriberJobAsync(store);
        var grpc = new RecordingTranscriptionClient { NextChunkResult = new ChunkCommandResult(true, "split_ok") };
        var controller = new JobsController(
            store,
            new StubJobWorkspace(),
            MockPipeline.Instance,
            MockBroadcaster.Instance,
            grpc,
            NullLogger<JobsController>.Instance);

        var result = await controller.PostChunkAction(
            jobId,
            new ChunkActionRequest { Action = "split", ChunkIndex = 1, SplitParts = 3 },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ChunkActionResponse>(ok.Value);
        Assert.True(body.Ok);
        Assert.Single(grpc.ChunkCalls);
        Assert.Equal(TranscriptionChunkAction.Split, grpc.ChunkCalls[0].Action);
        Assert.Equal(3, grpc.ChunkCalls[0].SplitParts);
        Assert.Equal(1, grpc.ChunkCalls[0].ChunkIndex);
    }

    private static class MockPipeline
    {
        public static readonly IPipeline Instance = new NoOpPipeline();
        private sealed class NoOpPipeline : IPipeline
        {
            public Task RunAsync(string jobId, CancellationToken ct = default) => Task.CompletedTask;
        }
    }

    private static class MockBroadcaster
    {
        public static readonly IBroadcaster Instance = new NoOpBroadcaster();
        private sealed class NoOpBroadcaster : IBroadcaster
        {
            public void Publish(string jobId, string payloadJson) { }
            public void Subscribe(string jobId, Action<string> handler) { }
            public void Unsubscribe(string jobId, Action<string> handler) { }
        }
    }
}
