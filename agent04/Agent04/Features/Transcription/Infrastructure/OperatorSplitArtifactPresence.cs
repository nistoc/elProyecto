using System.Text.RegularExpressions;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>Detects operator-split layout under <c>{split_chunks_dir}/chunk_N/</c> (sub_chunks or results).</summary>
public static class OperatorSplitArtifactPresence
{
    private static readonly Regex SubChunkResult = new(
        @"^sub_chunk_\d+_result\.json$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool HasArtifactsForChunk(string artifactRoot, string splitChunksDir, int chunkIndex)
    {
        if (string.IsNullOrWhiteSpace(artifactRoot) || chunkIndex < 0)
            return false;
        var root = Path.GetFullPath(artifactRoot);
        if (!Directory.Exists(root))
            return false;
        var dir = (splitChunksDir ?? "split_chunks").Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
