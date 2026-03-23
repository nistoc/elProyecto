using Agent04.Features.Transcription.Infrastructure;
using Agent04.Proto;
using Xunit;

namespace Agent04.Tests;

public class ChunkArtifactGroupVirtualModelBinderTests
{
    [Fact]
    public void ApplyToResponse_sets_main_and_sub_virtual_models_by_index()
    {
        var resp = new GetChunkArtifactGroupsResponse();
        var g = new ChunkArtifactGroup { Index = 2, DisplayStem = "x" };
        var sc = new SubChunkArtifactGroup { DisplayStem = "s", SubIndex = 1 };
        g.SubChunks.Add(sc);
        resp.Groups.Add(g);

        var vm = new List<ChunkVirtualModelEntry>
        {
            new()
            {
                ChunkIndex = 2,
                State = "Completed",
                IsSubChunk = false,
                CompletedAt = "2020-01-01T00:00:00Z",
            },
            new()
            {
                ChunkIndex = 2,
                State = "Running",
                IsSubChunk = true,
                ParentChunkIndex = 2,
                SubChunkIndex = 1,
                StartedAt = "2020-01-02T00:00:00Z",
            },
        };

        ChunkArtifactGroupVirtualModelBinder.ApplyToResponse(resp, vm);

        Assert.NotNull(resp.Groups[0].MainVirtualModel);
        Assert.Equal("Completed", resp.Groups[0].MainVirtualModel.State);
        Assert.NotNull(resp.Groups[0].SubChunks[0].SubVirtualModel);
        Assert.Equal("Running", resp.Groups[0].SubChunks[0].SubVirtualModel.State);
    }

    [Fact]
    public void ApplyToResponse_skips_sub_without_sub_index()
    {
        var resp = new GetChunkArtifactGroupsResponse();
        var g = new ChunkArtifactGroup { Index = 0, DisplayStem = "x" };
        g.SubChunks.Add(new SubChunkArtifactGroup { DisplayStem = "u" });
        resp.Groups.Add(g);

        var vm = new List<ChunkVirtualModelEntry>
        {
            new()
            {
                ChunkIndex = 0,
                IsSubChunk = true,
                ParentChunkIndex = 0,
                SubChunkIndex = 0,
                State = "Failed",
            },
        };

        ChunkArtifactGroupVirtualModelBinder.ApplyToResponse(resp, vm);

        Assert.Null(resp.Groups[0].SubChunks[0].SubVirtualModel);
    }

    [Fact]
    public void ApplyToResponse_noop_when_virtual_model_empty()
    {
        var resp = new GetChunkArtifactGroupsResponse();
        resp.Groups.Add(new ChunkArtifactGroup { Index = 0, DisplayStem = "x" });
        ChunkArtifactGroupVirtualModelBinder.ApplyToResponse(resp, Array.Empty<ChunkVirtualModelEntry>());
        Assert.Null(resp.Groups[0].MainVirtualModel);
    }
}
