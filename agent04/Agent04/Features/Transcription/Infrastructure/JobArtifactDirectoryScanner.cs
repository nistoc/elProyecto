using System.Text;
using System.Text.RegularExpressions;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Port of agent05 <c>JobProjectFilesScanner</c> for chunk grouping: same paths, index rules, split layout.
/// Audio duration is not populated (no TagLib in Agent04); text line counts are counted best-effort.
/// </summary>
public static class JobArtifactDirectoryScanner
{
    private static readonly string[] TextExtensions = { ".md", ".txt", ".json", ".srt", ".vtt", ".csv", ".xml", ".log", ".text" };
    private static readonly string[] AudioExtensions = { ".m4a", ".mp3", ".wav", ".ogg", ".flac" };

    private static readonly Regex FirstDigits = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex SubChunkIndex = new(@"_sub_(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SubChunkResult = new(@"sub_chunk_(\d+)_result\.json$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ChunkMerged = new(@"^chunk_(\d+)_merged\.(json|md)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public sealed class ScanResult
    {
        public IReadOnlyList<ArtifactFileEntry> Chunks { get; init; } = Array.Empty<ArtifactFileEntry>();
        public IReadOnlyList<ArtifactFileEntry> ChunkJson { get; init; } = Array.Empty<ArtifactFileEntry>();
        public IReadOnlyList<ArtifactFileEntry> Intermediate { get; init; } = Array.Empty<ArtifactFileEntry>();
        public IReadOnlyList<ArtifactFileEntry> SplitChunks { get; init; } = Array.Empty<ArtifactFileEntry>();
    }

    public static ScanResult ScanForChunkGrouping(string artifactRoot)
    {
        var jobDir = Path.GetFullPath(artifactRoot);
        if (!Directory.Exists(jobDir))
            return new ScanResult();

        var chunksDir = Path.Combine(jobDir, "chunks");
        var chunksJsonDir = Path.Combine(jobDir, "chunks_json");
        var intermediateDir = Path.Combine(jobDir, "intermediate_results");

        return new ScanResult
        {
            Chunks = ScanChunkFolder(chunksDir, "chunks"),
            ChunkJson = ScanChunkFolder(chunksJsonDir, "chunks_json"),
            Intermediate = ScanChunkFolder(intermediateDir, "intermediate_results"),
            SplitChunks = ScanSplitChunks(jobDir),
        };
    }

    private static IReadOnlyList<ArtifactFileEntry> ScanChunkFolder(string folder, string relativeFolder)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<ArtifactFileEntry>();

        var list = new List<ArtifactFileEntry>();
        foreach (var fi in EnumerateFilesSorted(folder))
        {
            var file = ToEntry(fi, $"{relativeFolder}/{fi.Name}");
            var inferred = ChunkArtifactFileNameInference.InferChunkIndexFromName(fi.Name);
            var withIndex = file with { Index = inferred ?? ParseFirstInt(fi.Name) };
            list.Add(withIndex);
        }

        return list;
    }

    private static IReadOnlyList<ArtifactFileEntry> ScanSplitChunks(string jobDir)
    {
        var splitRoot = Path.Combine(jobDir, "split_chunks");
        if (!Directory.Exists(splitRoot))
            return Array.Empty<ArtifactFileEntry>();

        var list = new List<ArtifactFileEntry>();
        try
        {
            foreach (var dir in new DirectoryInfo(splitRoot).EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!dir.Name.StartsWith("chunk_", StringComparison.OrdinalIgnoreCase))
                    continue;
                var parentPart = dir.Name["chunk_".Length..];
                if (!int.TryParse(parentPart, out var parentIdx))
                    continue;

                var subChunksDir = Path.Combine(dir.FullName, "sub_chunks");
                var resultsDir = Path.Combine(dir.FullName, "results");

                var transcribedSub = new HashSet<int>();
                if (Directory.Exists(resultsDir))
                {
                    foreach (var fi in EnumerateFilesSorted(resultsDir))
                    {
                        var m = SubChunkResult.Match(fi.Name);
                        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var subIdx))
                            continue;
                        transcribedSub.Add(subIdx);
                        var f = ToEntry(fi, RelPathUnderJob(jobDir, fi.FullName));
                        list.Add(f with
                        {
                            ParentIndex = parentIdx,
                            SubIndex = subIdx,
                            HasTranscript = true,
                            IsTranscript = true,
                        });
                    }
                }

                if (Directory.Exists(subChunksDir))
                {
                    foreach (var fi in EnumerateFilesSorted(subChunksDir))
                    {
                        var subMatch = SubChunkIndex.Match(fi.Name);
                        int? subIdx = subMatch.Success && int.TryParse(subMatch.Groups[1].Value, out var si) ? si : null;
                        var f = ToEntry(fi, RelPathUnderJob(jobDir, fi.FullName));
                        list.Add(f with
                        {
                            ParentIndex = parentIdx,
                            SubIndex = subIdx,
                            HasTranscript = subIdx.HasValue && transcribedSub.Contains(subIdx.Value),
                            IsTranscript = false,
                        });
                    }
                }

                foreach (var fi in EnumerateFilesSorted(dir.FullName))
                {
                    var mm = ChunkMerged.Match(fi.Name);
                    if (!mm.Success || !int.TryParse(mm.Groups[1].Value, out var mergedFor) || mergedFor != parentIdx)
                        continue;
                    var mf = ToEntry(fi, RelPathUnderJob(jobDir, fi.FullName));
                    list.Add(mf with
                    {
                        ParentIndex = parentIdx,
                        SubIndex = null,
                        HasTranscript = true,
                        IsTranscript = true,
                    });
                }
            }
        }
        catch
        {
            /* same as agent05: ignore */
        }

        return list;
    }

    private static IEnumerable<FileInfo> EnumerateFilesSorted(string directory) =>
        new DirectoryInfo(directory).EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

    private static ArtifactFileEntry ToEntry(FileInfo fi, string relativePathFromJob)
    {
        var rel = NormalizeRelPath(relativePathFromJob);
        var ext = fi.Extension;
        string kind;
        int? lines = null;
        if (TextExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            kind = "text";
            lines = CountLines(fi.FullName);
        }
        else if (AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            kind = "audio";
        else
            kind = "other";

        return new ArtifactFileEntry
        {
            Name = fi.Name,
            RelativePath = rel,
            SizeBytes = fi.Length,
            Kind = kind,
            LineCount = lines,
            DurationSeconds = null,
        };
    }

    private static string RelPathUnderJob(string jobDir, string fullPath)
    {
        var rel = Path.GetRelativePath(jobDir, fullPath);
        return NormalizeRelPath(rel);
    }

    private static string NormalizeRelPath(string rel) => rel.Replace('\\', '/');

    private static int? ParseFirstInt(string fileName)
    {
        var m = FirstDigits.Match(fileName);
        return m.Success && int.TryParse(m.Value, out var n) ? n : null;
    }

    private static int? CountLines(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            var count = 0;
            while (reader.ReadLine() != null) count++;
            return count;
        }
        catch
        {
            return null;
        }
    }
}
