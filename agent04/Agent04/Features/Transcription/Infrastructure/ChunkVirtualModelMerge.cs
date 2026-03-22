using Agent04.Proto;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Merges the orchestrator's last known VM with Rentgen-built rows. Prevents placeholder Pending rows from
/// wiping terminal states when a chunk node is temporarily missing from the query scope.
/// </summary>
public static class ChunkVirtualModelMerge
{
    public static IReadOnlyList<ChunkVirtualModelEntry> Merge(
        IReadOnlyList<ChunkVirtualModelEntry>? previous,
        IReadOnlyList<ChunkVirtualModelEntry>? incoming)
    {
        if (incoming is not { Count: > 0 })
            return previous?.Count > 0 ? CloneList(previous) : Array.Empty<ChunkVirtualModelEntry>();
        if (previous is not { Count: > 0 })
            return CloneList(incoming);

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
                merged.Add(e.Clone());
        }

        return merged;
    }

    private static List<ChunkVirtualModelEntry> CloneList(IReadOnlyList<ChunkVirtualModelEntry> src)
    {
        var list = new List<ChunkVirtualModelEntry>(src.Count);
        foreach (var e in src)
            list.Add(e.Clone());
        return list;
    }

    private static string Key(ChunkVirtualModelEntry e) =>
        e.IsSubChunk ? $"s:{e.ParentChunkIndex}:{e.SubChunkIndex}" : $"m:{e.ChunkIndex}";

    private static ChunkVirtualModelEntry MergePair(ChunkVirtualModelEntry? prev, ChunkVirtualModelEntry live)
    {
        if (prev != null && IsWeakPlaceholder(live) && ShouldKeepPrevOverPlaceholder(prev))
            return prev.Clone();

        var chosen = live.Clone();
        if (prev == null)
            return chosen;

        var log = CombineActivityLog(prev.TranscriptActivityLog, chosen.TranscriptActivityLog);
        if (log == chosen.TranscriptActivityLog)
            return chosen;

        chosen.TranscriptActivityLog = log ?? "";
        return chosen;
    }

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
