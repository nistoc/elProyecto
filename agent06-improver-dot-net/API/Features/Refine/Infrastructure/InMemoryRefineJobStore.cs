using System.Collections.Concurrent;
using System.Linq;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class InMemoryRefineJobStore : IRefineJobStore
{
    private readonly ConcurrentDictionary<string, RefineJobStatus> _jobs = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<RefineJobStatus>> _streamSnapshotsByJob = new();

    public string Create(IReadOnlyList<string>? tags = null, string? callbackUrl = null, string? jobDirectoryRelative = null, string? workspaceRootOverride = null)
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
            CallbackUrl = callbackUrl,
            JobDirectoryRelative = string.IsNullOrWhiteSpace(jobDirectoryRelative) ? null : jobDirectoryRelative.Trim(),
            WorkspaceRootOverride = string.IsNullOrWhiteSpace(workspaceRootOverride) ? null : workspaceRootOverride.Trim()
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
        if (update.State is RefineJobState.Completed or RefineJobState.Failed or RefineJobState.Cancelled)
            job.CompletedAt = now;
        if (update.ClearBatchEvent)
        {
            job.BatchEventKind = null;
            job.BatchEventIndex0 = -1;
            job.BatchThreadsRelativePath = null;
        }
        if (update.BatchEventKind != null)
        {
            job.BatchEventKind = update.BatchEventKind;
            if (update.BatchEventIndex0.HasValue) job.BatchEventIndex0 = update.BatchEventIndex0.Value;
            if (update.BatchThreadsRelativePath != null) job.BatchThreadsRelativePath = update.BatchThreadsRelativePath;
        }
        if (update.OpenAiRequestPreview != null)
            job.OpenAiRequestPreview = update.OpenAiRequestPreview;
        if (update.HasBatchBeforeText)
            job.BatchBeforeText = update.BatchBeforeText ?? "";
        if (update.HasBatchAfterText)
            job.BatchAfterText = update.BatchAfterText;
        if (update.RefinerLogLine != null)
            job.RefinerLogLine = update.RefinerLogLine;
        job.StreamSequence++;
        job.UpdatedAt = now;

        var q = _streamSnapshotsByJob.GetOrAdd(jobId, static _ => new ConcurrentQueue<RefineJobStatus>());
        q.Enqueue(CloneForStream(job));
    }

    public bool TryDequeueStreamSnapshot(string jobId, out RefineJobStatus? snapshot)
    {
        snapshot = null;
        if (!_streamSnapshotsByJob.TryGetValue(jobId, out var q) || !q.TryDequeue(out var item))
            return false;
        snapshot = item;
        return true;
    }

    /// <summary>Shallow copy of fields needed for gRPC ToResponse — safe to send after dequeue.</summary>
    private static RefineJobStatus CloneForStream(RefineJobStatus j) =>
        new()
        {
            JobId = j.JobId,
            State = j.State,
            ProgressPercent = j.ProgressPercent,
            CurrentPhase = j.CurrentPhase,
            CurrentBatch = j.CurrentBatch,
            TotalBatches = j.TotalBatches,
            CreatedAt = j.CreatedAt,
            StartedAt = j.StartedAt,
            CompletedAt = j.CompletedAt,
            UpdatedAt = j.UpdatedAt,
            OutputFilePath = j.OutputFilePath,
            ErrorMessage = j.ErrorMessage,
            CallbackUrl = j.CallbackUrl,
            Tags = j.Tags is { Count: > 0 } ? j.Tags.ToArray() : Array.Empty<string>(),
            JobDirectoryRelative = j.JobDirectoryRelative,
            WorkspaceRootOverride = j.WorkspaceRootOverride,
            StreamSequence = j.StreamSequence,
            BatchEventKind = j.BatchEventKind,
            BatchEventIndex0 = j.BatchEventIndex0,
            BatchThreadsRelativePath = j.BatchThreadsRelativePath,
            OpenAiRequestPreview = j.OpenAiRequestPreview,
            BatchBeforeText = j.BatchBeforeText,
            BatchAfterText = j.BatchAfterText,
            RefinerLogLine = j.RefinerLogLine,
        };

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
