using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;
using JobState = Agent04.Features.Transcription.Application.JobState;

namespace Agent04.Tests;

public class NodeStoreTagTests
{
    [Fact]
    public void GetNodeByScopeAndId_ReturnsNode_WhenExistsInScope()
    {
        var store = new InMemoryNodeStore();
        store.EnsureNode("job1", null, "job1", "job");
        store.EnsureNode("job1:chunking", "job1", "job1", "phase");
        store.StartNode("job1:chunking");

        var node = store.GetNodeByScopeAndId("job1", "job1:chunking");
        Assert.NotNull(node);
        Assert.Equal("job1:chunking", node.Id);
        Assert.Equal("job1", node.ParentId);
        Assert.Equal("job1", node.ScopeId);
        Assert.Equal("phase", node.Kind);
    }

    [Fact]
    public void GetNodeByScopeAndId_ReturnsNull_WhenNodeNotInScope()
    {
        var store = new InMemoryNodeStore();
        store.EnsureNode("job1", null, "job1", "job");
        store.EnsureNode("job2", null, "job2", "job");

        var node = store.GetNodeByScopeAndId("job1", "job2");
        Assert.Null(node);
    }

    [Fact]
    public void GetNodeByScopeAndId_ReturnsNull_WhenNodeIdEmpty()
    {
        var store = new InMemoryNodeStore();
        store.EnsureNode("job1", null, "job1", "job");
        Assert.Null(store.GetNodeByScopeAndId("job1", ""));
    }

    [Fact]
    public void GetNodeByScopeAndId_ReturnsNull_WhenNodeDoesNotExist()
    {
        var store = new InMemoryNodeStore();
        var node = store.GetNodeByScopeAndId("job1", "job1:nonexistent");
        Assert.Null(node);
    }
}
