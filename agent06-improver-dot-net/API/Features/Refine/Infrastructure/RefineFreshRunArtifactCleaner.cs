using Microsoft.Extensions.Logging;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// Agent06 owns <c>refiner_threads/</c> and <see cref="RefineDebugLog"/>. Call synchronously before starting <see cref="RefinePipeline.RunAsync"/> (gRPC/HTTP submit),
/// so clients do not read stale files after Submit returns. Do not call on resume (<see cref="RefinePipeline.ResumeAsync"/> / <see cref="RefinePipeline.ResumeFromCheckpointAsync"/>).
/// </summary>
public static class RefineFreshRunArtifactCleaner
{
    public static void ClearForNewSubmit(string artifactRoot, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(artifactRoot))
            return;

        var threadsDir = RefinePaths.RefinerThreadsDir(artifactRoot);
        if (Directory.Exists(threadsDir))
        {
            foreach (var path in Directory.EnumerateFiles(threadsDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to delete refiner_threads file {Path}", path);
                }
            }
        }

        var debugLog = Path.Combine(artifactRoot, RefineDebugLog.FileName);
        if (File.Exists(debugLog))
        {
            try
            {
                File.Delete(debugLog);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to delete {File}", RefineDebugLog.FileName);
            }
        }

        logger?.LogInformation(
            "Cleared refiner_threads and {File} for new refine run under {Root}",
            RefineDebugLog.FileName,
            artifactRoot);
    }
}
