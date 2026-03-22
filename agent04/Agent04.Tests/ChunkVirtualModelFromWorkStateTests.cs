using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class ChunkVirtualModelFromWorkStateTests
{
    [Fact]
    public void Build_maps_main_and_sub_rows()
    {
        var doc = new TranscriptionWorkStateDocument
        {
            Chunks =
            [
                new TranscriptionWorkStateChunk
                {
                    Index = 1,
                    State = "Completed",
                    StartedAt = "2026-01-01T00:00:00Z",
                    CompletedAt = "2026-01-01T00:05:00Z",
                    IsSubChunk = false,
                },
                new TranscriptionWorkStateChunk
                {
                    Index = 1,
                    State = "Running",
                    StartedAt = "2026-01-02T00:00:00Z",
                    IsSubChunk = true,
                    ParentChunkIndex = 1,
                    SubChunkIndex = 0,
                },
            ],
        };

        var vm = ChunkVirtualModelFromWorkState.Build(doc);

        Assert.Equal(2, vm.Count);
        Assert.False(vm[0].IsSubChunk);
        Assert.Equal(1, vm[0].ChunkIndex);
        Assert.Equal("Completed", vm[0].State);
        Assert.True(vm[1].IsSubChunk);
        Assert.Equal(1, vm[1].ParentChunkIndex);
        Assert.Equal(0, vm[1].SubChunkIndex);
        Assert.Equal("Running", vm[1].State);
    }

    [Fact]
    public void Build_null_or_empty_returns_empty()
    {
        Assert.Empty(ChunkVirtualModelFromWorkState.Build(null));
        Assert.Empty(ChunkVirtualModelFromWorkState.Build(new TranscriptionWorkStateDocument()));
    }
}
