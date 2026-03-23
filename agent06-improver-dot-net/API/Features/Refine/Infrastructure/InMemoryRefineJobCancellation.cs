using System.Collections.Concurrent;
using TranslationImprover.Features.Refine.Application;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class InMemoryRefineJobCancellation : IRefineJobCancellation
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _byJob = new();

    public void Register(string jobId, CancellationTokenSource cts)
    {
        _byJob[jobId] = cts;
    }

    public bool TryCancel(string jobId)
    {
        if (!_byJob.TryRemove(jobId, out var cts)) return false;
        try { cts.Cancel(); } catch { /* ignore */ }
        try { cts.Dispose(); } catch { /* ignore */ }
        return true;
    }
}
