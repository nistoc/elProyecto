using System.Threading.Channels;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Runs job pipelines off a queue instead of controller fire-and-forget. Jobs still run
/// concurrently (one task per job); the host's stopping token cancels in-flight pipelines.
/// </summary>
public sealed class PipelineBackgroundService : BackgroundService, IPipelineQueue
{
    private readonly Channel<WorkItem> _queue = Channel.CreateUnbounded<WorkItem>();
    private readonly IPipeline _pipeline;
    private readonly ILogger<PipelineBackgroundService> _logger;

    private readonly record struct WorkItem(string JobId, bool RefinerOnly, string? InputFile = null, bool LessonPrompt = false);

    public PipelineBackgroundService(IPipeline pipeline, ILogger<PipelineBackgroundService> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    public void Enqueue(string jobId) => _queue.Writer.TryWrite(new WorkItem(jobId, RefinerOnly: false));

    public void EnqueueRefiner(string jobId, string? inputFile = null, bool lessonPrompt = false) =>
        _queue.Writer.TryWrite(new WorkItem(jobId, RefinerOnly: true, InputFile: inputFile, LessonPrompt: lessonPrompt));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var work = item;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (work.RefinerOnly)
                        await _pipeline.RunRefinerAsync(work.JobId, work.InputFile, work.LessonPrompt, stoppingToken);
                    else
                        await _pipeline.RunAsync(work.JobId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pipeline failed for job {JobId} (refinerOnly={RefinerOnly})", work.JobId, work.RefinerOnly);
                }
            }, stoppingToken);
        }
    }
}
