namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// Resolves the filesystem root for per-job artifacts (refiner_threads, outputs, refiner_debug.log).
/// Uses the same configured WorkspaceRoot as the rest of the service unless gRPC sends <c>workspace_root_override</c>.
/// </summary>
public static class RefineArtifactBaseResolver
{
    /// <returns>Artifact base directory and value to persist in the job request for resume.</returns>
    public static (string ArtifactBase, string? WorkspaceRootOverrideStored) Resolve(
        string configuredWorkspaceRoot,
        string? workspaceRootOverride,
        string? jobDirectoryRelative)
    {
        if (!string.IsNullOrWhiteSpace(workspaceRootOverride))
        {
            var b = Path.GetFullPath(workspaceRootOverride.Trim());
            return (b, b);
        }

        if (!string.IsNullOrWhiteSpace(jobDirectoryRelative))
        {
            var b = Path.GetFullPath(configuredWorkspaceRoot.Trim());
            return (b, b);
        }

        return (configuredWorkspaceRoot, null);
    }
}
