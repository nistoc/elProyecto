using Agent04.Features.Transcription.Application;

namespace Agent04.Features.JobQuery.Application;

/// <summary>
/// Query jobs by semantic key (tag) and filters for virtual model / UI (0.01–10 Hz).
/// </summary>
public interface IJobQueryService
{
    JobStatus? GetById(string jobId);
    IReadOnlyList<JobStatus> Query(JobListFilter filter);

    /// <summary>
    /// Query jobs by semantic key (tag) with optional filters. Aligns with plan naming.
    /// </summary>
    IReadOnlyList<JobStatus> QueryBySemanticKey(
        string semanticKey,
        JobState? status = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 50,
        int offset = 0);
}
