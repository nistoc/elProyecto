namespace XtractManager.Features.Jobs.Application;

public interface IPipeline
{
    Task RunAsync(string jobId, CancellationToken ct = default);

    /// <summary>Runs only the refiner stage for a job whose transcription already finished (manual start after review). <paramref name="inputFile"/> is an optional job-relative transcript file to refine; when null the newest combined transcript is used.</summary>
    Task RunRefinerAsync(string jobId, string? inputFile, CancellationToken ct = default);
}
