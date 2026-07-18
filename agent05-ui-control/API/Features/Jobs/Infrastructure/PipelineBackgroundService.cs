using System.Threading.Channels;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Runs job pipelines off a queue instead of controller fire-and-forget. Jobs still run
/// concurrently (one task per job); the host's stopping token cancels in-flight pipelines.
/// </summary>
public sealed class PipelineBackgroundService : BackgroundService, IPipelineQueue
{
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    private readonly IPipeline _pipeline;
    private readonly ILogger<PipelineBackgroundService> _logger;

    public PipelineBackgroundService(IPipeline pipeline, ILogger<PipelineBackgroundService> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    public void Enqueue(string jobId) => _queue.Writer.TryWrite(jobId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var id = jobId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _pipeline.RunAsync(id, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pipeline failed for job {JobId}", id);
                }
            }, stoppingToken);
        }
    }
}
