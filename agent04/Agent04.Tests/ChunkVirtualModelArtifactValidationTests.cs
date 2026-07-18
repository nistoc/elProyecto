using Agent04.Features.Transcription.Infrastructure;
using Agent04.Proto;
using Xunit;

namespace Agent04.Tests;

public class ChunkVirtualModelArtifactValidationTests
{
    private static ChunkVirtualModelEntry Row(int index, string state, bool isSub = false) =>
        new() { ChunkIndex = index, State = state, IsSubChunk = isSub };

    private static ChunkArtifactGroup Group(int index, bool withJson)
    {
        var g = new ChunkArtifactGroup { Index = index };
        if (withJson)
            g.JsonFiles.Add(new JobArtifactFileEntry { Name = $"part_{index:000}.json" });
        return g;
    }

    [Fact]
    public void CompletedWithoutJson_BecomesFailed_WhenOtherChunksHaveJson()
    {
        var vm = new[] { Row(0, "Completed"), Row(1, "Completed") };
        var groups = new[] { Group(0, withJson: true), Group(1, withJson: false) };

        var result = ChunkVirtualModelArtifactValidation.Apply(vm, groups, jobIsRunningInProcess: false);

        Assert.Equal("Completed", result[0].State);
        Assert.Equal("Failed", result[1].State);
        Assert.Contains("missing", result[1].ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunningRow_BecomesFailed_WhenJobNotRunningInProcess()
    {
        var vm = new[] { Row(0, "Running") };
        var groups = new[] { Group(0, withJson: false) };

        var result = ChunkVirtualModelArtifactValidation.Apply(vm, groups, jobIsRunningInProcess: false);

        Assert.Equal("Failed", result[0].State);
        Assert.Contains("Interrupted", result[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveRunningJob_IsLeftUntouched()
    {
        var vm = new[] { Row(0, "Running"), Row(1, "Completed") };
        var groups = new[] { Group(0, withJson: false), Group(1, withJson: false) };

        var result = ChunkVirtualModelArtifactValidation.Apply(vm, groups, jobIsRunningInProcess: true);

        Assert.Equal("Running", result[0].State);
        Assert.Equal("Completed", result[1].State);
    }

    [Fact]
    public void CompletedRowsKept_WhenNoChunkHasJsonAtAll()
    {
        // Job may legitimately run with save_per_chunk_json disabled.
        var vm = new[] { Row(0, "Completed"), Row(1, "Completed") };
        var groups = new[] { Group(0, withJson: false), Group(1, withJson: false) };

        var result = ChunkVirtualModelArtifactValidation.Apply(vm, groups, jobIsRunningInProcess: false);

        Assert.All(result, r => Assert.Equal("Completed", r.State));
    }

    [Fact]
    public void SubChunkRows_AreLeftUntouched()
    {
        var vm = new[] { Row(0, "Completed", isSub: true) };
        var groups = new[] { Group(0, withJson: true) };

        var result = ChunkVirtualModelArtifactValidation.Apply(vm, groups, jobIsRunningInProcess: false);

        Assert.Equal("Completed", result[0].State);
    }
}
