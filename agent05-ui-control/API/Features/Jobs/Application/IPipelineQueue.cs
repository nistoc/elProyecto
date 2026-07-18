namespace XtractManager.Features.Jobs.Application;

public interface IPipelineQueue
{
    /// <summary>Queues a job for background transcription (agent04); the refiner is started manually afterwards.</summary>
    void Enqueue(string jobId);

    /// <summary>Queues only the refiner stage (agent06) for a job whose transcription already finished.</summary>
    void EnqueueRefiner(string jobId);
}
