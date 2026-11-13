using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Reads the combined transcripts of participant-track jobs from the workspace and writes the merged
/// dialogue into a job folder of its own: <c>transcript_dialogue_&lt;stamp&gt;.md</c> plus <c>dialogue.json</c>.
/// Source jobs are only read. The refiner is not started: the dialogue waits in <c>awaiting_refiner</c>.
/// </summary>
public sealed class DialogueBuilder : IDialogueBuilder
{
    public const string DialogueTag = "dialogue";
    public const string TranscriptFilePrefix = "transcript_dialogue_";
    public const int MinTracks = 2;
    public const int MaxTracks = 8;
    public const int MaxSpeakerLength = 60;
    public const int MaxTitleLength = 200;

    private readonly IJobStore _store;
    private readonly IJobWorkspace _workspace;
    private readonly ILogger<DialogueBuilder> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DialogueBuilder(IJobStore store, IJobWorkspace workspace, ILogger<DialogueBuilder> logger)
    {
        _store = store;
        _workspace = workspace;
        _logger = logger;
    }

    public async Task<DialogueBuildResult> CreateAsync(DialogueCreateRequest request, CancellationToken ct = default)
    {
        var tracks = ValidateTracks(request.Tracks);
        var title = NormalizeTitle(request.Title) ?? "Dialogue: " + string.Join(" / ", tracks.Select(t => t.Speaker));

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var merge = await MergeTracksAsync(tracks, ct).ConfigureAwait(false);

            var jobId = await _store.CreateAsync(new JobCreateInput(title, new[] { DialogueTag }), ct).ConfigureAwait(false);
            try
            {
                await _workspace.EnsureJobDirectoryAsync(jobId, ct).ConfigureAwait(false);
                var jobDir = _workspace.GetJobDirectoryPath(jobId);
                var now = DateTime.UtcNow;
                var fileName = await WriteMergedFileAsync(jobDir, merge.Markdown, now, ct).ConfigureAwait(false);

                var manifest = new DialogueManifest
                {
                    Title = title,
                    CreatedAt = now.ToString("O"),
                    Tracks = tracks
                        .Select(t => new DialogueManifestTrack { JobId = t.JobId, Speaker = t.Speaker, File = t.File })
                        .ToList()
                };
                manifest.Merges.Add(ToManifestMerge(fileName, now, merge));
                await DialogueManifestFile.SaveAsync(jobDir, manifest, ct).ConfigureAwait(false);

                // The merged transcript is ready to read; the refiner stays a manual step.
                await _store.UpdateAsync(jobId, s =>
                {
                    s.Phase = "awaiting_refiner";
                    s.Status = "done";
                    s.JobDirectoryPath = jobDir;
                }, ct).ConfigureAwait(false);
                var snap = await _store.GetAsync(jobId, ct).ConfigureAwait(false);
                if (snap != null)
                    await JobSnapshotDiskEnricher.TryWriteUiStateAsync(jobDir, snap, ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Dialogue job {JobId} created from {TrackCount} tracks: {File}, {Entries} lines",
                    jobId, tracks.Count, fileName, merge.Entries);
                return new DialogueBuildResult(jobId, fileName, merge.Entries, merge.Tracks, merge.Warnings);
            }
            catch
            {
                // Nothing of the half-made dialogue stays behind; the source jobs were never touched.
                await _store.DeleteAsync(jobId, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DialogueBuildResult> RebuildAsync(string dialogueJobId, CancellationToken ct = default)
    {
        var id = (dialogueJobId ?? "").Trim();
        if (!IsSafeJobId(id))
            throw new DialogueRequestException("job id must be a plain folder name");
        var jobDir = _workspace.GetJobDirectoryPath(id);
        if (!Directory.Exists(jobDir))
            throw new DialogueRequestException($"job '{id}' not found", notFound: true);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var manifest = DialogueManifestFile.TryLoad(jobDir)
                ?? throw new DialogueRequestException($"job '{id}' is not a dialogue: no readable {DialogueManifestFile.FileName} in its folder");
            var tracks = ValidateTracks(manifest.Tracks
                .Select(t => new DialogueTrackRequest(t.JobId, t.Speaker, t.File))
                .ToList());

            var merge = await MergeTracksAsync(tracks, ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var fileName = await WriteMergedFileAsync(jobDir, merge.Markdown, now, ct).ConfigureAwait(false);
            manifest.Merges.Add(ToManifestMerge(fileName, now, merge));
            await DialogueManifestFile.SaveAsync(jobDir, manifest, ct).ConfigureAwait(false);

            _logger.LogInformation("Dialogue job {JobId} merged again: {File}, {Entries} lines", id, fileName, merge.Entries);
            return new DialogueBuildResult(id, fileName, merge.Entries, merge.Tracks, merge.Warnings);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record MergeOutcome(
        string Markdown,
        int Entries,
        IReadOnlyList<DialogueTrackResult> Tracks,
        IReadOnlyList<string> Warnings);

    private async Task<MergeOutcome> MergeTracksAsync(IReadOnlyList<DialogueTrackRequest> tracks, CancellationToken ct)
    {
        var warnings = new List<string>();
        var inputs = new List<DialogueTrack>();
        var results = new List<DialogueTrackResult>();

        foreach (var track in tracks)
        {
            var jobDir = _workspace.GetJobDirectoryPath(track.JobId);
            if (!Directory.Exists(jobDir))
                throw new DialogueRequestException($"job '{track.JobId}' not found", notFound: true);

            var file = ResolveTranscriptFile(track, jobDir);
            var entries = TrackDialogueMerger.Parse(await File.ReadAllTextAsync(file.FullName, Encoding.UTF8, ct).ConfigureAwait(false));
            if (entries.Count == 0)
                throw new DialogueRequestException($"'{file.Name}' of job '{track.JobId}' has no transcript lines (- TIME SPEAKER: \"text\")");

            if (HasCompressedTimesWithoutMap(jobDir))
            {
                warnings.Add(
                    $"{track.Speaker} (job {track.JobId}): the audio was sent with silence cut out and the job has no speech map " +
                    "(timeline/speech_map.json), so its times are not on the timeline of the recording and its lines may stand out of order");
            }

            var untimed = FindUntimedEntries(jobDir, entries, track, warnings);
            if (untimed.Count > 0)
            {
                warnings.Add(
                    $"{track.Speaker} (job {track.JobId}): {untimed.Count} line(s) come from a chunk transcribed without time marks; " +
                    $"they stand at the start of that chunk and are labelled '{track.Speaker}{TrackDialogueMerger.UntimedSuffix}'");
            }

            inputs.Add(new DialogueTrack(track.Speaker, entries, untimed));
            results.Add(new DialogueTrackResult(track.JobId, track.Speaker, file.Name, entries.Count, untimed.Count));
        }

        var markdown = TrackDialogueMerger.Merge(inputs);
        return new MergeOutcome(markdown, results.Sum(r => r.Entries), results, warnings);
    }

    /// <summary>
    /// The transcript of a track: the named file, or the newest combined <c>transcript*.md</c> that is not a
    /// refiner output — the same default the refiner uses for its input.
    /// </summary>
    private static FileInfo ResolveTranscriptFile(DialogueTrackRequest track, string jobDir)
    {
        if (!string.IsNullOrEmpty(track.File))
        {
            var name = Path.GetFileName(track.File);
            var full = Path.Combine(jobDir, name);
            if (!string.Equals(name, track.File, StringComparison.Ordinal)
                || !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(full))
                throw new DialogueRequestException($"file '{track.File}' must be an existing .md directly in the folder of job '{track.JobId}'");
            return new FileInfo(full);
        }

        var newest = new DirectoryInfo(jobDir).EnumerateFiles("transcript*.md")
            .Where(f => !f.Name.Contains("_fixed", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        return newest ?? throw new DialogueRequestException($"job '{track.JobId}' has no combined transcript (transcript*.md) yet");
    }

    /// <summary>
    /// Entries of the transcript that belong to a chunk whose result has no time marks (the diarizing model
    /// failed and a plain model answered): <c>chunks_json/X.json</c> has text but no segments. Such a chunk is
    /// one line in <c>chunks_md/X.md</c>, found in the combined transcript by its time.
    /// </summary>
    private HashSet<int> FindUntimedEntries(
        string jobDir,
        IReadOnlyList<TranscriptEntry> entries,
        DialogueTrackRequest track,
        List<string> warnings)
    {
        var result = new HashSet<int>();
        var jsonDir = Path.Combine(jobDir, "chunks_json");
        if (!Directory.Exists(jsonDir))
            return result;

        foreach (var jsonPath in Directory.EnumerateFiles(jsonDir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsChunkWithoutTimeMarks(jsonPath))
                continue;

            var chunkName = Path.GetFileNameWithoutExtension(jsonPath);
            // The operator split this chunk and its parts were transcribed again: the transcript holds the
            // lines of the parts, and the plain-model text of the whole chunk is no longer in it.
            if (TryFindSplitMergeFolder(jobDir, chunkName) is { } splitFolder)
            {
                WarnAboutPartsWithoutTimeMarks(splitFolder, chunkName, track, warnings);
                continue;
            }

            var mdPath = Path.Combine(jobDir, "chunks_md", chunkName + ".md");
            var matched = 0;
            if (File.Exists(mdPath))
            {
                try
                {
                    foreach (var chunkEntry in TrackDialogueMerger.Parse(File.ReadAllText(mdPath, Encoding.UTF8)))
                    {
                        var sameTime = Enumerable.Range(0, entries.Count)
                            .Where(i => !result.Contains(i) && string.Equals(entries[i].TimeText, chunkEntry.TimeText, StringComparison.Ordinal))
                            .ToList();
                        var pick = sameTime.Count == 1
                            ? sameTime[0]
                            : sameTime.Where(i => string.Equals(entries[i].Body, chunkEntry.Body, StringComparison.Ordinal))
                                .DefaultIfEmpty(-1)
                                .First();
                        if (pick < 0)
                            continue;
                        result.Add(pick);
                        matched++;
                    }
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not read chunk transcript {Path}", mdPath);
                }
            }

            if (matched == 0)
            {
                warnings.Add(
                    $"{track.Speaker} (job {track.JobId}): chunk '{chunkName}' was transcribed without time marks, " +
                    "but its line was not found in the transcript, so it is not labelled");
            }
        }

        return result;
    }

    /// <summary>
    /// True for a job transcribed from silence-compressed audio before agent04 kept a speech map: a
    /// compression report lies in <c>converted_wav/</c> but <c>timeline/speech_map.json</c> does not exist.
    /// The times of such a transcript count seconds of the compressed file, not of the recording.
    /// </summary>
    private static bool HasCompressedTimesWithoutMap(string jobDir)
    {
        try
        {
            var converted = Path.Combine(jobDir, "converted_wav");
            return Directory.Exists(converted)
                && Directory.EnumerateFiles(converted, "*.silence_report.json").Any()
                && !File.Exists(Path.Combine(jobDir, "timeline", "speech_map.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsChunkWithoutTimeMarks(string jsonPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
            return HasTextButNoSegments(doc.RootElement);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A model answer that carries text and no timed segments.</summary>
    private static bool HasTextButNoSegments(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object)
            return false;
        if (response.TryGetProperty("segments", out var segments)
            && segments.ValueKind == JsonValueKind.Array
            && segments.GetArrayLength() > 0)
            return false;
        return response.TryGetProperty("text", out var text)
            && text.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(text.GetString());
    }

    private static readonly Regex ChunkIndexInName = new(@"_part_(\d+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Folder <c>split_chunks/chunk_N</c> of a chunk whose operator split has been merged: it holds
    /// <c>chunk_N_merged.md</c>, the file agent04 puts into the transcript instead of the chunk's own lines.
    /// </summary>
    private static string? TryFindSplitMergeFolder(string jobDir, string chunkName)
    {
        var m = ChunkIndexInName.Match(chunkName);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return null;
        var folder = Path.Combine(jobDir, "split_chunks", "chunk_" + index.ToString(CultureInfo.InvariantCulture));
        var merged = Path.Combine(folder, "chunk_" + index.ToString(CultureInfo.InvariantCulture) + "_merged.md");
        return File.Exists(merged) ? folder : null;
    }

    /// <summary>
    /// A part of a split chunk can fall back to a plain model too. Its line stands at the start of the part;
    /// it is not labelled (the transcript does not say which line came from which part), so the reader is told.
    /// </summary>
    private static void WarnAboutPartsWithoutTimeMarks(string splitFolder, string chunkName, DialogueTrackRequest track, List<string> warnings)
    {
        var resultsDir = Path.Combine(splitFolder, "results");
        if (!Directory.Exists(resultsDir))
            return;

        var untimedParts = 0;
        foreach (var path in Directory.EnumerateFiles(resultsDir, "sub_chunk_*_result.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("raw_response", out var raw)
                    && HasTextButNoSegments(raw))
                    untimedParts++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // an unreadable part result says nothing about time marks
            }
        }

        if (untimedParts > 0)
        {
            warnings.Add(
                $"{track.Speaker} (job {track.JobId}): {untimedParts} part(s) of the split chunk '{chunkName}' were transcribed " +
                "without time marks; their lines stand at the start of the part and are not labelled");
        }
    }

    /// <summary>Writes a new file; an earlier merge (possibly edited by hand) is never overwritten.</summary>
    private static async Task<string> WriteMergedFileAsync(string jobDir, string markdown, DateTime nowUtc, CancellationToken ct)
    {
        var stamp = nowUtc.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        for (var attempt = 0; ; attempt++)
        {
            var name = TranscriptFilePrefix + stamp + (attempt == 0 ? "" : "_" + attempt.ToString(CultureInfo.InvariantCulture)) + ".md";
            var path = Path.Combine(jobDir, name);
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                var bytes = new UTF8Encoding(false).GetBytes(markdown);
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                return name;
            }
            catch (IOException) when (File.Exists(path) && attempt < 100)
            {
                // Same millisecond as an earlier merge: take the next free name.
            }
        }
    }

    private static DialogueManifestMerge ToManifestMerge(string fileName, DateTime nowUtc, MergeOutcome merge) => new()
    {
        File = fileName,
        CreatedAt = nowUtc.ToString("O"),
        Entries = merge.Entries,
        Tracks = merge.Tracks
            .Select(t => new DialogueManifestMergeTrack
            {
                JobId = t.JobId,
                Speaker = t.Speaker,
                File = t.File,
                Entries = t.Entries,
                UntimedEntries = t.UntimedEntries
            })
            .ToList(),
        Warnings = merge.Warnings.Count > 0 ? merge.Warnings.ToList() : null
    };

    private static IReadOnlyList<DialogueTrackRequest> ValidateTracks(IReadOnlyList<DialogueTrackRequest>? tracks)
    {
        if (tracks == null || tracks.Count < MinTracks)
            throw new DialogueRequestException($"a dialogue needs at least {MinTracks} tracks");
        if (tracks.Count > MaxTracks)
            throw new DialogueRequestException($"a dialogue takes at most {MaxTracks} tracks");

        var result = new List<DialogueTrackRequest>();
        var speakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in tracks)
        {
            var jobId = (track?.JobId ?? "").Trim();
            if (!IsSafeJobId(jobId))
                throw new DialogueRequestException("every track needs a jobId that is a plain folder name");

            var speaker = (track?.Speaker ?? "").Trim();
            if (speaker.Length == 0 || speaker.Length > MaxSpeakerLength)
                throw new DialogueRequestException($"track '{jobId}': speaker is required and must be at most {MaxSpeakerLength} characters");
            if (speaker.IndexOfAny(new[] { ':', '"', '\r', '\n' }) >= 0)
                throw new DialogueRequestException($"track '{jobId}': speaker must not contain ':' or '\"' or line breaks");
            if (!speakers.Add(speaker))
                throw new DialogueRequestException($"speaker '{speaker}' is used by more than one track");

            var file = string.IsNullOrWhiteSpace(track?.File) ? null : track!.File!.Trim();
            result.Add(new DialogueTrackRequest(jobId, speaker, file));
        }
        return result;
    }

    private static bool IsSafeJobId(string id) =>
        id.Length > 0
        && id != "."
        && id != ".."
        && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && string.Equals(Path.GetFileName(id), id, StringComparison.Ordinal);

    private static string? NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        var oneLine = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length > MaxTitleLength ? oneLine[..MaxTitleLength] : oneLine;
    }
}
