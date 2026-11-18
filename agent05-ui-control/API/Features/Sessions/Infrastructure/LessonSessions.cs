using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Features.Sessions.Infrastructure;

/// <summary>The lessons of the recordings folder together with the jobs that transcribed them.</summary>
public static class LessonSessions
{
    /// <summary>Every lesson with the runs of its tracks and the dialogues merged from them, newest lesson first.</summary>
    public static async Task<IReadOnlyList<RecordingSession>> WithRunsAsync(
        IRecordingsCatalog catalog,
        IJobStore store,
        IJobWorkspace workspace,
        CancellationToken ct = default)
    {
        var sessions = catalog.Scan();
        var jobs = await store.ListAsync(new JobListFilter(Limit: int.MaxValue), ct);
        var index = TrackRunsIndex.Build(workspace.WorkspaceRootPath, jobs, catalog.RootPath);
        return sessions
            .Select(s => s with { Periods = s.Periods.Select(index.Attach).ToList() })
            .ToList();
    }
}
