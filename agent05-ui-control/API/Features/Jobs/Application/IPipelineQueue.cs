namespace XtractManager.Features.Jobs.Application;

public interface IPipelineQueue
{
    /// <summary>Queues a job for background pipeline execution (agent04 → agent06).</summary>
    void Enqueue(string jobId);
}
