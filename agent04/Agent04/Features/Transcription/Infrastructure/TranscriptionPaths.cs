namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Resolves the per-job artifact directory (parent of the input audio) under a workspace root.
/// </summary>
public static class TranscriptionPaths
{
    /// <summary>
    /// Returns the full path to the directory containing <paramref name="inputFilePathFull"/>.
    /// Throws if the file does not exist or is not under <paramref name="workspaceRootFull"/>.
    /// </summary>
    public static string ResolveArtifactRoot(string workspaceRootFull, string inputFilePathFull)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootFull))
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRootFull));
        if (!File.Exists(inputFilePathFull))
            throw new FileNotFoundException("Input file not found.", inputFilePathFull);

        var root = Path.GetFullPath(workspaceRootFull.Trim());
        var inputFull = Path.GetFullPath(inputFilePathFull);
        var rel = Path.GetRelativePath(root, inputFull);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            throw new InvalidOperationException($"Input file is not under workspace root. Root={root}, Input={inputFull}");

        var dir = Path.GetDirectoryName(inputFull);
        if (string.IsNullOrEmpty(dir))
            throw new InvalidOperationException("Could not resolve directory for input file.");
        return dir;
    }
}
