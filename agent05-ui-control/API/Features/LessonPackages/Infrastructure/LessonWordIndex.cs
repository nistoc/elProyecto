using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.LessonPackages.Application;
using XtractManager.Features.Sessions.Application;
using XtractManager.Features.Sessions.Infrastructure;

namespace XtractManager.Features.LessonPackages.Infrastructure;

/// <summary>
/// The words of every lesson, read from the transcripts in the job folders. For a recording that has a
/// dialogue, the newest dialogue is read; for one that has none, the newest run of each of its tracks.
/// Of a job the newest refined transcript is read, else the newest combined one. Nothing is kept on disk:
/// a lesson is read again when the set, size or write time of its files changes, so a lesson transcribed
/// today is counted the next time a package is opened.
/// </summary>
public sealed class LessonWordIndex : ILessonWordIndex
{
    private readonly IRecordingsCatalog _catalog;
    private readonly IJobStore _store;
    private readonly IJobWorkspace _workspace;
    private readonly ILogger<LessonWordIndex> _logger;
    /// <summary>How many lessons are read at once: the files lie on one disk, more readers do not make it faster.</summary>
    private const int ReadersAtOnce = 4;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _reading = new(1, 1);
    private readonly Dictionary<string, (string Signature, LessonWords Words)> _cache = new(StringComparer.Ordinal);

    public LessonWordIndex(
        IRecordingsCatalog catalog,
        IJobStore store,
        IJobWorkspace workspace,
        ILogger<LessonWordIndex> logger)
    {
        _catalog = catalog;
        _store = store;
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<LessonsRead> ReadAsync(CancellationToken ct = default)
    {
        if (!_catalog.Enabled)
            return LessonsRead.Nothing;

        var sessions = await LessonSessions.WithRunsAsync(_catalog, _store, _workspace, ct);
        var files = new SortedDictionary<string, List<FileInfo>>(StringComparer.Ordinal);
        var mixedTrackByJob = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var session in sessions)
        foreach (var period in session.Periods)
        {
            if (period.Tracks.FirstOrDefault(t => t.Kind == RecordingsScanner.KindMixed) is { } mixed)
            {
                foreach (var jobId in period.Dialogues.Select(d => d.JobId)
                             .Concat(period.Tracks.SelectMany(t => t.Runs).Select(r => r.JobId)))
                    mixedTrackByJob[jobId] = mixed.Id;
            }

            foreach (var jobId in JobsToRead(period))
            {
                var file = BestTranscript(_workspace.GetJobDirectoryPath(jobId));
                if (file == null)
                    continue;
                if (!files.TryGetValue(session.Date, out var ofLesson))
                    files[session.Date] = ofLesson = new List<FileInfo>();
                ofLesson.Add(file);
            }
        }

        var signatures = files.ToDictionary(
            f => f.Key,
            f => string.Join('|', f.Value
                .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(file => $"{file.FullName}:{file.Length}:{file.LastWriteTimeUtc.Ticks}")),
            StringComparer.Ordinal);

        // One reading at a time: a request that comes meanwhile waits for it (and may be cancelled) and then
        // finds the lessons kept, instead of reading the same files once more.
        await _reading.WaitAsync(ct);
        try
        {
            List<string> changed;
            lock (_gate)
            {
                foreach (var gone in _cache.Keys.Where(date => !files.ContainsKey(date)).ToList())
                    _cache.Remove(gone);
                changed = files.Keys
                    .Where(date => !_cache.TryGetValue(date, out var hit) || hit.Signature != signatures[date])
                    .ToList();
            }

            if (changed.Count > 0)
            {
                var watch = Stopwatch.StartNew();
                var read = new ConcurrentDictionary<string, LessonWords>(StringComparer.Ordinal);
                try
                {
                    await Parallel.ForEachAsync(
                        changed,
                        new ParallelOptions { MaxDegreeOfParallelism = ReadersAtOnce, CancellationToken = ct },
                        (date, _) =>
                        {
                            // A lesson with a file that could not be read is not kept: it is read again next time.
                            if (ReadLesson(date, files[date]) is { } words)
                                read[date] = words;
                            return ValueTask.CompletedTask;
                        });
                }
                finally
                {
                    // What was read before a cancel is kept too: the next request does not read it again.
                    lock (_gate)
                    {
                        foreach (var (date, words) in read)
                            _cache[date] = (signatures[date], words);
                    }

                    _logger.LogInformation(
                        "Words of {Read} of {Changed} lessons read anew in {Ms} ms", read.Count, changed.Count, watch.ElapsedMilliseconds);
                }
            }
        }
        finally
        {
            _reading.Release();
        }

        var lessons = new List<LessonWords>();
        lock (_gate)
        {
            // Oldest first: the dates are the keys of a sorted dictionary.
            foreach (var date in files.Keys)
            {
                if (_cache.TryGetValue(date, out var hit) && hit.Words.Forms.Count > 0)
                    lessons.Add(hit.Words);
            }
        }

        return new LessonsRead(lessons, mixedTrackByJob);
    }

    /// <summary>The jobs whose transcripts are the text of a recording.</summary>
    public static IReadOnlyList<string> JobsToRead(RecordingPeriod period)
    {
        if (period.Dialogues.Count > 0)
            return new[] { period.Dialogues.OrderBy(d => d.CreatedAt ?? "", StringComparer.Ordinal).Last().JobId };
        return period.Tracks
            .Select(t => t.Runs.LastOrDefault(r => r.HasTranscript || r.HasRefined)?.JobId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();
    }

    /// <summary>The newest refined transcript of a job, else its newest combined one; null when it has none.</summary>
    public static FileInfo? BestTranscript(string jobDirectory)
    {
        try
        {
            if (!Directory.Exists(jobDirectory))
                return null;
            var transcripts = new DirectoryInfo(jobDirectory).EnumerateFiles("transcript*.md")
                .Where(f => !f.Name.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            return transcripts.FirstOrDefault(f => f.Name.Contains("_fixed", StringComparison.OrdinalIgnoreCase))
                   ?? transcripts.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <returns>The words of the lesson; null when one of its files could not be read.</returns>
    private LessonWords? ReadLesson(string date, IEnumerable<FileInfo> files)
    {
        var lines = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var entries = TrackDialogueMerger.Parse(File.ReadAllText(file.FullName, Encoding.UTF8));
                lines.AddRange(entries.Select(e => e.Body));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    ex, "{File} cannot be read; the lesson of {Date} stays as it was read before, or is not counted, until it can", file.FullName, date);
                return null;
            }
        }

        return new LessonWords(date, lines);
    }
}
