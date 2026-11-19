using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>Only so many transcriptions go at once; the rest wait their turn, and a refiner run does not wait.</summary>
public sealed class PipelineLimitTests
{
    private static PipelineBackgroundService Service(IPipeline pipeline, string? limit)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [PipelineBackgroundService.MaxParallelJobsKey] = limit })
            .Build();
        return new PipelineBackgroundService(pipeline, config, NullLogger<PipelineBackgroundService>.Instance);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not come true in 10 seconds.");
            await Task.Delay(20);
        }
    }

    [Theory]
    [InlineData(null, PipelineBackgroundService.DefaultMaxParallelJobs)]
    [InlineData("", PipelineBackgroundService.DefaultMaxParallelJobs)]
    [InlineData("many", PipelineBackgroundService.DefaultMaxParallelJobs)]
    [InlineData("3", 3)]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("1000", 64)]
    public void The_limit_is_the_configured_number_kept_between_one_and_sixty_four(string? configured, int expected)
    {
        Assert.Equal(expected, PipelineBackgroundService.ParallelJobs(configured));
        Assert.Equal(expected, Service(new GatedPipeline(), configured).MaxParallelJobs);
    }

    [Fact]
    public async Task A_transcription_above_the_limit_waits_until_one_of_the_going_ends()
    {
        var pipeline = new GatedPipeline();
        using var service = Service(pipeline, "2");
        await service.StartAsync(CancellationToken.None);
        try
        {
            service.Enqueue("a");
            service.Enqueue("b");
            service.Enqueue("c");

            await Until(() => pipeline.Started.Count == 2);
            await Task.Delay(250);
            Assert.Equal(2, pipeline.Started.Count);

            pipeline.Finish(pipeline.Started.First());

            await Until(() => pipeline.Started.Count == 3);
            Assert.Equal(new[] { "a", "b", "c" }, pipeline.Started.OrderBy(j => j, StringComparer.Ordinal));
        }
        finally
        {
            pipeline.FinishAll();
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_refiner_run_does_not_wait_behind_the_transcriptions()
    {
        var pipeline = new GatedPipeline();
        using var service = Service(pipeline, "1");
        await service.StartAsync(CancellationToken.None);
        try
        {
            service.Enqueue("a");
            service.Enqueue("b");
            await Until(() => pipeline.Started.Count == 1);

            service.EnqueueRefiner("r");

            await Until(() => pipeline.Refines.Contains("r"));
            Assert.Single(pipeline.Started);
        }
        finally
        {
            pipeline.FinishAll();
            await service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>A transcription here goes on until the test ends it.</summary>
    private sealed class GatedPipeline : IPipeline
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new();

        public ConcurrentQueue<string> Started { get; } = new();
        public ConcurrentQueue<string> Refines { get; } = new();

        private TaskCompletionSource Gate(string jobId) =>
            _gates.GetOrAdd(jobId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public void Finish(string jobId) => Gate(jobId).TrySetResult();

        public void FinishAll()
        {
            foreach (var jobId in new[] { "a", "b", "c" })
                Finish(jobId);
        }

        public async Task RunAsync(string jobId, CancellationToken ct = default)
        {
            Started.Enqueue(jobId);
            await Gate(jobId).Task.WaitAsync(ct);
        }

        public Task RunRefinerAsync(string jobId, string? inputFile, bool lessonPrompt, CancellationToken ct = default)
        {
            Refines.Enqueue(jobId);
            return Task.CompletedTask;
        }
    }
}
