using System.Text;
using System.Text.RegularExpressions;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>Scans artifact root into <see cref="ProjectFilesCatalogResult"/> (rules aligned with Xtract <c>JobProjectFilesScanner</c>).</summary>
internal static class ProjectFilesCatalogScanner
{
    private static readonly string[] TextExtensions = { ".md", ".txt", ".json", ".srt", ".vtt", ".csv", ".xml", ".log", ".text" };
    private static readonly string[] AudioExtensions = { ".m4a", ".mp3", ".wav", ".ogg", ".flac" };

    private static readonly Regex FirstDigits = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex SubChunkIndex = new(@"_sub_(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SubChunkResult = new(@"sub_chunk_(\d+)_result\.json$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ChunkMerged = new(@"^chunk_(\d+)_merged\.(json|md)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ProjectFilesCatalogResult Scan(string jobDir)
    {
        jobDir = Path.GetFullPath(jobDir);

        if (!Directory.Exists(jobDir))
        {
            return new ProjectFilesCatalogResult();
        }

        var original = new List<ArtifactFileEntry>();
        var transcripts = new List<ArtifactFileEntry>();

        foreach (var fi in EnumerateFilesSorted(jobDir))
        {
            var name = fi.Name;
            if (IsAudioFileName(name))
                original.Add(ToEntry(fi, fi.Name));
            else if (name.Contains("transcript", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                transcripts.Add(ToEntry(fi, fi.Name));
        }

        var chunksDir = Path.Combine(jobDir, "chunks");
        var chunksJsonDir = Path.Combine(jobDir, "chunks_json");
        var chunksMdDir = Path.Combine(jobDir, "chunks_md");
        var intermediateDir = Path.Combine(jobDir, "intermediate_results");
        var convertedDir = Path.Combine(jobDir, "converted_wav");

        var chunkJsonCombined = new List<ArtifactFileEntry>();
        chunkJsonCombined.AddRange(ScanChunkFolder(chunksJsonDir, "chunks_json", withIndex: true));
        chunkJsonCombined.AddRange(ScanChunkFolder(chunksMdDir, "chunks_md", withIndex: true));

        return new ProjectFilesCatalogResult
        {
            Original = original,
            Transcripts = transcripts,
            Chunks = ScanChunkFolder(chunksDir, "chunks", withIndex: true),
            ChunkJson = chunkJsonCombined,
            Intermediate = ScanSimpleFolder(intermediateDir, "intermediate_results"),
            Converted = ScanSimpleFolder(convertedDir, "converted_wav"),
            SplitChunks = ScanSplitChunks(jobDir),
        };
    }

    private static IReadOnlyList<ArtifactFileEntry> ScanSimpleFolder(string folder, string relativeFolder)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<ArtifactFileEntry>();

        var list = new List<ArtifactFileEntry>();
        foreach (var fi in EnumerateFilesSorted(folder))
            list.Add(ToEntry(fi, $"{relativeFolder}/{fi.Name}"));
        return list;
    }

    private static IReadOnlyList<ArtifactFileEntry> ScanChunkFolder(string folder, string relativeFolder, bool withIndex)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<ArtifactFileEntry>();

        var list = new List<ArtifactFileEntry>();
        foreach (var fi in EnumerateFilesSorted(folder))
        {
            var e = ToEntry(fi, $"{relativeFolder}/{fi.Name}");
            if (withIndex)
            {
                var inferred = ChunkArtifactFileNameInference.InferChunkIndexFromName(fi.Name);
                e = e with { Index = inferred ?? ParseFirstInt(fi.Name) };
            }

            list.Add(e);
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
                        f = f with
                        {
                            ParentIndex = parentIdx,
                            SubIndex = subIdx,
                            HasTranscript = true,
                            IsTranscript = true,
                        };
                        list.Add(f);
                    }
                }

                if (Directory.Exists(subChunksDir))
                {
                    foreach (var fi in EnumerateFilesSorted(subChunksDir))
                    {
                        var subMatch = SubChunkIndex.Match(fi.Name);
                        int? subIdx = subMatch.Success && int.TryParse(subMatch.Groups[1].Value, out var si) ? si : null;
                        var f = ToEntry(fi, RelPathUnderJob(jobDir, fi.FullName));
                        f = f with
                        {
                            ParentIndex = parentIdx,
                            SubIndex = subIdx,
                            HasTranscript = subIdx.HasValue && transcribedSub.Contains(subIdx.Value),
                            IsTranscript = false,
                        };
                        list.Add(f);
                    }
                }

                foreach (var fi in EnumerateFilesSorted(dir.FullName))
                {
                    var mm = ChunkMerged.Match(fi.Name);
                    if (!mm.Success || !int.TryParse(mm.Groups[1].Value, out var mergedFor) || mergedFor != parentIdx)
                        continue;
                    var mf = ToEntry(fi, RelPathUnderJob(jobDir, fi.FullName));
                    mf = mf with
                    {
                        ParentIndex = parentIdx,
                        SubIndex = null,
                        HasTranscript = true,
                        IsTranscript = true,
                    };
                    list.Add(mf);
                }
            }
        }
        catch
        {
            // ignore — same as agent-browser
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
        int? lineCount = null;
        double? durationSeconds = null;

        if (TextExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            kind = "text";
            lineCount = CountLines(fi.FullName);
        }
        else if (AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            kind = "audio";
            durationSeconds = GetAudioDurationSeconds(fi.FullName);
        }
        else
            kind = "other";

        return new ArtifactFileEntry
        {
            Name = fi.Name,
            RelativePath = rel,
            SizeBytes = fi.Length,
            Kind = kind,
            LineCount = lineCount,
            DurationSeconds = durationSeconds,
        };
    }

    private static string RelPathUnderJob(string jobDir, string fullPath)
    {
        var rel = Path.GetRelativePath(jobDir, fullPath);
        return NormalizeRelPath(rel);
    }

    private static string NormalizeRelPath(string rel) => rel.Replace('\\', '/');

    private static bool IsAudioFileName(string name)
    {
        var ext = Path.GetExtension(name);
        return AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

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

    private static double? GetAudioDurationSeconds(string filePath)
    {
        try
        {
            using var tfile = global::TagLib.File.Create(filePath);
            return tfile.Properties.Duration.TotalSeconds;
        }
        catch
        {
            return null;
        }
    }
}
