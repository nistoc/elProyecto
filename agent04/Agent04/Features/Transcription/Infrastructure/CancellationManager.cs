using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class CancellationManager : ICancellationManager
{
    private readonly string _cancelDir;
    private readonly ConcurrentDictionary<int, byte> _seen = new();
    private readonly ConcurrentDictionary<(int Parent, int Sub), byte> _seenSub = new();
    private static readonly Regex Pattern = new(@"cancel_(\d+)\.flag$", RegexOptions.Compiled);
    private static readonly Regex SubPattern = new(@"cancel_sub_(\d+)_(\d+)\.flag$", RegexOptions.Compiled);

    public CancellationManager(string cancelDir)
    {
        _cancelDir = cancelDir ?? "cancel_signals";
        Directory.CreateDirectory(_cancelDir);
    }

    public IReadOnlySet<int> Poll()
    {
        var found = ScanDir();
        var @new = new HashSet<int>();
        foreach (var idx in found)
            if (_seen.TryAdd(idx, 0)) @new.Add(idx);
        ScanSubDir();
        return @new;
    }

    public bool IsCancelled(int chunkIndex)
    {
        if (_seen.ContainsKey(chunkIndex)) return true;
        _ = Poll();
        return _seen.ContainsKey(chunkIndex);
    }

    public void MarkCancelled(int chunkIndex)
    {
        var path = Path.Combine(_cancelDir, $"cancel_{chunkIndex}.flag");
        try { File.WriteAllText(path, "cancelled"); } catch { /* best-effort */ }
        _seen.TryAdd(chunkIndex, 0);
    }

    public void MarkSubChunkCancelled(int parentChunkIndex, int subChunkIndex)
    {
        var path = Path.Combine(_cancelDir, $"cancel_sub_{parentChunkIndex}_{subChunkIndex}.flag");
        try { File.WriteAllText(path, "cancelled"); } catch { /* best-effort */ }
        _seenSub.TryAdd((parentChunkIndex, subChunkIndex), 0);
    }

    public bool IsSubChunkCancelled(int parentChunkIndex, int subChunkIndex)
    {
        if (_seenSub.ContainsKey((parentChunkIndex, subChunkIndex))) return true;
        _ = Poll();
        return _seenSub.ContainsKey((parentChunkIndex, subChunkIndex));
    }

    private void ScanSubDir()
    {
        if (!Directory.Exists(_cancelDir)) return;
        foreach (var name in Directory.EnumerateFiles(_cancelDir))
        {
            var m = SubPattern.Match(Path.GetFileName(name));
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out var parent) ||
                !int.TryParse(m.Groups[2].Value, out var sub))
                continue;
            _seenSub.TryAdd((parent, sub), 0);
        }
    }

    private HashSet<int> ScanDir()
    {
        var found = new HashSet<int>();
        if (!Directory.Exists(_cancelDir)) return found;
        foreach (var name in Directory.EnumerateFiles(_cancelDir))
        {
            var m = Pattern.Match(Path.GetFileName(name));
            if (m.Success && int.TryParse(m.Groups[1].Value, out var idx))
                found.Add(idx);
        }
        return found;
    }
}
