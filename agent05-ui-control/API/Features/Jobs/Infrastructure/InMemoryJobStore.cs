using System.Collections.Concurrent;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class InMemoryJobStore : Application.IJobStore
{
    private readonly ConcurrentDictionary<string, Application.JobSnapshot> _jobs = new();

    public Task<Application.JobSnapshot?> GetAsync(string jobId, CancellationToken ct = default)
    {
        _jobs.TryGetValue(jobId, out var snap);
        return Task.FromResult(snap);
    }

    public Task<IReadOnlyList<Application.JobListItem>> ListAsync(Application.JobListFilter filter, CancellationToken ct = default)
    {
        var list = _jobs.Values
            .Where(j => Filter(j, filter))
            .OrderByDescending(j => j.Id)
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
        update(snap);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string jobId, CancellationToken ct = default)
    {
        return Task.FromResult(_jobs.TryRemove(jobId, out _));
    }
}
