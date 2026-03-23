namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class StubRefinerServiceClient : Application.IRefinerServiceClient
{
    public Task<Application.SubmitRefineJobResult> SubmitRefineJobAsync(Application.RefineJobInput input, CancellationToken ct = default)
    {
        return Task.FromResult(new Application.SubmitRefineJobResult("stub-refine-job"));
    }

    public async IAsyncEnumerable<Application.RefineStatusUpdate> StreamRefineStatusAsync(string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<bool> CancelRefineJobAsync(string jobId, CancellationToken ct = default)
    {
        return Task.FromResult(false);
    }
}
