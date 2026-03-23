using System.Text;
using Microsoft.Extensions.Hosting;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class JobWorkspace : Application.IJobWorkspace
{
    private readonly string _basePath;
    private readonly ILogger<JobWorkspace> _logger;

    public JobWorkspace(IConfiguration configuration, IHostEnvironment environment, ILogger<JobWorkspace> logger)
    {
        var raw = configuration["Jobs:WorkspacePath"]?.Trim();
        if (string.IsNullOrEmpty(raw))
            raw = "./runtime";
        // Relative path: resolve from content root (running app). Absolute path: use as-is.
        _basePath = Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath ?? Directory.GetCurrentDirectory(), raw));
        _logger = logger;
        _logger.LogInformation("Job workspace base path (Jobs:WorkspacePath): {Path}", _basePath);
    }

    /// <inheritdoc />
    public string WorkspaceRootPath => _basePath;

    public string GetJobDirectoryPath(string jobId)
    {
        return Path.Combine(_basePath, jobId);
    }

    public Task EnsureJobDirectoryAsync(string jobId, CancellationToken ct = default)
    {
        var dir = GetJobDirectoryPath(jobId);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            _logger.LogInformation("Created job directory: {Path}", dir);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Single-segment file name safe for the job directory. Strips any path, replaces invalid characters,
    /// and matches what <see cref="SaveUploadedFileAsync"/> writes so the transcription pipeline can resolve the file.
    /// </summary>
    public static string SanitizeUploadedFileName(string? originalFileName)
    {
        var raw = Path.GetFileName((originalFileName ?? "").Trim());
        var ext = Path.GetExtension(raw);
        if (string.IsNullOrEmpty(ext))
            ext = ".bin";

        var stem = Path.GetFileNameWithoutExtension(raw);
        if (string.IsNullOrEmpty(stem))
            stem = "audio";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(stem.Length);
        foreach (var c in stem)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        stem = sb.ToString().Trim(' ', '.');
        if (string.IsNullOrEmpty(stem))
            stem = "audio";

        stem = EnsureNonReservedWindowsStem(stem);

        const int maxLen = 255;
        var maxStem = maxLen - ext.Length;
        if (maxStem < 1)
            return "audio" + ext;
        if (stem.Length > maxStem)
            stem = stem[..maxStem].TrimEnd(' ', '.');
        if (string.IsNullOrEmpty(stem))
            stem = "audio";

        return stem + ext;
    }

    private static string EnsureNonReservedWindowsStem(string stem)
    {
        var upper = stem.ToUpperInvariant();
        if (upper is "CON" or "PRN" or "AUX" or "NUL")
            return "_" + stem;
        if (upper.Length == 4
            && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal))
            && char.IsDigit(upper[3]))
            return "_" + stem;
        return stem;
    }

    public async Task<string> SaveUploadedFileAsync(string jobId, Stream source, string originalFileName, CancellationToken ct = default)
    {
        await EnsureJobDirectoryAsync(jobId, ct);
        var dir = GetJobDirectoryPath(jobId);
        var safeName = SanitizeUploadedFileName(originalFileName);
        var fullPath = Path.Combine(dir, safeName);
        await using var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await source.CopyToAsync(file, ct);
        return safeName;
    }

    public Task<IReadOnlyList<(string JobId, DateTime CreatedUtc)>> ListJobDirectoriesAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_basePath))
            return Task.FromResult<IReadOnlyList<(string JobId, DateTime CreatedUtc)>>(Array.Empty<(string, DateTime)>());
        var list = new List<(string JobId, DateTime CreatedUtc)>();
        foreach (var dir in Directory.EnumerateDirectories(_basePath))
        {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name)) continue;
            try
            {
                var created = Directory.GetCreationTimeUtc(dir);
                list.Add((name, created));
            }
            catch (IOException) { /* skip inaccessible */ }
        }
        return Task.FromResult<IReadOnlyList<(string JobId, DateTime CreatedUtc)>>(list);
    }

    /// <inheritdoc />
    public Task<bool> TryDeleteJobDirectoryAsync(string jobId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return Task.FromResult(false);
        // Reject path segments in job id (only flat names under workspace, as created by the API / lister)
        if (jobId.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\']) >= 0)
            return Task.FromResult(false);
        if (jobId is "." or "..")
            return Task.FromResult(false);

        var full = Path.GetFullPath(GetJobDirectoryPath(jobId));
        var root = Path.GetFullPath(_basePath);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var underRoot = full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
        if (!underRoot)
        {
            _logger.LogWarning("TryDeleteJobDirectoryAsync: path outside workspace, refusing: {Full}", full);
            return Task.FromResult(false);
        }

        if (!Directory.Exists(full))
            return Task.FromResult(false);

        try
        {
            Directory.Delete(full, recursive: true);
            _logger.LogInformation("Deleted job directory: {Path}", full);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete job directory {Path}", full);
            return Task.FromResult(false);
        }
    }
}
