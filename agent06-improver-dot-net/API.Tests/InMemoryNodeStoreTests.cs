using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

public class InMemoryNodeStoreTests
{
    [Fact]
    public void GetNodeByScopeAndId_Existing_ReturnsNode()
    {
        var store = new InMemoryNodeStore();
        store.EnsureNode("job1", null, "job1", "job");
        store.EnsureNode("job1:refine", "job1", "job1", "phase");
        store.EnsureNode("job1:refine:batch-0", "job1:refine", "job1", "batch");

        var node = store.GetNodeByScopeAndId("job1", "job1:refine:batch-0");
        Assert.NotNull(node);
        Assert.Equal("job1:refine:batch-0", node.Id);
        Assert.Equal("job1:refine", node.ParentId);
        Assert.Equal("job1", node.ScopeId);
        Assert.Equal("batch", node.Kind);
    }

    [Fact]
    public void GetNodeByScopeAndId_WrongScope_ReturnsNull()
    {
        var store = new InMemoryNodeStore();
        store.EnsureNode("job1:refine", "job1", "job1", "phase");

        Assert.Null(store.GetNodeByScopeAndId("other-scope", "job1:refine"));
    }

    [Fact]
    public void GetNodeByScopeAndId_NotExisting_ReturnsNull()
    {
        var store = new InMemoryNodeStore();
        Assert.Null(store.GetNodeByScopeAndId("job1", "missing-node"));
    }

    [Fact]
    public void GetTreeByScope_BuildsHierarchy()
    {
        var store = new InMemoryNodeStore();
        store.EnsureNode("job1", null, "job1", "job");
        store.EnsureNode("job1:refine", "job1", "job1", "phase");
        store.EnsureNode("job1:refine:batch-0", "job1:refine", "job1", "batch");

        var tree = store.GetTreeByScope("job1");
        Assert.Single(tree);
        Assert.Equal("job1", tree[0].Id);
        Assert.NotNull(tree[0].Children);
        Assert.Single(tree[0].Children!);
        Assert.Equal("job1:refine", tree[0].Children![0].Id);
        Assert.Single(tree[0].Children![0].Children!);
        Assert.Equal("job1:refine:batch-0", tree[0].Children![0].Children![0].Id);
    }

    [Fact]
    public void CompleteNode_UpdatesStatus()
    {
        var store = new InMemoryNodeStore();
        store.EnsureNode("n1", null, "scope1", "job");
        store.StartNode("n1");
        store.CompleteNode("n1", RefineJobState.Completed, DateTimeOffset.UtcNow);

        var node = store.GetNodeByScopeAndId("scope1", "n1");
        Assert.NotNull(node);
        Assert.Equal(RefineJobState.Completed, node.Status);
    }
}
