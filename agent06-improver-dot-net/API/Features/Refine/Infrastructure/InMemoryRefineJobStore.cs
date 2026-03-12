using System.Collections.Concurrent;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class InMemoryRefineJobStore : IRefineJobStore
{
    private readonly ConcurrentDictionary<string, RefineJobStatus> _jobs = new();

    public string Create(IReadOnlyList<string>? tags = null, string? callbackUrl = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        _jobs[id] = new RefineJobStatus
        {
            JobId = id,
            State = RefineJobState.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            Tags = tags ?? Array.Empty<string>(),
            CallbackUrl = callbackUrl
        };
        return id;
    }

    public void Update(string jobId, RefineJobStatusUpdate update)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return;
        var now = DateTimeOffset.UtcNow;
        if (update.State.HasValue) job.State = update.State.Value;
        if (update.ProgressPercent.HasValue) job.ProgressPercent = update.ProgressPercent.Value;
        if (update.CurrentPhase != null) job.CurrentPhase = update.CurrentPhase;
        if (update.CurrentBatch.HasValue) job.CurrentBatch = update.CurrentBatch.Value;
        if (update.TotalBatches.HasValue) job.TotalBatches = update.TotalBatches.Value;
        if (update.OutputFilePath != null) job.OutputFilePath = update.OutputFilePath;
        if (update.ErrorMessage != null) job.ErrorMessage = update.ErrorMessage;
        if (update.State == RefineJobState.Running && !job.StartedAt.HasValue) job.StartedAt = now;
        if (update.State is RefineJobState.Completed or RefineJobState.Failed or RefineJobState.Cancelled) job.CompletedAt = now;
        job.UpdatedAt = now;
    }

    public RefineJobStatus? Get(string jobId) => _jobs.TryGetValue(jobId, out var j) ? j : null;

    public IReadOnlyList<RefineJobStatus> List(RefineJobListFilter? filter)
    {
        var list = _jobs.Values.AsEnumerable();
        if (filter?.Status is { } s)
            list = list.Where(x => x.State == s);
        if (!string.IsNullOrEmpty(filter?.SemanticKey))
        {
            var key = filter.SemanticKey;
            list = list.Where(x => x.Tags?.Contains(key, StringComparer.OrdinalIgnoreCase) == true);
        }
        if (filter?.From is { } from)
            list = list.Where(x => x.CreatedAt >= from);
        if (filter?.To is { } to)
            list = list.Where(x => x.CreatedAt <= to);
        list = list.OrderByDescending(x => x.CreatedAt).Skip(filter?.Offset ?? 0).Take(filter?.Limit ?? 50);
        return list.ToList();
    }
}
