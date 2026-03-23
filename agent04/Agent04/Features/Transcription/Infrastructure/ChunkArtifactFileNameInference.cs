using System.Text.RegularExpressions;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Derives main-chunk index from artifact filenames (<c>_part_NNN</c>, <c>_chunk_NNN_result.json</c>).
/// </summary>
public static class ChunkArtifactFileNameInference
{
    private static readonly Regex PartBound = new(@"\bpart_(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PartUnderscore = new(@"_part_(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>
    /// Per-chunk pipeline JSON (e.g. intermediate_results). <c>(?&lt;!sub)</c> avoids matching inside <c>sub_chunk_XX_result.json</c>.
    /// </summary>
    private static readonly Regex ChunkResultJson = new(@"(?<!sub)_chunk_(\d+)_result\.json$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int? InferChunkIndexFromName(string fileName)
    {
        var m = PartBound.Match(fileName);
        if (!m.Success)
            m = PartUnderscore.Match(fileName);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var part))
            return part;

        if (fileName.StartsWith("sub_chunk_", StringComparison.OrdinalIgnoreCase))
            return null;

        m = ChunkResultJson.Match(fileName);
        return m.Success && int.TryParse(m.Groups[1].Value, out var chunk) ? chunk : null;
    }
}
