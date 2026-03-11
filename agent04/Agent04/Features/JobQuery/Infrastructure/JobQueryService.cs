using Agent04.Features.JobQuery.Application;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.JobQuery.Infrastructure;

public sealed class JobQueryService : IJobQueryService
{
    private readonly IJobStatusStore _store;

    public JobQueryService(IJobStatusStore store)
    {
        _store = store;
    }

    public JobStatus? GetById(string jobId) => _store.Get(jobId);

    public IReadOnlyList<JobStatus> Query(JobListFilter filter) => _store.List(filter);

    public IReadOnlyList<JobStatus> QueryBySemanticKey(
        string semanticKey,
        JobState? status = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 50,
        int offset = 0)
    {
        var filter = new JobListFilter
        {
            SemanticKey = semanticKey,
            Status = status,
            From = from,
            To = to,
            Limit = limit,
            Offset = offset
        };
        return _store.List(filter);
    }
}
