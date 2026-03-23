namespace XtractManager.Features.Jobs.Application;

/// <summary>Manual refiner control after transcription completes (awaiting_refiner).</summary>
public interface IRefinerOrchestration
{
    /// <param name="transcriptRelativePath">Optional path relative to the job directory (from UI row). When set, that file is read first.</param>
    Task StartRefinerAsync(string jobId, string? transcriptRelativePath = null, CancellationToken ct = default);
    Task PauseRefinerAsync(string jobId, CancellationToken ct = default);
    Task ResumeRefinerAsync(string jobId, CancellationToken ct = default);
    Task SkipRefinerAsync(string jobId, CancellationToken ct = default);
}
