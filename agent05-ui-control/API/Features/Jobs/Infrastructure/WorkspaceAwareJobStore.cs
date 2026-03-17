using System.Text;

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
            var result = GetResultFromDir(dirPath);

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
                Result = result
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

    /// <summary>Find "audio.*" in job dir and return that filename for display (e.g. "audio.mp3").</summary>
    private static string? GetOriginalFilenameFromDir(string dirPath)
    {
        try
        {
            var dir = new DirectoryInfo(dirPath);
            var audioFile = dir.EnumerateFiles("audio.*").FirstOrDefault();
            return audioFile?.Name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Read transcript.md and transcript_fixed.md from job dir to populate Result for archive jobs.</summary>
    private static Application.JobResult? GetResultFromDir(string dirPath)
    {
        string? transcript = null;
        string? transcriptFixed = null;
        try
        {
            var transcriptPath = Path.Combine(dirPath, "transcript.md");
            if (File.Exists(transcriptPath))
                transcript = File.ReadAllText(transcriptPath, Encoding.UTF8);
            var fixedPath = Path.Combine(dirPath, "transcript_fixed.md");
            if (File.Exists(fixedPath))
                transcriptFixed = File.ReadAllText(fixedPath, Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
        if (transcript == null && transcriptFixed == null)
            return null;
        return new Application.JobResult
        {
            Transcript = transcript,
            TranscriptFixed = transcriptFixed
        };
    }
}
