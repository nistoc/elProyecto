using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>Deletes one operator-split sub-chunk (audio, result json, cancel flag, work-state row).</summary>
public static class OperatorSubChunkArtifacts
{
    private static readonly Regex SubIndexInName = new(
        @"_sub_0*(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SafeId = new(@"[^a-zA-Z0-9_\-\.]", RegexOptions.Compiled);

    public static bool IsSubChunkRunning(JobSnapshot? snap, int parentChunkIndex, int subChunkIndex)
    {
        var vm = snap?.Chunks?.ChunkVirtualModel;
        if (vm is not { Count: > 0 })
            return false;
        foreach (var e in vm)
        {
            if (e.IsSubChunk != true) continue;
            if (e.ParentChunkIndex != parentChunkIndex || e.SubChunkIndex != subChunkIndex)
                continue;
            var s = (e.State ?? "").Trim();
            return s.Equals("Running", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <returns>(ok, error message for client)</returns>
    public static async Task<(bool Ok, string Message)> TryDeleteBundleAsync(
        string jobDirectoryPath,
        string? agent04JobId,
        int parentChunkIndex,
        int subChunkIndex,
        string splitChunksDir,
        JobSnapshot? snap,
        ILogger? logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jobDirectoryPath) || parentChunkIndex < 0 || subChunkIndex < 0)
            return (false, "invalid parameters");
        var root = Path.GetFullPath(jobDirectoryPath);
        if (!Directory.Exists(root))
            return (false, "job directory not found");

        if (IsSubChunkRunning(snap, parentChunkIndex, subChunkIndex))
            return (false, "sub-chunk is running; cancel it first");

        var dir = string.IsNullOrWhiteSpace(splitChunksDir) ? "split_chunks" : splitChunksDir.Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var chunkDir = Path.Combine(root, dir, $"chunk_{parentChunkIndex}");
        var subChunksDir = Path.Combine(chunkDir, "sub_chunks");
        var resultsDir = Path.Combine(chunkDir, "results");

        if (Directory.Exists(subChunksDir))
        {
            foreach (var fi in new DirectoryInfo(subChunksDir).EnumerateFiles())
            {
                var m = SubIndexInName.Match(fi.Name);
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out var idx) || idx != subChunkIndex)
                    continue;
                try
                {
                    fi.Delete();
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Delete sub-chunk audio {Path}", fi.FullName);
                    return (false, "failed to delete sub-chunk audio file");
                }
            }
        }

        var resultJson = Path.Combine(resultsDir, $"sub_chunk_{subChunkIndex:D2}_result.json");
        try
        {
            if (File.Exists(resultJson))
                File.Delete(resultJson);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Delete sub-chunk result {Path}", resultJson);
            return (false, "failed to delete sub-chunk result json");
        }

        var safe = string.IsNullOrEmpty(agent04JobId)
            ? "_unknown"
            : SafeId.Replace(agent04JobId, "_");
        var cancelFlag = Path.Combine(
            root,
            ".agent04_chunk_cancel",
            safe,
            $"cancel_sub_{parentChunkIndex}_{subChunkIndex}.flag");
        try
        {
            if (File.Exists(cancelFlag))
                File.Delete(cancelFlag);
        }
        catch
        {
            /* best-effort */
        }

        var wsOk = await TranscriptionWorkStateMutation.TryRemoveSubChunkRowAsync(
            root,
            parentChunkIndex,
            subChunkIndex,
            logger,
            ct).ConfigureAwait(false);
        if (!wsOk)
            return (false, "could not update transcription_work_state.json");

        return (true, "sub_chunk_deleted");
    }
}
