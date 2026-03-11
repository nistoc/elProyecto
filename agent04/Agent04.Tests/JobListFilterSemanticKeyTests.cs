using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class JobListFilterSemanticKeyTests
{
    [Fact]
    public void List_WithSemanticKey_FiltersJobsByTag()
    {
        var store = new InMemoryJobStatusStore();
        var id1 = store.Create(new[] { "alpha", "beta" }, null);
        var id2 = store.Create(new[] { "beta", "gamma" }, null);
        var id3 = store.Create(null, null);

        var list = store.List(new JobListFilter { SemanticKey = "beta", Limit = 50 });
        Assert.Equal(2, list.Count);
        Assert.Contains(list, j => j.JobId == id1);
        Assert.Contains(list, j => j.JobId == id2);
        Assert.DoesNotContain(list, j => j.JobId == id3);
    }

    [Fact]
    public void List_WithSemanticKey_EmptyWhenNoMatch()
    {
        var store = new InMemoryJobStatusStore();
        store.Create(new[] { "alpha" }, null);
        var list = store.List(new JobListFilter { SemanticKey = "zeta", Limit = 50 });
        Assert.Empty(list);
    }

    [Fact]
    public void List_WithoutSemanticKey_ReturnsAll()
    {
        var store = new InMemoryJobStatusStore();
        store.Create(new[] { "alpha" }, null);
        store.Create(new[] { "beta" }, null);
        var list = store.List(new JobListFilter { Limit = 50 });
        Assert.Equal(2, list.Count);
    }
}
