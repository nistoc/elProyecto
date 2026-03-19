namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// Resolves per-job artifact directory under a workspace root (single segment, e.g. Xtract job id).
/// </summary>
public static class RefineWorkspacePaths
{
    public static string ResolveEffectiveArtifactRoot(string workspaceRootFull, string? jobDirectoryRelative)
    {
        var root = Path.GetFullPath(workspaceRootFull.Trim());
        if (string.IsNullOrWhiteSpace(jobDirectoryRelative))
            return root;

        var rel = jobDirectoryRelative.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (rel.Contains("..", StringComparison.Ordinal) || rel.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            throw new ArgumentException("job_directory_relative must be a single path segment.", nameof(jobDirectoryRelative));

        var combined = Path.GetFullPath(Path.Combine(root, rel));
        var back = Path.GetRelativePath(root, combined);
        if (back.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(back))
            throw new ArgumentException("job_directory_relative resolves outside workspace_root.", nameof(jobDirectoryRelative));
        return combined;
    }
}
