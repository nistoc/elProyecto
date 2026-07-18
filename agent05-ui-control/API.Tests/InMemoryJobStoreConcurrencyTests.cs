using System.Text.Json;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class InMemoryJobStoreConcurrencyTests
{
    [Fact]
    public async Task ParallelUpdatesAndSerializingReads_DoNotThrow()
    {
        var store = new InMemoryJobStore();
        var id = await store.CreateAsync(new JobCreateInput("audio.m4a"));
        await store.UpdateAsync(id, s => s.Chunks = new ChunkState { Total = 10 });

        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < 1000; i++)
            {
                await store.UpdateAsync(id, s =>
                {
                    s.Chunks!.Completed = Enumerable.Range(0, i % 10).ToList();
                    s.Chunks.ChunkVirtualModel = Enumerable.Range(0, i % 10)
                        .Select(n => new ChunkVirtualModelEntry { Index = n, State = "Running" })
                        .ToList();
                    s.Status = i % 2 == 0 ? "running" : "queued";
                });
            }
        });

        var reader = Task.Run(async () =>
        {
            for (var i = 0; i < 1000; i++)
            {
                var snap = await store.GetAsync(id);
                Assert.NotNull(snap);
                _ = JsonSerializer.Serialize(snap);
            }
        });

        await Task.WhenAll(writer, reader);
    }

    [Fact]
    public async Task GetAsync_ReturnsIndependentCopy()
    {
        var store = new InMemoryJobStore();
        var id = await store.CreateAsync(new JobCreateInput("audio.m4a"));

        var copy = await store.GetAsync(id);
        Assert.NotNull(copy);
        copy!.Status = "mutated-locally";

        var fresh = await store.GetAsync(id);
        Assert.Equal("queued", fresh!.Status);
    }
}
