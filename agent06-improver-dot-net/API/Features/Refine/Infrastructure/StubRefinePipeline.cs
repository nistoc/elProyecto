using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// Stub pipeline until full implementation (stage 6). Sets job to Running then Failed with "Not implemented".
/// </summary>
public sealed class StubRefinePipeline : IRefinePipeline
{
    private readonly IRefineJobStore _store;

    public StubRefinePipeline(IRefineJobStore store)
    {
        _store = store;
    }

    public async Task RunAsync(string jobId, RefineJobRequest request, string workspaceRoot, string artifactRoot, CancellationToken cancellationToken = default)
    {
        _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Running, CurrentPhase = "Starting" });
        await Task.Yield();
        _store.Update(jobId, new RefineJobStatusUpdate { State = RefineJobState.Failed, ErrorMessage = "Refine pipeline not implemented yet." });
    }
}
