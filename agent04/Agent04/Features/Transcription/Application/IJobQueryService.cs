namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Query jobs by semantic key (tag) and filters for virtual model / UI (0.01–10 Hz).
/// </summary>
public interface IJobQueryService
{
    JobStatus? GetById(string jobId);
    IReadOnlyList<JobStatus> Query(JobListFilter filter);
}
