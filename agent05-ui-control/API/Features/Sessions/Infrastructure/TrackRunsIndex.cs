using System.Collections.Concurrent;
using System.Security.Cryptography;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Features.Sessions.Infrastructure;

/// <summary>
/// Which jobs of the workspace transcribed which track. A job is tied to a track by:
/// <list type="number">
/// <item>its <c>track_source.json</c> (jobs started from the lessons list);</item>
/// <item>the name and size of its audio file (jobs made by a plain upload; a track whose folder was moved);</item>
/// <item>the size and the first bytes of its audio file (a plain upload of the track under another name).</item>
/// </list>
/// Dialogue jobs are tied to the period whose runs they were merged from. Job folders are only read.
/// </summary>
public sealed class TrackRunsIndex
{
    /// <summary>How much of a file is compared when only the size matches.</summary>
    public const int HeadBytes = 64 * 1024;

    private static readonly ConcurrentDictionary<string, (long Size, long ModifiedTicks, string Hash)> HeadHashes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string? _recordingsRoot;
    private readonly Dictionary<string, List<TrackRun>> _byTrack = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, long Size), List<TrackRun>> _byFile = new();
    private readonly Dictionary<long, List<(TrackRun Run, FileInfo Audio)>> _bySize = new();
    private readonly List<PeriodDialogue> _dialogues = new();

    private TrackRunsIndex(string? recordingsRoot) => _recordingsRoot = recordingsRoot;

    /// <param name="recordingsRoot">The recordings folder; without it a track is not compared by content.</param>
    public static TrackRunsIndex Build(string workspaceRoot, IReadOnlyList<JobListItem> jobs, string? recordingsRoot = null)
    {
        var index = new TrackRunsIndex(recordingsRoot);
        JobLanguageShareCache.Warm(jobs.Select(job => Path.Combine(workspaceRoot, job.Id)));
        foreach (var job in jobs)
        {
            var dir = Path.Combine(workspaceRoot, job.Id);
            try
            {
                if (Directory.Exists(dir))
                    index.Add(job, new DirectoryInfo(dir));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read is just not shown as a run.
            }
        }

        return index;
    }

    private void Add(JobListItem job, DirectoryInfo dir)
    {
        var dialogue = DialogueManifestFile.TryLoad(dir.FullName);
        if (dialogue != null)
        {
            _dialogues.Add(new PeriodDialogue(
                job.Id,
                dialogue.Title,
                dialogue.CreatedAt ?? job.CreatedAt,
                dialogue.Tracks.Select(t => t.JobId).Where(id => !string.IsNullOrEmpty(id)).ToList(),
                dialogue.Merges.Count));
            return;
        }

        var files = dir.EnumerateFiles().ToList();
        var transcripts = files
            .Where(f => f.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                        && f.Name.StartsWith("transcript", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var run = new TrackRun(
            job.Id,
            job.Status,
            job.Phase,
            job.CreatedAt,
            HasTranscript: transcripts.Any(f => !f.Name.Contains("_fixed", StringComparison.OrdinalIgnoreCase)),
            HasRefined: transcripts.Any(f => f.Name.Contains("_fixed", StringComparison.OrdinalIgnoreCase)))
        {
            Settings = JobRunSettingsReader.Read(dir.FullName),
            Share = JobLanguageShareCache.Read(dir.FullName),
        };

        var source = TrackSourceFile.TryLoad(dir.FullName);
        if (source != null)
        {
            Put(_byTrack, source.Track.Replace('\\', '/'), run);
            if (!string.IsNullOrEmpty(source.FileName) && source.SizeBytes > 0)
                Put(_byFile, FileKey(source.FileName, source.SizeBytes), run);
        }

        foreach (var audio in files.Where(f => RecordingsScanner.IsAudioFile(f.Name) && f.Length > 0))
        {
            Put(_byFile, FileKey(audio.Name, audio.Length), run);
            if (!_bySize.TryGetValue(audio.Length, out var sameSize))
                _bySize[audio.Length] = sameSize = new List<(TrackRun, FileInfo)>();
            sameSize.Add((run, audio));
        }
    }

    /// <summary>The period with the runs of its tracks and the dialogues merged from them.</summary>
    public RecordingPeriod Attach(RecordingPeriod period)
    {
        var tracks = period.Tracks.Select(t => t with { Runs = RunsOf(t) }).ToList();
        var jobIds = tracks.SelectMany(t => t.Runs).Select(r => r.JobId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dialogues = _dialogues
            .Where(d => d.TrackJobIds.Any(jobIds.Contains))
            .OrderBy(d => d.CreatedAt, StringComparer.Ordinal)
            .ToList();
        return period with { Tracks = tracks, Dialogues = dialogues };
    }

    public IReadOnlyList<TrackRun> RunsOf(RecordingTrack track)
    {
        var runs = new Dictionary<string, TrackRun>(StringComparer.OrdinalIgnoreCase);
        if (_byTrack.TryGetValue(track.Id, out var byTrack))
            foreach (var r in byTrack)
                runs[r.JobId] = r;
        if (_byFile.TryGetValue(FileKey(track.FileName, track.SizeBytes), out var byFile))
            foreach (var r in byFile)
                runs[r.JobId] = r;

        // Same size under another name: the same recording only when the first bytes are the same too.
        if (_recordingsRoot != null && _bySize.TryGetValue(track.SizeBytes, out var sameSize))
        {
            string? trackHead = null;
            foreach (var (run, audio) in sameSize)
            {
                if (runs.ContainsKey(run.JobId))
                    continue;
                trackHead ??= HeadHash(new FileInfo(
                    Path.Combine(_recordingsRoot, track.Id.Replace('/', Path.DirectorySeparatorChar)))) ?? "";
                if (trackHead.Length > 0 && trackHead == HeadHash(audio))
                    runs[run.JobId] = run;
            }
        }

        return runs.Values
            .OrderBy(r => r.CreatedAt, StringComparer.Ordinal)
            .ThenBy(r => r.JobId, StringComparer.Ordinal)
            .ToList();
    }

    // The job folder holds the file under the name the upload gave it (JobWorkspace.SanitizeUploadedFileName).
    private static (string Name, long Size) FileKey(string fileName, long size) =>
        (JobWorkspace.SanitizeUploadedFileName(fileName).ToLowerInvariant(), size);

    /// <summary>
    /// Something equal for two files with the same content, as far as this index tells it: the size and
    /// the first bytes. Null when the file cannot be read.
    /// </summary>
    public static string? ContentKey(string fullPath)
    {
        var file = new FileInfo(fullPath);
        var head = HeadHash(file);
        return head == null ? null : file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + head;
    }

    /// <summary>SHA-256 of the first <see cref="HeadBytes"/> of the file; remembered until the file changes.</summary>
    private static string? HeadHash(FileInfo file)
    {
        try
        {
            file.Refresh();
            if (!file.Exists)
                return null;
            var size = file.Length;
            var modified = file.LastWriteTimeUtc.Ticks;
            if (HeadHashes.TryGetValue(file.FullName, out var known) && known.Size == size && known.ModifiedTicks == modified)
                return known.Hash;

            var buffer = new byte[(int)Math.Min(HeadBytes, size)];
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                stream.ReadExactly(buffer);
            var hash = Convert.ToHexString(SHA256.HashData(buffer));
            HeadHashes[file.FullName] = (size, modified, hash);
            return hash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Put<TKey>(Dictionary<TKey, List<TrackRun>> map, TKey key, TrackRun run)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = new List<TrackRun>();
        if (!list.Any(r => r.JobId == run.JobId))
            list.Add(run);
    }
}
