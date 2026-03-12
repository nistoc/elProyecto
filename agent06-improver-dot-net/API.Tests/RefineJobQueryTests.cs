using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using TranslationImprover.Features.RefineJobQuery.Infrastructure;
using Xunit;

namespace API.Tests;

public class RefineJobQueryTests
{
    [Fact]
    public void List_WithSemanticKey_FiltersByTag()
    {
        var store = new InMemoryRefineJobStore();
        var id1 = store.Create(tags: new[] { "user-1", "scenario-A" });
        var id2 = store.Create(tags: new[] { "user-2" });
        var id3 = store.Create(tags: new[] { "user-1" });

        var query = new RefineJobQueryService(store);
        var list = query.Query(new RefineJobListFilter { SemanticKey = "user-1", Limit = 50 });

        Assert.Equal(2, list.Count);
        Assert.Contains(list, j => j.JobId == id1);
        Assert.Contains(list, j => j.JobId == id3);
        Assert.DoesNotContain(list, j => j.JobId == id2);
    }

    [Fact]
    public void QueryBySemanticKey_ReturnsMatchingJobs()
    {
        var store = new InMemoryRefineJobStore();
        store.Create(tags: new[] { "key-A" });
        store.Create(tags: new[] { "key-B" });
        store.Create(tags: new[] { "key-A", "key-B" });

        var query = new RefineJobQueryService(store);
        var list = query.QueryBySemanticKey("key-A", limit: 10);

        Assert.Equal(2, list.Count);
        Assert.All(list, j => Assert.Contains(j.Tags, t => string.Equals(t, "key-A", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void GetById_Existing_ReturnsJob()
    {
        var store = new InMemoryRefineJobStore();
        var id = store.Create(tags: new[] { "tag1" });
        var query = new RefineJobQueryService(store);

        var job = query.GetById(id);
        Assert.NotNull(job);
        Assert.Equal(id, job.JobId);
        Assert.Single(job.Tags, "tag1");
    }

    [Fact]
    public void GetById_NotExisting_ReturnsNull()
    {
        var store = new InMemoryRefineJobStore();
        var query = new RefineJobQueryService(store);
        Assert.Null(query.GetById("nonexistent"));
    }
}
