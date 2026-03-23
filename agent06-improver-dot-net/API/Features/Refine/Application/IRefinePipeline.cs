using TranslationImprover.Features.Refine.Infrastructure;

namespace TranslationImprover.Features.Refine.Application;

/// <summary>
/// Runs the refinement pipeline for a job. Called after job is created; receives CancellationToken for cancel support.
/// </summary>
public interface IRefinePipeline
{
    /// <summary>
    /// Process input (file or inline content) and write result; update store and optionally node model.
    /// </summary>
    /// <param name="artifactRoot">Per-job directory (shared jobs root + job_directory_relative): refiner_threads, outputs, debug log.</param>
    Task RunAsync(
        string jobId,
        RefineJobRequest request,
        string artifactRoot,
        CancellationToken cancellationToken = default);

    /// <summary>Continue a paused job using <c>refiner_threads/checkpoint.json</c> under the job artifact root.</summary>
    Task ResumeAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Continue from an on-disk checkpoint under <paramref name="artifactRoot"/> for a newly created <paramref name="jobId"/> (e.g. after Agent06 restart).
    /// </summary>
    Task ResumeFromCheckpointAsync(string jobId, string artifactRoot, RefineCheckpoint checkpoint, CancellationToken cancellationToken = default);
}
