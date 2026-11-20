using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.LessonPackages.Application;
using XtractManager.Features.Sessions.Application;
using XtractManager.Features.Sessions.Infrastructure;

namespace XtractManager.Features.LessonPackages.Infrastructure;

/// <summary>
/// The words of every lesson, read from the transcripts in the job folders. For a recording that has a
/// dialogue, the newest dialogue is read; for one that has none, the newest run of each of its tracks.
/// Of a job the raw transcript is read, and of its refined one only what the raw text has
/// (<see cref="SaidText"/>): a refine may have translated Russian speech, and a translated word was not said.
/// A lesson of a day folder with no transcript in any job is read from the answer files of another
/// recognition service that lie in that folder (<see cref="AnswersOfDay"/>). Nothing is kept on disk:
/// a lesson is read again when the set, size or write time of its files changes, so a lesson transcribed
/// today is counted the next time a package is opened.
/// </summary>
public sealed class LessonWordIndex : ILessonWordIndex
{
    /// <summary>The files the words of one job are read from.</summary>
    /// <param name="Raw">The newest combined transcript (<c>transcript.md</c>, or the merge of a dialogue).</param>
    /// <param name="Refined">The newest refined transcript.</param>
    /// <param name="Answers">The chunk answers of a job that has a refined transcript and no raw one.</param>
    public sealed record JobText(FileInfo? Raw, FileInfo? Refined, IReadOnlyList<FileInfo> Answers)
    {
        public IEnumerable<FileInfo> Files =>
            new[] { Raw, Refined }.Where(f => f != null).Select(f => f!).Concat(Answers);
    }

    /// <summary>What one lesson is read from: the text of a job, or an answer file of a day folder.</summary>
    private sealed record Source(JobText? Job, FileInfo? DayAnswer)
    {
        public IEnumerable<FileInfo> Files => Job?.Files ?? new[] { DayAnswer! };
    }

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
        var files = new SortedDictionary<string, List<Source>>(StringComparer.Ordinal);
        var mixedTrackByJob = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            var ofSession = new List<Source>();
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
                    if (TextOf(_workspace.GetJobDirectoryPath(jobId)) is { } text)
                        ofSession.Add(new Source(text, null));
                }
            }

            // A day folder none of whose recordings was transcribed by a job: what another service answered.
            if (ofSession.Count == 0 && !session.Loose && _catalog.RootPath is { } root)
                ofSession.AddRange(AnswersOfDay(Path.Combine(root, session.Id)).Select(f => new Source(null, f)));

            if (ofSession.Count == 0)
                continue;
            if (!files.TryGetValue(session.Date, out var ofLesson))
                files[session.Date] = ofLesson = new List<Source>();
            ofLesson.AddRange(ofSession);
        }

        var signatures = files.ToDictionary(
            f => f.Key,
            f => string.Join('|', f.Value
                .SelectMany(source => source.Files)
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
            .Select(t => TrackTextRuns.TextOrder(t.Runs).FirstOrDefault(r => r.HasTranscript || r.HasRefined)?.JobId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();
    }

    /// <summary>
    /// The files a job is read from: its newest combined transcript and its newest refined one; for a job
    /// that kept a refined transcript and no combined one, the chunk answers stand in for the raw text.
    /// Null when the job has no transcript.
    /// </summary>
    public static JobText? TextOf(string jobDirectory)
    {
        try
        {
            if (!Directory.Exists(jobDirectory))
                return null;
            var transcripts = new DirectoryInfo(jobDirectory).EnumerateFiles("transcript*.md")
                .Where(f => !f.Name.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            var refined = transcripts.FirstOrDefault(IsRefined);
            var raw = transcripts.FirstOrDefault(f => !IsRefined(f));
            if (refined == null && raw == null)
                return null;

            IReadOnlyList<FileInfo> answers = Array.Empty<FileInfo>();
            var chunkAnswers = new DirectoryInfo(Path.Combine(jobDirectory, "chunks_json"));
            if (raw == null && chunkAnswers.Exists)
                answers = chunkAnswers.EnumerateFiles("*.json").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return new JobText(raw, refined, answers);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        static bool IsRefined(FileInfo f) => f.Name.Contains("_fixed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The answer files of another recognition service that lie flat in a day folder next to the audio:
    /// every <c>*.json</c> but the <c>response.json</c> of the old pipeline. Whether a file is such an answer
    /// (<c>segments[].text</c>) is seen when it is read; a file of another shape adds no line.
    /// </summary>
    public static IReadOnlyList<FileInfo> AnswersOfDay(string dayDirectory)
    {
        try
        {
            var day = new DirectoryInfo(dayDirectory);
            if (!day.Exists)
                return Array.Empty<FileInfo>();
            return day.EnumerateFiles("*.json")
                .Where(f => !f.Name.EndsWith("response.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<FileInfo>();
        }
    }

    /// <returns>The words of the lesson; null when one of its files could not be read.</returns>
    private LessonWords? ReadLesson(string date, IEnumerable<Source> sources)
    {
        var lines = new List<string>();
        FileInfo? reading = null;
        try
        {
            foreach (var source in sources)
            {
                if (source.DayAnswer is { } answer)
                {
                    lines.AddRange(SegmentTexts(reading = answer));
                    continue;
                }

                var job = source.Job!;
                var raw = new List<string>();
                if (job.Raw != null)
                    raw.AddRange(TranscriptLines(reading = job.Raw));
                foreach (var chunk in job.Answers)
                    raw.AddRange(SegmentTexts(reading = chunk));
                var refined = job.Refined != null ? TranscriptLines(reading = job.Refined) : new List<string>();
                lines.AddRange(SaidText.Lines(raw, refined));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex, "{File} cannot be read; the lesson of {Date} stays as it was read before, or is not counted, until it can", reading?.FullName, date);
            return null;
        }

        return new LessonWords(date, lines);
    }

    private static List<string> TranscriptLines(FileInfo file) =>
        TrackDialogueMerger.Parse(File.ReadAllText(file.FullName, Encoding.UTF8)).Select(e => e.Body).ToList();

    /// <summary>
    /// The text of every segment of an answer (<c>segments[].text</c>), or its whole <c>text</c> when it has
    /// no segments. A file that is not such an answer gives nothing.
    /// </summary>
    private static List<string> SegmentTexts(FileInfo file)
    {
        var texts = new List<string>();
        try
        {
            // The transcriber may be writing the file right now: share it instead of failing on a lock.
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var answer = doc.RootElement;
            if (answer.ValueKind != JsonValueKind.Object)
                return texts;
            if (answer.TryGetProperty("segments", out var segments) && segments.ValueKind == JsonValueKind.Array)
            {
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.ValueKind == JsonValueKind.Object
                        && segment.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                        && text.GetString() is { Length: > 0 } line)
                        texts.Add(line);
                }
            }

            if (texts.Count == 0 && answer.TryGetProperty("text", out var whole) && whole.ValueKind == JsonValueKind.String
                && whole.GetString() is { Length: > 0 } all)
                texts.Add(all);
        }
        catch (JsonException)
        {
            // not an answer file: it adds no line
        }

        return texts;
    }
}
