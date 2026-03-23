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
        if (snap == null)
            snap = TryBuildArchiveSnapshot(jobId);
        if (snap == null)
            return null;

        var dirPath = _workspace.GetJobDirectoryPath(jobId);
        if (Directory.Exists(dirPath))
        {
            snap.JobDirectoryPath ??= dirPath;
            JobSnapshotDiskEnricher.TryEnrichFromDisk(snap, dirPath, _logger);
        }

        return snap;
    }

    private Application.JobSnapshot? TryBuildArchiveSnapshot(string jobId)
    {
        var dirPath = _workspace.GetJobDirectoryPath(jobId);
        if (!Directory.Exists(dirPath))
        {
            _logger.LogDebug("GetAsync({JobId}): directory does not exist: {Path}", jobId, dirPath);
            return null;
        }
        try
        {
            var created = Directory.GetCreationTimeUtc(dirPath);
            var filesInDir = Directory.EnumerateFileSystemEntries(dirPath).ToList();
            _logger.LogInformation(
                "GetAsync({JobId}): returning archive job from disk, JobDirectoryPath={Path}, fileCount={Count}, files=[{Files}]",
                jobId, dirPath, filesInDir.Count, string.Join(", ", filesInDir.Select(Path.GetFileName)));

            var originalFilename = GetOriginalFilenameFromDir(dirPath);

            return new Application.JobSnapshot
            {
                Id = jobId,
                Status = "completed",
                Phase = "idle",
                OriginalFilename = originalFilename,
                CreatedAt = created.ToString("O"),
                CompletedAt = null,
                Tags = null,
                JobDirectoryPath = dirPath,
            };
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read archive job directory {JobId} at {Path}", jobId, dirPath);
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
            _logger.LogDebug("ListAsync: workspace returned {Count} job directories", dirs.Count);
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
            var archiveDir = _workspace.GetJobDirectoryPath(jobId);
            var listDisplayName = GetOriginalFilenameFromDir(archiveDir) ?? jobId;
            archiveItems.Add(new Application.JobListItem(
                jobId,
                listDisplayName,
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

    public async Task<bool> UpdateAsync(string jobId, Action<Application.JobSnapshot> update, CancellationToken ct = default)
    {
        if (await _inner.UpdateAsync(jobId, update, ct))
            return true;

        // Archive-only jobs appear in GET/list via disk but live only as detached snapshots — refiner/transcription updates no-op'd.
        if (_inner is not InMemoryJobStore mem)
            return false;

        var dirPath = _workspace.GetJobDirectoryPath(jobId);
        if (string.IsNullOrWhiteSpace(dirPath) || !Directory.Exists(dirPath))
            return false;

        var snap = TryBuildArchiveSnapshot(jobId);
        if (snap == null)
            return false;

        snap.JobDirectoryPath ??= dirPath;
        JobSnapshotDiskEnricher.TryEnrichFromDisk(snap, dirPath, _logger);

        if (mem.TryInsertIfAbsent(jobId, snap))
            _logger.LogInformation(
                "UpdateAsync({JobId}): materialized archive job from disk into memory (was list-only / GET-only)",
                jobId);
        return await _inner.UpdateAsync(jobId, update, ct);
    }

    public async Task<bool> DeleteAsync(string jobId, CancellationToken ct = default)
    {
        var removedFromMemory = await _inner.DeleteAsync(jobId, ct);
        var removedFromDisk = await _workspace.TryDeleteJobDirectoryAsync(jobId, ct);
        if (removedFromDisk)
            _logger.LogDebug("DeleteAsync({JobId}): removed workspace directory (archive or leftover files)", jobId);
        return removedFromMemory || removedFromDisk;
    }

    /// <summary>Find first audio file in job dir and return its name for display (e.g. "audio.mp3" or original name from agent-browser).</summary>
    private static string? GetOriginalFilenameFromDir(string dirPath)
    {
        try
        {
            var dir = new DirectoryInfo(dirPath);
            var audioExtensions = new[] { ".m4a", ".mp3", ".wav", ".ogg", ".flac", ".bin" };
            var firstAudio = dir.EnumerateFiles()
                .FirstOrDefault(f => audioExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase));
            return firstAudio?.Name;
        }
        catch
        {
            return null;
        }
    }

}
