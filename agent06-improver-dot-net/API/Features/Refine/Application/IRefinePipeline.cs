namespace TranslationImprover.Features.Refine.Application;

/// <summary>
/// Runs the refinement pipeline for a job. Called after job is created; receives CancellationToken for cancel support.
/// </summary>
public interface IRefinePipeline
{
    /// <summary>
    /// Process input (file or inline content) and write result; update store and optionally node model.
    /// </summary>
    Task RunAsync(
        string jobId,
        RefineJobRequest request,
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}
