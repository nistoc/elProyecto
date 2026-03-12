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

    public async Task<string> SaveUploadedFileAsync(string jobId, Stream source, string originalFileName, CancellationToken ct = default)
    {
        await EnsureJobDirectoryAsync(jobId, ct);
        var dir = GetJobDirectoryPath(jobId);
        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext))
            ext = ".bin";
        var safeName = "audio" + ext;
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
}
