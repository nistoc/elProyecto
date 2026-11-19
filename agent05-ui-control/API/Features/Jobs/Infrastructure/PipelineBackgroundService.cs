using System.Globalization;
using System.Threading.Channels;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Runs job pipelines off a queue instead of controller fire-and-forget. A job is a task of its own; of
/// the transcriptions only <see cref="MaxParallelJobsKey"/> go at once and the rest wait their turn (a
/// batch of days queues a dozen recordings in one call). A refiner run is short and started by hand, so it
/// does not wait. The host's stopping token cancels in-flight pipelines.
/// </summary>
public sealed class PipelineBackgroundService : BackgroundService, IPipelineQueue
{
    /// <summary>How many transcriptions go at once; each one sends several chunks in parallel itself.</summary>
    public const string MaxParallelJobsKey = "Pipeline:MaxParallelJobs";

    public const int DefaultMaxParallelJobs = 4;

    private readonly Channel<WorkItem> _queue = Channel.CreateUnbounded<WorkItem>();
    private readonly IPipeline _pipeline;
    private readonly ILogger<PipelineBackgroundService> _logger;
    private readonly SemaphoreSlim _transcriptions;

    private readonly record struct WorkItem(string JobId, bool RefinerOnly, string? InputFile = null, bool LessonPrompt = false);

    public PipelineBackgroundService(IPipeline pipeline, IConfiguration configuration, ILogger<PipelineBackgroundService> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
        MaxParallelJobs = ParallelJobs(configuration[MaxParallelJobsKey]);
        _transcriptions = new SemaphoreSlim(MaxParallelJobs, MaxParallelJobs);
    }

    /// <summary>The limit this service runs with.</summary>
    public int MaxParallelJobs { get; }

    /// <summary>The configured limit: the default when the value is absent or not a number, never below 1 or above 64.</summary>
    public static int ParallelJobs(string? configured) =>
        int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 1, 64)
            : DefaultMaxParallelJobs;

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
                    {
                        await _pipeline.RunRefinerAsync(work.JobId, work.InputFile, work.LessonPrompt, stoppingToken);
                        return;
                    }

                    // The wait is inside the job's own task, so a queued transcription never holds back
                    // the reading of the queue (a refiner run behind it starts at once).
                    await _transcriptions.WaitAsync(stoppingToken);
                    try
                    {
                        await _pipeline.RunAsync(work.JobId, stoppingToken);
                    }
                    finally
                    {
                        _transcriptions.Release();
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // The host is stopping.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pipeline failed for job {JobId} (refinerOnly={RefinerOnly})", work.JobId, work.RefinerOnly);
                }
            }, stoppingToken);
        }
    }
}
