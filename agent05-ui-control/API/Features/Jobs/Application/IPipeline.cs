namespace XtractManager.Features.Jobs.Application;

public interface IPipeline
{
    Task RunAsync(string jobId, CancellationToken ct = default);

    /// <summary>Runs only the refiner stage for a job whose transcription already finished (manual start after review).</summary>
    Task RunRefinerAsync(string jobId, CancellationToken ct = default);
}
