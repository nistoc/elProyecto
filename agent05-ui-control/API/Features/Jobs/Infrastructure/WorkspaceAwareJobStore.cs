namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Wraps an in-memory job store and merges the list with job directories found on disk (Jobs:WorkspacePath),
/// so that "archive" jobs created by other runs or agent-browser appear in the API list.
/// </summary>
public sealed class WorkspaceAwareJobStore : Application.IJobStore
{
    private readonly Application.IJobStore _inner;
    private readonly Application.IJobWorkspace _workspace;
    private readonly ILogger<WorkspaceAwareJobStore> _logger;

    public WorkspaceAwareJobStore(
        Application.IJobStore inner,
        Application.IJobWorkspace workspace,
        ILogger<WorkspaceAwareJobStore> logger)
    {
        _inner = inner;
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<Application.JobSnapshot?> GetAsync(string jobId, CancellationToken ct = default)
    {
        var snap = await _inner.GetAsync(jobId, ct);
        if (snap != null)
            return snap;
        var dirPath = _workspace.GetJobDirectoryPath(jobId);
        if (!Directory.Exists(dirPath))
            return null;
        try
        {
            var created = Directory.GetCreationTimeUtc(dirPath);
            return new Application.JobSnapshot
            {
                Id = jobId,
                Status = "completed",
                Phase = "idle",
                OriginalFilename = null,
                CreatedAt = created.ToString("O"),
                CompletedAt = null,
                Tags = null
            };
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read archive job directory {JobId}", jobId);
            return null;
        }
    }

    public async Task<IReadOnlyList<Application.JobListItem>> ListAsync(Application.JobListFilter filter, CancellationToken ct = default)
    {
        var allFilter = new Application.JobListFilter(
            filter.SemanticKey, filter.Status, filter.From, filter.To,
            Limit: int.MaxValue, Offset: 0);
        var fromMemory = await _inner.ListAsync(allFilter, ct);
        var memoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var j in fromMemory)
            memoryIds.Add(j.Id);

        IReadOnlyList<(string JobId, DateTime CreatedUtc)> dirs;
        try
        {
            dirs = await _workspace.ListJobDirectoriesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list job directories from workspace");
            return fromMemory.Skip(filter.Offset).Take(filter.Limit).ToList();
        }

        var archiveItems = new List<Application.JobListItem>();
        foreach (var (jobId, createdUtc) in dirs)
        {
            if (memoryIds.Contains(jobId))
                continue;
            var createdAt = createdUtc.ToString("O");
            if (!string.IsNullOrEmpty(filter.SemanticKey))
                continue;
            if (!string.IsNullOrEmpty(filter.Status) && !string.Equals("completed", filter.Status, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrEmpty(filter.From) && string.CompareOrdinal(createdAt, filter.From) < 0)
                continue;
            if (!string.IsNullOrEmpty(filter.To) && string.CompareOrdinal(createdAt, filter.To) > 0)
                continue;
            archiveItems.Add(new Application.JobListItem(
                jobId,
                "—",
                "completed",
                "idle",
                createdAt,
                null,
                null));
        }

        var merged = fromMemory.Concat(archiveItems)
            .OrderByDescending(j => j.CreatedAt ?? "")
            .Skip(filter.Offset)
            .Take(filter.Limit)
            .ToList();
        return merged;
    }

    public Task<string> CreateAsync(Application.JobCreateInput input, CancellationToken ct = default)
        => _inner.CreateAsync(input, ct);

    public Task<bool> UpdateAsync(string jobId, Action<Application.JobSnapshot> update, CancellationToken ct = default)
        => _inner.UpdateAsync(jobId, update, ct);

    public Task<bool> DeleteAsync(string jobId, CancellationToken ct = default)
        => _inner.DeleteAsync(jobId, ct);
}
