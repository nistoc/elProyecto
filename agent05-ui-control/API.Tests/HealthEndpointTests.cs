using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task JobStore_Create_and_Get_returns_snapshot()
    {
        IJobStore store = new InMemoryJobStore();
        var id = await store.CreateAsync(new JobCreateInput("test.mp3", new[] { "tag1" }));
        Assert.NotNull(id);
        Assert.NotEmpty(id);

        var snap = await store.GetAsync(id);
        Assert.NotNull(snap);
        Assert.Equal(id, snap.Id);
        Assert.Equal("test.mp3", snap.OriginalFilename);
        Assert.NotNull(snap.Tags);
        Assert.Single(snap.Tags);
        Assert.Equal("tag1", snap.Tags[0]);
    }

    [Fact]
    public async Task JobStore_List_returns_empty_initially()
    {
        IJobStore store = new InMemoryJobStore();
        var list = await store.ListAsync(new JobListFilter());
        Assert.NotNull(list);
        Assert.Empty(list);
    }
}
