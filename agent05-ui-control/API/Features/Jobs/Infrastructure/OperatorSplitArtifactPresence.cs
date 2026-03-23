using System.Text.RegularExpressions;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>Detects operator-split layout under <c>{split_chunks_dir}/chunk_N/</c> (sub_chunks or results).</summary>
public static class OperatorSplitArtifactPresence
{
    private static readonly Regex SubChunkResult = new(
        @"^sub_chunk_\d+_result\.json$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <param name="jobDirectoryPath">Xtract job directory (artifact root).</param>
    public static bool HasArtifactsForChunk(
        string jobDirectoryPath,
        int chunkIndex,
        string splitChunksDir = "split_chunks")
    {
        if (string.IsNullOrWhiteSpace(jobDirectoryPath) || chunkIndex < 0)
            return false;
        var root = Path.GetFullPath(jobDirectoryPath);
        if (!Directory.Exists(root))
            return false;
        var dir = splitChunksDir.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(dir))
            dir = "split_chunks";
        var chunkDir = Path.Combine(root, dir, $"chunk_{chunkIndex}");
        if (!Directory.Exists(chunkDir))
            return false;
        var subChunks = Path.Combine(chunkDir, "sub_chunks");
        if (Directory.Exists(subChunks))
        {
            foreach (var _ in Directory.EnumerateFiles(subChunks))
                return true;
        }

        var results = Path.Combine(chunkDir, "results");
        if (!Directory.Exists(results))
            return false;
        foreach (var path in Directory.EnumerateFiles(results))
        {
            if (SubChunkResult.IsMatch(Path.GetFileName(path)))
                return true;
        }

        return false;
    }
}
