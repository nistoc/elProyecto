using TranslationImprover.Application;
using TranslationImprover.Features.Refine.Application;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// Takes a refine request from either entry (REST, gRPC) and starts the run: resolves the job folder, brings the
/// request to one form (<see cref="RefineJobRequestMapper"/>), checks it, creates the job and lets the pipeline go.
/// </summary>
public sealed class RefineJobSubmitter
{
    private readonly IRefineJobStore _store;
    private readonly IRefinePipeline _pipeline;
    private readonly IRefineJobCancellation _cancellation;
    private readonly WorkspaceRoot _workspaceRoot;

    public RefineJobSubmitter(IRefineJobStore store, IRefinePipeline pipeline, IRefineJobCancellation cancellation, WorkspaceRoot workspaceRoot)
    {
        _store = store;
        _pipeline = pipeline;
        _cancellation = cancellation;
        _workspaceRoot = workspaceRoot;
    }

    /// <param name="JobId">The started job; null when the request was refused.</param>
    /// <param name="Error">Why the request was refused; null when the job was started.</param>
    public sealed record Result(string? JobId, RefineRequestError? Error);

    /// <param name="request">The request as the entry received it.</param>
    /// <param name="entryToken">The token of the call that brought the request; the run is tied to it as before.</param>
    public Result Submit(RefineJobRequest? request, CancellationToken entryToken)
    {
        var root = _workspaceRoot.RootPath;
        string artifactRoot;
        try
        {
            artifactRoot = RefineWorkspacePaths.ResolveEffectiveArtifactRoot(root,
                string.IsNullOrWhiteSpace(request?.JobDirectoryRelative) ? null : request!.JobDirectoryRelative!.Trim());
        }
        catch (ArgumentException ex)
        {
            return new Result(null, new RefineRequestError(ex.Message));
        }

        var normalized = RefineJobRequestMapper.Normalize(request ?? new RefineJobRequest());
        var error = RefineJobRequestMapper.Validate(normalized, artifactRoot);
        if (error != null)
            return new Result(null, error);

        var jobId = _store.Create(normalized.Tags, normalized.CallbackUrl);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(entryToken);
        _cancellation.Register(jobId, cts);
        _ = _pipeline.RunAsync(jobId, normalized, root, artifactRoot, cts.Token);
        return new Result(jobId, null);
    }
}
