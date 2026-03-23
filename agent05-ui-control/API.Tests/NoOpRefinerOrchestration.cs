using XtractManager.Features.Jobs.Application;

namespace XtractManager.Tests;

internal sealed class NoOpRefinerOrchestration : IRefinerOrchestration
{
    public Task StartRefinerAsync(string jobId, string? transcriptRelativePath = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task PauseRefinerAsync(string jobId, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResumeRefinerAsync(string jobId, CancellationToken ct = default) => Task.CompletedTask;
    public Task SkipRefinerAsync(string jobId, CancellationToken ct = default) => Task.CompletedTask;
}
