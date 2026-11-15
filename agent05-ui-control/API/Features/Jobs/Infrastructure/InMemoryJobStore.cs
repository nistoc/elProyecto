using System.Collections.Concurrent;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class InMemoryJobStore : Application.IAdoptingJobStore
{
    private readonly ConcurrentDictionary<string, Application.JobSnapshot> _jobs = new();
    private readonly ConcurrentDictionary<string, object> _locks = new();

    private object LockFor(string jobId) => _locks.GetOrAdd(jobId, _ => new object());

    public Task<Application.JobSnapshot?> GetAsync(string jobId, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var snap))
            return Task.FromResult<Application.JobSnapshot?>(null);
        // Readers get a deep copy: writers keep mutating the stored instance under the per-job lock,
        // so serializing a shared snapshot mid-update can no longer tear.
        lock (LockFor(jobId))
        {
            return Task.FromResult<Application.JobSnapshot?>(snap.Clone());
        }
    }

    public Task<IReadOnlyList<Application.JobListItem>> ListAsync(Application.JobListFilter filter, CancellationToken ct = default)
    {
        var copies = new List<Application.JobSnapshot>();
        foreach (var (jobId, snap) in _jobs)
        {
            lock (LockFor(jobId))
            {
                copies.Add(snap.Clone());
            }
        }

        var list = copies
            .Where(j => Filter(j, filter))
            .OrderByDescending(j => j.CreatedAt ?? "")
            .Skip(filter.Offset)
            .Take(filter.Limit)
            .Select(j => new Application.JobListItem(
                j.Id,
                j.OriginalFilename ?? "",
                j.Status,
                j.Phase,
                j.CreatedAt,
                j.CompletedAt,
                j.Tags))
            .ToList();
        return Task.FromResult<IReadOnlyList<Application.JobListItem>>(list);
    }

    private static bool Filter(Application.JobSnapshot j, Application.JobListFilter filter)
    {
        if (!string.IsNullOrEmpty(filter.SemanticKey))
        {
            if (j.Tags == null || !j.Tags.Any(t => string.Equals(t, filter.SemanticKey, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        if (!string.IsNullOrEmpty(filter.Status) && !string.Equals(j.Status, filter.Status, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(filter.From) && string.CompareOrdinal(j.CreatedAt ?? "", filter.From) < 0)
            return false;
        if (!string.IsNullOrEmpty(filter.To) && string.CompareOrdinal(j.CreatedAt ?? "", filter.To) > 0)
            return false;
        return true;
    }

    public Task<string> CreateAsync(Application.JobCreateInput input, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var createdAt = DateTime.UtcNow.ToString("O");
        var snap = new Application.JobSnapshot
        {
            Id = id,
            Status = "queued",
            Phase = "idle",
            OriginalFilename = input.OriginalFilename,
            Tags = input.Tags?.ToList(),
            CreatedAt = createdAt
        };
        _jobs[id] = snap;
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(string jobId, Action<Application.JobSnapshot> update, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(jobId, out var snap))
            return Task.FromResult(false);
        lock (LockFor(jobId))
        {
            update(snap);
        }
        return Task.FromResult(true);
    }

    public bool TryAdopt(Application.JobSnapshot snapshot) => _jobs.TryAdd(snapshot.Id, snapshot);

    public Task<bool> DeleteAsync(string jobId, CancellationToken ct = default)
    {
        var removed = _jobs.TryRemove(jobId, out _);
        _locks.TryRemove(jobId, out _);
        return Task.FromResult(removed);
    }
}
