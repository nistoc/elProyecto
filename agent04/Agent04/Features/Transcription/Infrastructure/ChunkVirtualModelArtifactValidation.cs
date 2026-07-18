using Agent04.Proto;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Reconciles VM rows with what actually exists on disk, so the UI never shows a state
/// without a backing artifact. Applied only when the job is NOT running in this process
/// (post-mortem view): a live pipeline is the source of truth for its own rows, and the
/// write path already persists the per-chunk JSON before marking a chunk Completed.
/// - Completed main-chunk row without its per-chunk JSON → Failed (result lost, e.g. crash);
///   skipped when no chunk has JSON at all (job may run with save_per_chunk_json off).
/// - Running row → Failed (zombie row of a dead process; nothing is transcribing it).
/// </summary>
public static class ChunkVirtualModelArtifactValidation
{
    public static IReadOnlyList<ChunkVirtualModelEntry> Apply(
        IReadOnlyList<ChunkVirtualModelEntry> vm,
        IEnumerable<ChunkArtifactGroup> groups,
        bool jobIsRunningInProcess)
    {
        if (jobIsRunningInProcess || vm.Count == 0)
            return vm;

        var jsonByIndex = new Dictionary<int, bool>();
        foreach (var g in groups)
            jsonByIndex[g.Index] = g.JsonFiles.Count > 0;
        var anyJsonOnDisk = jsonByIndex.Values.Any(v => v);

        var result = new List<ChunkVirtualModelEntry>(vm.Count);
        foreach (var row in vm)
        {
            if (row.IsSubChunk)
            {
                result.Add(row);
                continue;
            }

            var hasJson = jsonByIndex.TryGetValue(row.ChunkIndex, out var h) && h;
            if (anyJsonOnDisk
                && string.Equals(row.State, "Completed", StringComparison.OrdinalIgnoreCase)
                && !hasJson)
            {
                var updated = row.Clone();
                updated.State = "Failed";
                updated.ErrorMessage = "Completed state but transcript JSON is missing on disk (lost in a crash?) — retranscribe this chunk.";
                result.Add(updated);
                continue;
            }

            if (string.Equals(row.State, "Running", StringComparison.OrdinalIgnoreCase))
            {
                var updated = row.Clone();
                updated.State = "Failed";
                updated.ErrorMessage = "Interrupted: the transcription process is no longer running — retranscribe this chunk.";
                result.Add(updated);
                continue;
            }

            result.Add(row);
        }

        return result;
    }
}
