using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>Best-effort edit of <see cref="JobSnapshotDiskEnricher.TranscriptionWorkStateFileName"/> next to job artifacts.</summary>
public static class TranscriptionWorkStateMutation
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<bool> TryRemoveSubChunkRowAsync(
        string jobDirectoryPath,
        int parentChunkIndex,
        int subChunkIndex,
        ILogger? logger,
        CancellationToken ct)
    {
        var root = Path.GetFullPath(jobDirectoryPath);
        var path = Path.Combine(root, JobSnapshotDiskEnricher.TranscriptionWorkStateFileName);
        if (!File.Exists(path))
            return true;

        WorkStateDoc? doc;
        try
        {
            await using var fs = File.OpenRead(path);
            doc = await JsonSerializer.DeserializeAsync<WorkStateDoc>(fs, JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not read work state for sub-chunk removal: {Path}", path);
            return false;
        }

        if (doc?.Chunks is not { Count: > 0 })
            return true;

        var n = doc.Chunks.RemoveAll(c =>
            c.IsSubChunk
            && c.ParentChunkIndex == parentChunkIndex
            && c.SubChunkIndex == subChunkIndex);
        if (n == 0)
            return true;

        try
        {
            await WriteAtomicAsync(path, doc, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not write work state after sub-chunk removal: {Path}", path);
            return false;
        }
    }

    private static async Task WriteAtomicAsync(string path, WorkStateDoc doc, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
        if (!File.Exists(path))
            File.Move(tmp, path);
        else
            File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
    }

    private sealed class WorkStateDoc
    {
        public int SchemaVersion { get; set; }
        public int TotalChunks { get; set; }
        public bool RecoveredFromArtifacts { get; set; }
        public List<WorkStateChunkRow>? Chunks { get; set; }
    }

    private sealed class WorkStateChunkRow
    {
        public int Index { get; set; }
        public string State { get; set; } = "";
        public string? StartedAt { get; set; }
        public string? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsSubChunk { get; set; }
        public int ParentChunkIndex { get; set; }
        public int SubChunkIndex { get; set; }
    }
}
