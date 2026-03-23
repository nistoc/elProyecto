using System.Collections.Concurrent;
using TranslationImprover.Features.Refine.Application;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class InMemoryRefineJobCancellation : IRefineJobCancellation, IRefineJobPause
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _byJob = new();
    private readonly ConcurrentDictionary<string, bool> _pauseRequested = new();

    public void Register(string jobId, CancellationTokenSource cts)
    {
        if (_byJob.TryRemove(jobId, out var old))
        {
            try { old.Dispose(); } catch { /* ignore */ }
        }
        _byJob[jobId] = cts;
    }

    public bool TryCancel(string jobId)
    {
        if (!_byJob.TryRemove(jobId, out var cts)) return false;
        try { cts.Cancel(); } catch { /* ignore */ }
        try { cts.Dispose(); } catch { /* ignore */ }
        _pauseRequested.TryRemove(jobId, out _);
        return true;
    }

    public void RequestPause(string jobId) => _pauseRequested[jobId] = true;

    public bool IsPauseRequested(string jobId) =>
        _pauseRequested.TryGetValue(jobId, out var v) && v;

    public void ClearPauseRequest(string jobId) => _pauseRequested.TryRemove(jobId, out _);
}
