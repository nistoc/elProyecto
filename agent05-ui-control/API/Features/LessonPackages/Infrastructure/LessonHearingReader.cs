using System.Collections.Concurrent;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Features.LessonPackages.Application;

namespace XtractManager.Features.LessonPackages.Infrastructure;

/// <summary>
/// The hearings of a recording, read from the chunk answers of its jobs (<see cref="JobTimedLinesReader"/>) and
/// kept in memory by job: a job is read again when the number, size or newest write time of its chunk answers
/// or of its timeline changes. Nothing is written.
/// </summary>
public sealed class LessonHearingReader : ILessonHearings
{
    private readonly IJobWorkspace _workspace;
    private readonly ConcurrentDictionary<string, (string Signature, IReadOnlyList<HeardLine>? Lines)> _text = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string Signature, IReadOnlyList<TimedLine>? Lines)> _other = new(StringComparer.Ordinal);

    public LessonHearingReader(IJobWorkspace workspace)
    {
        _workspace = workspace;
    }

    public LessonHearing? Read(LessonHearingSource source)
    {
        var lines = new List<HeardLine>();
        foreach (var job in source.TextJobs)
        {
            if (Cached(_text, job, JobTimedLinesReader.ReadHeard) is { } heard)
                lines.AddRange(heard);
        }

        if (lines.Count == 0)
            return null;
        var other = new List<TimedLine>();
        foreach (var job in source.OtherJobs)
        {
            if (Cached(_other, job, JobTimedLinesReader.Read) is { } read)
                other.AddRange(read);
        }

        return new LessonHearing(
            lines.OrderBy(l => l.Start).ToList(),
            other.OrderBy(l => l.Start).ToList());
    }

    private IReadOnlyList<T>? Cached<T>(
        ConcurrentDictionary<string, (string Signature, IReadOnlyList<T>? Lines)> cache,
        string jobId,
        Func<string, IReadOnlyList<T>?> read)
    {
        var directory = _workspace.GetJobDirectoryPath(jobId);
        var signature = Signature(directory);
        if (cache.TryGetValue(jobId, out var hit) && hit.Signature == signature)
            return hit.Lines;
        var lines = read(directory);
        cache[jobId] = (signature, lines);
        return lines;
    }

    private static string Signature(string directory)
    {
        try
        {
            var files = new[] { "chunks_json", "timeline" }
                .Select(d => new DirectoryInfo(Path.Combine(directory, d)))
                .Where(d => d.Exists)
                .SelectMany(d => d.EnumerateFiles("*.json"))
                .ToList();
            return $"{files.Count}:{files.Sum(f => f.Length)}:{(files.Count == 0 ? 0 : files.Max(f => f.LastWriteTimeUtc.Ticks))}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // read again next time
            return Guid.NewGuid().ToString("N");
        }
    }
}
