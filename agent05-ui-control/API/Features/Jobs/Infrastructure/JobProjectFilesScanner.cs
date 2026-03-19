using System.Text;
using System.Text.RegularExpressions;
using TagLib;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Structured scan of a job directory (same categories as agent-browser GET /api/jobs/:id/files).
/// </summary>
public static class JobProjectFilesScanner
{
    private static readonly string[] TextExtensions = { ".md", ".txt", ".json", ".srt", ".vtt", ".csv", ".xml", ".log", ".text" };
    private static readonly string[] AudioExtensions = { ".m4a", ".mp3", ".wav", ".ogg", ".flac" };

    private static readonly Regex FirstDigits = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex SubChunkIndex = new(@"_sub_(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SubChunkResult = new(@"sub_chunk_(\d+)_result\.json$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static JobProjectFiles Scan(string jobDir)
    {
        jobDir = Path.GetFullPath(jobDir);

        if (!Directory.Exists(jobDir))
            return new JobProjectFiles();

        var original = new List<JobProjectFile>();
        var transcripts = new List<JobProjectFile>();

        // Root: original audio vs transcripts (same rules as agent-browser)
        foreach (var fi in EnumerateFilesSorted(jobDir))
        {
            var name = fi.Name;
            if (IsAudioFileName(name))
            {
                original.Add(ToProjectFile(fi, fi.Name));
            }
            else if (name.Contains("transcript", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                     name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                transcripts.Add(ToProjectFile(fi, fi.Name));
            }
        }

        var chunksDir = Path.Combine(jobDir, "chunks");
        var chunksJsonDir = Path.Combine(jobDir, "chunks_json");
        var intermediateDir = Path.Combine(jobDir, "intermediate_results");
        var convertedDir = Path.Combine(jobDir, "converted_wav");

        return new JobProjectFiles
        {
            Original = original,
            Transcripts = transcripts,
            Chunks = ScanChunkFolder(chunksDir, "chunks", withIndex: true),
            ChunkJson = ScanChunkFolder(chunksJsonDir, "chunks_json", withIndex: true),
            Intermediate = ScanSimpleFolder(intermediateDir, "intermediate_results"),
            Converted = ScanSimpleFolder(convertedDir, "converted_wav"),
            SplitChunks = ScanSplitChunks(jobDir),
        };
    }

    private static IReadOnlyList<JobProjectFile> ScanSimpleFolder(string folder, string relativeFolder)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<JobProjectFile>();

        var list = new List<JobProjectFile>();
        foreach (var fi in EnumerateFilesSorted(folder))
            list.Add(ToProjectFile(fi, $"{relativeFolder}/{fi.Name}"));
        return list;
    }

    private static IReadOnlyList<JobProjectFile> ScanChunkFolder(string folder, string relativeFolder, bool withIndex)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<JobProjectFile>();

        var list = new List<JobProjectFile>();
        foreach (var fi in EnumerateFilesSorted(folder))
        {
            var file = ToProjectFile(fi, $"{relativeFolder}/{fi.Name}");
            if (withIndex)
                file.Index = ParseFirstInt(fi.Name);
            list.Add(file);
        }
        return list;
    }

    private static IReadOnlyList<JobProjectFile> ScanSplitChunks(string jobDir)
    {
        var splitRoot = Path.Combine(jobDir, "split_chunks");
        if (!Directory.Exists(splitRoot))
            return Array.Empty<JobProjectFile>();

        var list = new List<JobProjectFile>();
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
                        var f = ToProjectFile(fi, RelPathUnderJob(jobDir, fi.FullName));
                        f.ParentIndex = parentIdx;
                        f.SubIndex = subIdx;
                        f.HasTranscript = true;
                        f.IsTranscript = true;
                        list.Add(f);
                    }
                }

                if (Directory.Exists(subChunksDir))
                {
                    foreach (var fi in EnumerateFilesSorted(subChunksDir))
                    {
                        var subMatch = SubChunkIndex.Match(fi.Name);
                        int? subIdx = subMatch.Success && int.TryParse(subMatch.Groups[1].Value, out var si) ? si : null;
                        var f = ToProjectFile(fi, RelPathUnderJob(jobDir, fi.FullName));
                        f.ParentIndex = parentIdx;
                        f.SubIndex = subIdx;
                        f.HasTranscript = subIdx.HasValue && transcribedSub.Contains(subIdx.Value);
                        f.IsTranscript = false;
                        list.Add(f);
                    }
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

    /// <param name="relativePathFromJob">Path relative to job directory, forward slashes (e.g. <c>chunks/x.wav</c>).</param>
    private static JobProjectFile ToProjectFile(FileInfo fi, string relativePathFromJob)
    {
        var rel = NormalizeRelPath(relativePathFromJob);

        var ext = fi.Extension;
        JobProjectFile file = new()
        {
            Name = fi.Name,
            RelativePath = rel,
            FullPath = fi.FullName,
            SizeBytes = fi.Length,
        };

        if (TextExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            file.Kind = "text";
            file.LineCount = CountLines(fi.FullName);
        }
        else if (AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            file.Kind = "audio";
            file.DurationSeconds = GetAudioDurationSeconds(fi.FullName);
        }
        else
        {
            file.Kind = "other";
        }

        return file;
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
            using var tfile = TagLib.File.Create(filePath);
            return tfile.Properties.Duration.TotalSeconds;
        }
        catch
        {
            return null;
        }
    }
}
