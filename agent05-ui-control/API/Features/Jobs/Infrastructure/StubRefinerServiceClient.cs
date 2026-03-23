namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class StubRefinerServiceClient : Application.IRefinerServiceClient
{
    public Task<Application.SubmitRefineJobResult> SubmitRefineJobAsync(Application.RefineJobInput input, CancellationToken ct = default)
    {
        return Task.FromResult(new Application.SubmitRefineJobResult("stub-refine-job"));
    }

    public Task<Application.RefineStatusUpdate> GetRefineStatusAsync(string jobId, CancellationToken ct = default) =>
        Task.FromResult(new Application.RefineStatusUpdate(
            jobId,
            "Completed",
            100,
            null,
            0,
            0,
            null,
            null,
            null,
            0,
            null,
            0,
            null,
            null,
            null,
            null,
            null));

    public async IAsyncEnumerable<Application.RefineStatusUpdate> StreamRefineStatusAsync(
        string jobId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return await GetRefineStatusAsync(jobId, ct).ConfigureAwait(false);
    }

    public Task<bool> CancelRefineJobAsync(string jobId, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<bool> PauseRefineJobAsync(string jobId, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<bool> ResumeRefineJobAsync(string jobId, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<Application.SubmitRefineJobResult> ResumeRefineFromCheckpointAsync(
        string jobDirectoryRelative,
        string workspaceRootOverride,
        CancellationToken ct = default) =>
        Task.FromResult(new Application.SubmitRefineJobResult("stub-refine-from-ckpt"));
}
