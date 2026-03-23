using System.Text.RegularExpressions;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Resolve paths under <c>split_chunks/chunk_N/</c> for operator split + sub transcribe.
/// </summary>
public static class SplitChunkPaths
{
    private static readonly Regex SubIndexInName = new(@"_sub_0*(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>First audio file in <paramref name="subChunksDir"/> whose name contains <c>_sub_{subIndex}</c>.</summary>
    public static string? FindSubChunkAudioFile(string subChunksDir, int subIndex)
    {
        if (!Directory.Exists(subChunksDir)) return null;
        foreach (var fi in new DirectoryInfo(subChunksDir).EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var ext = fi.Extension;
            if (!ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".flac", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
                continue;
            var m = SubIndexInName.Match(fi.Name);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups[1].Value, out var idx) || idx != subIndex) continue;
            return fi.FullName;
        }

        return null;
    }
}
