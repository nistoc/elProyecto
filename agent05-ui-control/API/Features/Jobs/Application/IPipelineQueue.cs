namespace XtractManager.Features.Jobs.Application;

public interface IPipelineQueue
{
    /// <summary>Queues a job for background transcription (agent04); the refiner is started manually afterwards.</summary>
    void Enqueue(string jobId);

    /// <summary>Queues only the refiner stage (agent06) for a job whose transcription already finished. <paramref name="inputFile"/> is an optional job-relative transcript file to refine (null = newest combined).</summary>
    /// <param name="lessonPrompt">The refiner's lesson prompt with the check of edits instead of the saved settings (<see cref="RefinerRunPlan"/>).</param>
    void EnqueueRefiner(string jobId, string? inputFile = null, bool lessonPrompt = false);
}
