namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class StubPipeline : Application.IPipeline
{
    public Task RunAsync(string jobId, CancellationToken ct = default)
    {
        // Stub: no-op. Real implementation will call agent04/agent06.
        return Task.CompletedTask;
    }
}
