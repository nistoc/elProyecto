using System.Text.RegularExpressions;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>Port of agent05 <c>chunkArtifactGroups.ts</c> (file partitioning only; no VM rows).</summary>
public static class ChunkArtifactGrouping
{
    private static readonly Regex PartBound = new(@"\bpart_(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PartUnderscore = new(@"_part_(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChunkMerged = new(@"^chunk_(\d+)_merged\.(json|md)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int? InferChunkIndexFromName(string fileName)
    {
        var m = PartBound.Match(fileName);
        if (!m.Success)
            m = PartUnderscore.Match(fileName);
        if (!m.Success) return null;
        return int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    public static bool FileBelongsToChunkIndex(ArtifactFileEntry f, int index, int total)
    {
        _ = total;
        var inferred = InferChunkIndexFromName(f.Name);
        if (inferred.HasValue) return inferred.Value == index;
        if (f.Index.HasValue) return f.Index.Value == index;
        return false;
    }

    public static int[] ComputeChunkIndices(int totalChunks, JobArtifactDirectoryScanner.ScanResult files)
    {
        if (totalChunks > 0)
        {
            var arr = new int[totalChunks];
            for (var i = 0; i < totalChunks; i++) arr[i] = i;
            return arr;
        }

        var set = new SortedSet<int>();
        foreach (var f in files.Chunks) AddIndexFromFile(f, set);
        foreach (var f in files.ChunkJson) AddIndexFromFile(f, set);
        foreach (var f in files.SplitChunks)
        {
            if (f.ParentIndex.HasValue)
                set.Add(f.ParentIndex.Value);
        }

        return set.ToArray();
    }

    private static void AddIndexFromFile(ArtifactFileEntry f, SortedSet<int> set)
    {
        var inferred = InferChunkIndexFromName(f.Name);
        if (inferred.HasValue) set.Add(inferred.Value);
        else if (f.Index.HasValue) set.Add(f.Index.Value);
    }

    public static bool IsSplitParentMergedArtifact(ArtifactFileEntry f, int parentIndex)
    {
        if (f.ParentIndex != parentIndex) return false;
        var m = ChunkMerged.Match(f.Name);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n == parentIndex;
    }

    public static IReadOnlyList<SubChunkArtifactGroupResult> BuildSubChunkGroups(
        IReadOnlyList<ArtifactFileEntry> splitChunks,
        int parentIndex)
    {
        var forParent = splitChunks.Where(f => f.ParentIndex == parentIndex).ToList();
        if (forParent.Count == 0) return Array.Empty<SubChunkArtifactGroupResult>();

        var byKey = new Dictionary<string, (int? SubIndex, List<ArtifactFileEntry> Bucket)>(StringComparer.Ordinal);
        foreach (var f in forParent)
        {
            var subIndex = f.SubIndex;
            var key = subIndex.HasValue ? $"i:{subIndex.Value}" : "i:null";
            if (!byKey.TryGetValue(key, out var row))
            {
                row = (subIndex, new List<ArtifactFileEntry>());
                byKey[key] = row;
            }

            row.Bucket.Add(f);
        }

        var outList = new List<SubChunkArtifactGroupResult>();
        foreach (var row in byKey.Values)
        {
            var (subIndex, bucket) = row;
            var audioFiles = bucket.Where(x => x.Kind == "audio").ToList();
            var jsonFiles = bucket.Where(x => x.Kind == "text").ToList();
            if (audioFiles.Count == 0 && jsonFiles.Count == 0) continue;
            outList.Add(new SubChunkArtifactGroupResult
            {
                SubIndex = subIndex,
                AudioFiles = audioFiles,
                JsonFiles = jsonFiles,
                DisplayStem = ComputeDisplayStem(audioFiles, jsonFiles),
            });
        }

        outList.Sort((a, b) => SubChunkGroupSortKey(a.SubIndex).CompareTo(SubChunkGroupSortKey(b.SubIndex)));
        return outList;
    }

    private static int SubChunkGroupSortKey(int? subIndex) => subIndex ?? int.MaxValue;

    private static string ComputeDisplayStem(IReadOnlyList<ArtifactFileEntry> audioFiles, IReadOnlyList<ArtifactFileEntry> jsonFiles)
    {
        var first = audioFiles.Count > 0 ? audioFiles[0] : jsonFiles.Count > 0 ? jsonFiles[0] : null;
        if (first == null) return "";
        return StripExtension(first.Name);
    }

    private static string StripExtension(string name)
    {
        var i = name.LastIndexOf('.');
        return i > 0 ? name[..i] : name;
    }

    public static IReadOnlyList<ChunkArtifactGroupResult> BuildChunkGroups(
        JobArtifactDirectoryScanner.ScanResult files,
        int[] indices,
        int totalChunksForBelongs)
    {
        var list = new List<ChunkArtifactGroupResult>(indices.Length);
        foreach (var index in indices)
        {
            var audioFiles = files.Chunks.Where(f => FileBelongsToChunkIndex(f, index, totalChunksForBelongs)).ToList();
            var jsonFiles = files.ChunkJson.Where(f => FileBelongsToChunkIndex(f, index, totalChunksForBelongs)).ToList();
            var splitForParent = files.SplitChunks.Where(f => f.ParentIndex == index).ToList();
            var mergedSplitFiles = splitForParent.Where(f => IsSplitParentMergedArtifact(f, index)).ToList();
            var splitForSubRows = splitForParent.Where(f => !IsSplitParentMergedArtifact(f, index)).ToList();
            var subChunks = BuildSubChunkGroups(splitForSubRows, index);
            list.Add(new ChunkArtifactGroupResult
            {
                Index = index,
                AudioFiles = audioFiles,
                JsonFiles = jsonFiles,
                SubChunks = subChunks,
                MergedSplitFiles = mergedSplitFiles,
                DisplayStem = ComputeDisplayStem(audioFiles, jsonFiles),
            });
        }

        return list;
    }
}
