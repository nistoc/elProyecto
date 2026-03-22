using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Agent04 gRPC may emit Pending for chunk indices with no RENTGEN node. Replacing the stored VM with that
/// response (e.g. after Retranscribe) would wipe real per-chunk states (Cancelled, Completed, …). Merge
/// keeps prior terminal rows when the incoming row is only a Pending placeholder without timestamps.
/// Also preserves <see cref="ChunkVirtualModelEntry.TranscriptActivityLog"/> and rows missing from the incoming list.
/// </summary>
public static class ChunkVirtualModelMerge
{
    public static IReadOnlyList<ChunkVirtualModelEntry> Merge(
        IReadOnlyList<ChunkVirtualModelEntry>? previous,
        IReadOnlyList<ChunkVirtualModelEntry>? incoming)
    {
        if (incoming is not { Count: > 0 })
            return previous ?? Array.Empty<ChunkVirtualModelEntry>();
        if (previous is not { Count: > 0 })
            return incoming;

        var prevByKey = new Dictionary<string, ChunkVirtualModelEntry>(StringComparer.Ordinal);
        foreach (var e in previous)
            prevByKey[Key(e)] = e;

        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<ChunkVirtualModelEntry>(incoming.Count + 4);
        foreach (var live in incoming)
        {
            var k = Key(live);
            prevByKey.TryGetValue(k, out var prev);
            merged.Add(MergePair(prev, live));
            consumed.Add(k);
        }

        foreach (var e in previous)
        {
            var k = Key(e);
            if (!consumed.Contains(k))
                merged.Add(e);
        }

        return merged;
    }

    private static string Key(ChunkVirtualModelEntry e) =>
        e.IsSubChunk ? $"s:{e.ParentChunkIndex}:{e.SubChunkIndex}" : $"m:{e.Index}";

    private static ChunkVirtualModelEntry MergePair(ChunkVirtualModelEntry? prev, ChunkVirtualModelEntry live)
    {
        if (prev != null && IsWeakPlaceholder(live) && ShouldKeepPrevOverPlaceholder(prev))
            return prev;

        var chosen = live;
        if (prev == null)
            return chosen;

        var log = CombineActivityLog(prev.TranscriptActivityLog, chosen.TranscriptActivityLog);
        if (log == chosen.TranscriptActivityLog)
            return chosen;

        return CloneWithLog(chosen, log);
    }

    private static ChunkVirtualModelEntry CloneWithLog(ChunkVirtualModelEntry src, string? log) =>
        new()
        {
            Index = src.Index,
            StartedAt = src.StartedAt,
            CompletedAt = src.CompletedAt,
            State = src.State,
            ErrorMessage = src.ErrorMessage,
            IsSubChunk = src.IsSubChunk,
            ParentChunkIndex = src.ParentChunkIndex,
            SubChunkIndex = src.SubChunkIndex,
            TranscriptActivityLog = log
        };

    private static bool IsWeakPlaceholder(ChunkVirtualModelEntry e)
    {
        var s = (e.State ?? "").Trim();
        if (s.Length != 0 && !s.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(e.StartedAt) || !string.IsNullOrWhiteSpace(e.CompletedAt))
            return false;
        return true;
    }

    private static bool ShouldKeepPrevOverPlaceholder(ChunkVirtualModelEntry prev)
    {
        if (IsTerminal(prev))
            return true;
        if (IsRunning(prev))
            return true;
        if (!string.IsNullOrWhiteSpace(prev.StartedAt) || !string.IsNullOrWhiteSpace(prev.CompletedAt))
            return true;
        if (!string.IsNullOrWhiteSpace(prev.TranscriptActivityLog))
            return true;
        return false;
    }

    private static bool IsRunning(ChunkVirtualModelEntry e) =>
        (e.State ?? "").Trim().Equals("Running", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminal(ChunkVirtualModelEntry e)
    {
        var s = (e.State ?? "").Trim();
        return s.Equals("Completed", StringComparison.OrdinalIgnoreCase)
               || s.Equals("Failed", StringComparison.OrdinalIgnoreCase)
               || s.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static string? CombineActivityLog(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a))
            return string.IsNullOrWhiteSpace(b) ? null : b.Trim();
        if (string.IsNullOrWhiteSpace(b))
            return a.Trim();
        a = a.Trim();
        b = b.Trim();
        if (b.Contains(a, StringComparison.Ordinal))
            return b;
        if (a.Contains(b, StringComparison.Ordinal))
            return a;
        return a + "\n" + b;
    }
}
