using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class ChunkArtifactGroupsControllerTests
{
    [Fact]
    public async Task GetChunkArtifactGroups_ReturnsGroups_FromTranscriptionClient()
    {
        var store = new InMemoryJobStore();
        var jobId = await store.CreateAsync(new JobCreateInput("a.wav", null));
        await store.UpdateAsync(jobId, s =>
        {
            s.Agent04JobId = "grpc-job-1";
            s.Chunks = new ChunkState { Total = 2 };
        });

        var ws = new StubJobWorkspace();
        Directory.CreateDirectory(ws.GetJobDirectoryPath(jobId));

        var transcription = new RecordingTranscriptionClient
        {
            NextChunkGroups = new ChunkArtifactGroupsResult
            {
                Groups =
                [
                    new ChunkArtifactGroupJson
                    {
                        Index = 0,
                        DisplayStem = "chunk0",
                        AudioFiles =
                        [
                            new JobProjectFile
                            {
                                Name = "x.wav",
                                RelativePath = "chunks/x.wav",
                                Kind = "audio",
                                SizeBytes = 10,
                                Index = 0
                            }
                        ],
                        JsonFiles = [],
                        SubChunks = [],
                        MergedSplitFiles = [],
                        VmRow = new ChunkVirtualModelEntry
                        {
                            Index = 0,
                            State = "Running",
                            StartedAt = "2020-01-01T00:00:00Z",
                        },
                    }
                ]
            }
        };

        var controller = new JobsController(
            store,
            ws,
            MockPipeline.Instance,
            new NoOpRefinerOrchestration(),
            MockBroadcaster.Instance,
            transcription,
            NullLogger<JobsController>.Instance);

        var result = await controller.GetChunkArtifactGroups(jobId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, ApiJson.CamelCase);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("groups", out var groupsEl));
        Assert.Equal(JsonValueKind.Array, groupsEl.ValueKind);
        Assert.Equal(1, groupsEl.GetArrayLength());
        var g0 = groupsEl[0];
        Assert.Equal(0, g0.GetProperty("index").GetInt32());
        Assert.Equal("chunk0", g0.GetProperty("displayStem").GetString());
        Assert.Equal("x.wav", g0.GetProperty("audioFiles")[0].GetProperty("name").GetString());
        Assert.True(g0.TryGetProperty("vmRow", out var vmEl));
        Assert.Equal("Running", vmEl.GetProperty("state").GetString());
        Assert.Equal(0, vmEl.GetProperty("index").GetInt32());
        Assert.Single(transcription.GetChunkArtifactGroupsCalls);
        Assert.Null(transcription.GetChunkArtifactGroupsCalls[0].ClientVm);
    }

    [Fact]
    public async Task GetChunkArtifactGroups_WithoutAgent04Id_UsesXtractIdAsGrpcScope()
    {
        var store = new InMemoryJobStore();
        var jobId = await store.CreateAsync(new JobCreateInput("a.wav", null));
        var ws = new StubJobWorkspace();
        Directory.CreateDirectory(ws.GetJobDirectoryPath(jobId));

        var transcription = new RecordingTranscriptionClient
        {
            NextChunkGroups = new ChunkArtifactGroupsResult { Groups = [] }
        };

        var controller = new JobsController(
            store,
            ws,
            MockPipeline.Instance,
            new NoOpRefinerOrchestration(),
            MockBroadcaster.Instance,
            transcription,
            NullLogger<JobsController>.Instance);

        var result = await controller.GetChunkArtifactGroups(jobId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        Assert.Single(transcription.GetChunkArtifactGroupsCalls);
        var call = transcription.GetChunkArtifactGroupsCalls[0];
        Assert.Equal(jobId, call.Agent04JobId);
        Assert.Equal(jobId, call.JobDirectoryRelative);
        Assert.Equal(0, call.TotalChunks);
        Assert.Null(call.ClientVm);
    }

    [Fact]
    public async Task GetChunkArtifactGroups_PassesChunkVirtualModel_ToTranscriptionClient()
    {
        var store = new InMemoryJobStore();
        var jobId = await store.CreateAsync(new JobCreateInput("a.wav", null));
        await store.UpdateAsync(jobId, s =>
        {
            s.Agent04JobId = "grpc-job-1";
            s.Chunks = new ChunkState
            {
                Total = 3,
                ChunkVirtualModel =
                [
                    new ChunkVirtualModelEntry
                    {
                        Index = 0,
                        State = "Completed",
                        StartedAt = "2020-01-01T00:00:00Z",
                        CompletedAt = "2020-01-01T00:10:00Z",
                    },
                ],
            };
        });

        var ws = new StubJobWorkspace();
        Directory.CreateDirectory(ws.GetJobDirectoryPath(jobId));

        var transcription = new RecordingTranscriptionClient
        {
            NextChunkGroups = new ChunkArtifactGroupsResult { Groups = [] },
        };

        var controller = new JobsController(
            store,
            ws,
            MockPipeline.Instance,
            new NoOpRefinerOrchestration(),
            MockBroadcaster.Instance,
            transcription,
            NullLogger<JobsController>.Instance);

        await controller.GetChunkArtifactGroups(jobId, CancellationToken.None);
        Assert.Single(transcription.GetChunkArtifactGroupsCalls);
        var call = transcription.GetChunkArtifactGroupsCalls[0];
        Assert.NotNull(call.ClientVm);
        Assert.Single(call.ClientVm!);
        Assert.Equal("Completed", call.ClientVm![0].State);
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
