using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XtractManager.Infrastructure;

/// <summary>
/// Ensures Jobs:WorkspacePath and Agent06:WorkspaceRoot refer to the same folder when both are set (typical Xtract + Agent06 layout).
/// </summary>
public static class WorkspaceParityChecker
{
    public static void ValidateAtStartup(IConfiguration configuration, IHostEnvironment hostEnvironment, ILogger logger)
    {
        var jobsRaw = configuration["Jobs:WorkspacePath"]?.Trim();
        var a06Raw = configuration["Agent06:WorkspaceRoot"]?.Trim();
        if (string.IsNullOrWhiteSpace(jobsRaw) || string.IsNullOrWhiteSpace(a06Raw))
            return;

        var jobsFull = NormalizeResolved(ResolveConfigurationPath(jobsRaw, hostEnvironment));
        var a06Full = NormalizeResolved(ResolveConfigurationPath(a06Raw, hostEnvironment));

        if (string.Equals(jobsFull, a06Full, StringComparison.OrdinalIgnoreCase))
            return;

        logger.LogWarning(
            "Jobs:WorkspacePath ({Jobs}) differs from Agent06:WorkspaceRoot ({Agent06}). Per-job files from Agent04 may not align with Agent06 refine I/O.",
            jobsFull, a06Full);

        if (configuration.GetValue("Xtract:RequireAgent06WorkspaceMatchesJobs", false))
            throw new InvalidOperationException(
                "Xtract:RequireAgent06WorkspaceMatchesJobs is true but Jobs:WorkspacePath and Agent06:WorkspaceRoot resolve to different directories.");
    }

    private static string NormalizeResolved(string fullPath)
    {
        var p = Path.GetFullPath(fullPath);
        return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ResolveConfigurationPath(string raw, IHostEnvironment hostEnvironment)
    {
        if (Path.IsPathRooted(raw))
            return Path.GetFullPath(raw);
        var contentRoot = hostEnvironment.ContentRootPath ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(contentRoot, raw));
    }
}
