using System.Collections.Concurrent;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class InMemoryJobStatusStore : IJobStatusStore
{
    private readonly ConcurrentDictionary<string, JobStatus> _jobs = new();

    public string Create(IReadOnlyList<string>? tags = null, string? callbackUrl = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        _jobs[id] = new JobStatus
        {
            JobId = id,
            State = JobState.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            Tags = tags ?? Array.Empty<string>(),
            CallbackUrl = callbackUrl
        };
        return id;
    }

    public void Update(string jobId, JobStatusUpdate update)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return;
        var now = DateTimeOffset.UtcNow;
        if (update.State.HasValue) job.State = update.State.Value;
        if (update.ProgressPercent.HasValue) job.ProgressPercent = update.ProgressPercent.Value;
        if (update.CurrentPhase != null) job.CurrentPhase = update.CurrentPhase;
        if (update.TotalChunks.HasValue) job.TotalChunks = update.TotalChunks.Value;
        if (update.ProcessedChunks.HasValue) job.ProcessedChunks = update.ProcessedChunks.Value;
        if (update.MdOutputPath != null) job.MdOutputPath = update.MdOutputPath;
        if (update.JsonOutputPath != null) job.JsonOutputPath = update.JsonOutputPath;
        if (update.ErrorMessage != null) job.ErrorMessage = update.ErrorMessage;
        if (update.State == JobState.Running && !job.StartedAt.HasValue) job.StartedAt = now;
        if (update.State is JobState.Completed or JobState.Failed or JobState.Cancelled) job.CompletedAt = now;
        job.UpdatedAt = now;
    }

    public JobStatus? Get(string jobId) => _jobs.TryGetValue(jobId, out var j) ? j : null;

    public IReadOnlyList<JobStatus> List(JobListFilter? filter)
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
