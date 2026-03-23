using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>Removes entries from the flat project file catalog when they already appear in chunk artifact groups.</summary>
internal static class ProjectFilesCatalogExclusion
{
    public static ProjectFilesCatalogResult ExcludeGrouped(ProjectFilesCatalogResult catalog, HashSet<string> groupedRelativePaths)
    {
        if (groupedRelativePaths.Count == 0)
            return catalog;

        return catalog with
        {
            Chunks = Filter(catalog.Chunks, groupedRelativePaths),
            ChunkJson = Filter(catalog.ChunkJson, groupedRelativePaths),
            Intermediate = Filter(catalog.Intermediate, groupedRelativePaths),
            SplitChunks = Filter(catalog.SplitChunks, groupedRelativePaths),
        };
    }

    private static IReadOnlyList<ArtifactFileEntry> Filter(
        IReadOnlyList<ArtifactFileEntry> files,
        HashSet<string> groupedRelativePaths)
    {
        var list = new List<ArtifactFileEntry>(files.Count);
        foreach (var f in files)
        {
            var key = string.IsNullOrEmpty(f.RelativePath) ? f.Name : f.RelativePath;
            if (string.IsNullOrEmpty(key) || !groupedRelativePaths.Contains(key))
                list.Add(f);
        }

        return list;
    }
}
