namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// File system workspace for job directories (uploaded files, outputs).
/// Paths are under the configured Jobs:WorkspacePath.
/// </summary>
public interface IJobWorkspace
{
    /// <summary>Full path to the job directory (e.g. .../runtime/{jobId}).</summary>
    string GetJobDirectoryPath(string jobId);

    /// <summary>Creates the job directory if it does not exist.</summary>
    Task EnsureJobDirectoryAsync(string jobId, CancellationToken ct = default);

    /// <summary>Saves the uploaded file into the job directory. Returns the relative file name under the job dir.</summary>
    Task<string> SaveUploadedFileAsync(string jobId, Stream source, string originalFileName, CancellationToken ct = default);

    /// <summary>Lists existing job directory ids and their creation time (for archive/listing from disk).</summary>
    Task<IReadOnlyList<(string JobId, DateTime CreatedUtc)>> ListJobDirectoriesAsync(CancellationToken ct = default);
}
