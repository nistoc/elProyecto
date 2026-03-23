namespace TranslationImprover.Features.Refine.Application;

/// <summary>
/// Register and cancel CancellationTokenSource per job_id for POST .../cancel and gRPC CancelRefineJob.
/// </summary>
public interface IRefineJobCancellation
{
    void Register(string jobId, CancellationTokenSource cts);
    bool TryCancel(string jobId);
}
