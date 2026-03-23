using System.Collections.Concurrent;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class ChunkProgress : IChunkProgress
{
    private readonly ConcurrentDictionary<int, DateTimeOffset> _started = new();
    private readonly ConcurrentDictionary<int, byte> _completed = new();
    private readonly ConcurrentDictionary<int, byte> _cancelled = new();
    private readonly string _timeFormat;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    public int Total { get; }

    public ChunkProgress(int totalChunks, string timeFormat = "HH:MM:SS.M")
    {
        Total = totalChunks;
        _timeFormat = timeFormat;
    }

    public void MarkStarted(int chunkIndex)
    {
        _started.TryAdd(chunkIndex, DateTimeOffset.UtcNow);
    }

    public void MarkCompleted(int chunkIndex)
    {
        _started.TryRemove(chunkIndex, out _);
        _completed.TryAdd(chunkIndex, 0);
    }

    public void MarkCancelled(int chunkIndex)
    {
        _started.TryRemove(chunkIndex, out _);
        _cancelled.TryAdd(chunkIndex, 0);
    }

    public void Update()
    {
        var parts = new List<string>();
        for (var i = 0; i < Total; i++)
        {
            if (_completed.ContainsKey(i))
                parts.Add($"[{i + 1}:OK]");
            else if (_cancelled.ContainsKey(i))
                parts.Add($"[{i + 1}:CANCEL]");
            else if (_started.TryGetValue(i, out var t))
                parts.Add($"[{i + 1}:{FormatElapsed((DateTimeOffset.UtcNow - t).TotalSeconds)}]");
            else
                parts.Add($"[{i + 1}]");
        }
        Console.Write("\r" + string.Join(" ", parts));
    }

    public void Complete()
    {
        Update();
        var elapsed = (DateTimeOffset.UtcNow - _startTime).TotalSeconds;
        var elapsedStr = elapsed < 60 ? $"{elapsed:F1}s" : elapsed < 3600
            ? $"{(int)(elapsed / 60)}m {(int)(elapsed % 60)}s"
            : $"{(int)(elapsed / 3600)}h {(int)((elapsed % 3600) / 60)}m";
        Console.WriteLine($"\n\nCompleted {_completed.Count}/{Total} chunks (cancelled {_cancelled.Count}). Total time: {elapsedStr}\n");
    }

    private string FormatElapsed(double seconds)
    {
        var h = (int)(seconds / 3600);
        var m = (int)((seconds % 3600) / 60);
        var s = (int)(seconds % 60);
        var dec = (int)((seconds % 1) * 10);
        if (_timeFormat.Contains("HH", StringComparison.Ordinal) || _timeFormat.Contains('H'))
            return $"{h:02d}:{m:02d}:{s:02d}.{dec}";
        var totalM = (int)(seconds / 60);
        return $"{totalM:03d}:{s:03d}.{dec}";
    }
}
