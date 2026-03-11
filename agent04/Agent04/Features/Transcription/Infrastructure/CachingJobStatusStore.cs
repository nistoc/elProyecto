using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agent04.Features.Transcription.Application;
using Microsoft.Extensions.Caching.Memory;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Decorator over IJobStatusStore that caches Get and List for 0.01–10 Hz query load.
/// Short TTL for active jobs (Running/Pending), longer for Completed/Failed.
/// Invalidates job cache on Update; list cache invalidated via generation counter.
/// </summary>
public sealed class CachingJobStatusStore : IJobStatusStore
{
    private const int TtlActiveSeconds = 2;
    private const int TtlTerminalMinutes = 5;
    private const string JobKeyPrefix = "job:";
    private const string ListKeyPrefix = "list:";

    private readonly IJobStatusStore _inner;
    private readonly IMemoryCache _cache;
    private int _listGeneration;

    public CachingJobStatusStore(IJobStatusStore inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public string Create(IReadOnlyList<string>? tags = null) => _inner.Create(tags);

    public void Update(string jobId, JobStatusUpdate update)
    {
        _inner.Update(jobId, update);
        _cache.Remove(JobKeyPrefix + jobId);
        Interlocked.Increment(ref _listGeneration);
    }

    public JobStatus? Get(string jobId)
    {
        var key = JobKeyPrefix + jobId;
        if (_cache.TryGetValue(key, out JobStatus? cached))
            return cached;
        var job = _inner.Get(jobId);
        if (job == null) return null;
        var ttl = job.State is JobState.Running or JobState.Pending
            ? TimeSpan.FromSeconds(TtlActiveSeconds)
            : TimeSpan.FromMinutes(TtlTerminalMinutes);
        _cache.Set(key, job, ttl);
        return job;
    }

    public IReadOnlyList<JobStatus> List(JobListFilter? filter)
    {
        var gen = Volatile.Read(ref _listGeneration);
        var key = ListKeyPrefix + gen + ":" + FilterHash(filter);
        if (_cache.TryGetValue(key, out IReadOnlyList<JobStatus>? cached) && cached != null)
            return cached;
        var list = _inner.List(filter);
        var ttl = FilterIncludesActive(filter) ? TimeSpan.FromSeconds(TtlActiveSeconds) : TimeSpan.FromMinutes(TtlTerminalMinutes);
        _cache.Set(key, list, ttl);
        return list;
    }

    private static bool FilterIncludesActive(JobListFilter? filter)
    {
        if (filter == null) return true;
        if (filter.Status is JobState.Running or JobState.Pending) return true;
        if (!filter.Status.HasValue) return true; // no filter = may include active
        return false;
    }

    private static string FilterHash(JobListFilter? filter)
    {
        if (filter == null) return "all";
        var fromStr = filter.From?.ToString("O");
        var toStr = filter.To?.ToString("O");
        var json = JsonSerializer.Serialize(new
        {
            filter.Status,
            filter.Tag,
            From = fromStr,
            To = toStr,
            filter.Limit,
            filter.Offset
        });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes.AsSpan(0, 16));
    }
}
