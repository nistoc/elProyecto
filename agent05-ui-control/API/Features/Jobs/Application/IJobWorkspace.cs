namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// File system workspace for job directories (uploaded files, outputs).
/// Paths are under the configured Jobs:WorkspacePath.
/// </summary>
public interface IJobWorkspace
{
    /// <summary>Absolute path to the jobs workspace root (configured Jobs:WorkspacePath), parent of all job directories.</summary>
    string WorkspaceRootPath { get; }

    /// <summary>Full path to the job directory (e.g. .../runtime/{jobId}).</summary>
    string GetJobDirectoryPath(string jobId);

    /// <summary>Creates the job directory if it does not exist.</summary>
    Task EnsureJobDirectoryAsync(string jobId, CancellationToken ct = default);

    /// <summary>Saves the uploaded file into the job directory. Returns the relative file name under the job dir.</summary>
    Task<string> SaveUploadedFileAsync(string jobId, Stream source, string originalFileName, CancellationToken ct = default);

    /// <summary>Lists existing job directory ids and their creation time (for archive/listing from disk).</summary>
    Task<IReadOnlyList<(string JobId, DateTime CreatedUtc)>> ListJobDirectoriesAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes the job directory under the workspace if it exists (recursive).
    /// Returns true if a directory was removed. Path must stay under <see cref="WorkspaceRootPath"/>.
    /// </summary>
    Task<bool> TryDeleteJobDirectoryAsync(string jobId, CancellationToken ct = default);
}
