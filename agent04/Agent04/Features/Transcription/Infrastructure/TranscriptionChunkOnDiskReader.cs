using System.Text.RegularExpressions;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Discover main pipeline chunk audio files under <c>chunks/</c> (<c>_part_NNN</c> in name) and resolve cache manifest stem/path.
/// </summary>
public static class TranscriptionChunkOnDiskReader
{
    public static readonly Regex PartIndexInFileName = new(
        @"(?:^|_)part_(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Map 0-based chunk index to absolute path for each audio file under <paramref name="chunksDirectory"/>
    /// whose name contains <c>_part_NNN</c>.
    /// </summary>
    public static Dictionary<int, string> MapPartIndexToAudioPath(string chunksDirectory)
    {
        var map = new Dictionary<int, string>();
        if (!Directory.Exists(chunksDirectory))
            return map;
        foreach (var fi in new DirectoryInfo(chunksDirectory).EnumerateFiles()
                     .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsChunkAudioExtension(fi.Extension))
                continue;
            var m = PartIndexInFileName.Match(fi.Name);
            if (!m.Success)
                continue;
            if (!int.TryParse(m.Groups[1].Value, out var idx))
                continue;
            map[idx] = fi.FullName;
        }

        return map;
    }

    private static bool IsChunkAudioExtension(string ext) =>
        ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".flac", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Strip trailing <c>_part_NNN</c> from chunk file name stem for manifest base (e.g. foo_part_003.wav → foo).
    /// </summary>
    public static string ManifestStemFromChunkFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var m = Regex.Match(stem, @"_part_\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success ? stem.Substring(0, m.Index) : stem;
    }

    /// <summary>
    /// Prefer <c>{manifestStem}.manifest.json</c>; if missing and exactly one manifest exists in cache, use it; if several, pick first sorted path.
    /// </summary>
    public static string? TryResolveManifestJsonPath(string artifactRoot, string cacheDirRel, string manifestStem)
    {
        var rel = cacheDirRel.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var cacheDir = Path.Combine(artifactRoot, rel);
        if (!Directory.Exists(cacheDir))
            return null;
        var preferred = Path.Combine(cacheDir, manifestStem + ".manifest.json");
        if (File.Exists(preferred))
            return preferred;
        try
        {
            var all = Directory.GetFiles(cacheDir, "*.manifest.json", SearchOption.TopDirectoryOnly);
            return all.Length switch
            {
                0 => null,
                1 => all[0],
                _ => all.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).First()
            };
        }
        catch
        {
            return null;
        }
    }
}
