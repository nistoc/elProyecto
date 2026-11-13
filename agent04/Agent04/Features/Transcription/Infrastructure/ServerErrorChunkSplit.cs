using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// What is done with a chunk the server refused three times (HTTP 5xx): the chunk is cut into parts, each
/// part is transcribed on its own, and the parts are put back into one answer of the chunk, with times
/// counted from the start of the chunk.
/// <para>
/// Without it the client's next request has no <c>response_format</c>, and a diarize model answers plain
/// text: the whole chunk becomes one line without time marks. Shorter pieces of the same audio pass.
/// </para>
/// <para>
/// The answer has the shape of a server answer (<c>text</c>, <c>segments</c>, <c>usage</c>), so the cache,
/// <c>chunks_json</c> and the md writers read it as any other; the block <see cref="RawBlockKey"/> says
/// that it was assembled here and from what.
/// </para>
/// </summary>
public sealed class ServerErrorChunkSplit
{
    /// <summary>Config key: into how many parts a refused chunk is cut. 0 or 1 switches the split off.</summary>
    public const string PartsConfigKey = "server_error_split_parts";

    public const int DefaultParts = 4;
    public const int MaxParts = 16;

    /// <summary>A part is not made shorter than this; a chunk too short for two such parts is not cut.</summary>
    public const double MinPartSec = 10.0;

    /// <summary>Key of the block in the chunk's answer that describes the split.</summary>
    public const string RawBlockKey = "auto_split";

    private readonly IAudioUtils _audioUtils;
    private readonly ITranscriptionClient _client;
    private readonly ITranscriptionMerger _merger;
    private readonly ILogger? _logger;

    public ServerErrorChunkSplit(
        IAudioUtils audioUtils,
        ITranscriptionClient client,
        ITranscriptionMerger merger,
        ILogger? logger = null)
    {
        _audioUtils = audioUtils;
        _client = client;
        _merger = merger;
        _logger = logger;
    }

    /// <summary>Parts named by the config (default <see cref="DefaultParts"/>); below 2 the split is off.</summary>
    public static int ConfiguredParts(TranscriptionConfig config)
    {
        var parts = config.Get<int?>(PartsConfigKey) ?? DefaultParts;
        return parts < 2 ? 0 : Math.Min(parts, MaxParts);
    }

    /// <summary>
    /// Parts for a chunk of this length: the configured number, fewer when the parts would be shorter than
    /// <see cref="MinPartSec"/>, 0 when the chunk is not to be cut at all.
    /// </summary>
    public static int PartsFor(TranscriptionConfig config, double chunkDurationSec)
    {
        var parts = ConfiguredParts(config);
        if (parts < 2 || chunkDurationSec <= 0)
            return 0;
        parts = Math.Min(parts, (int)Math.Floor(chunkDurationSec / MinPartSec));
        return parts < 2 ? 0 : parts;
    }

    /// <summary>
    /// Cuts the chunk into <paramref name="parts"/> parts, transcribes them one after another and returns
    /// one answer of the chunk. A part that came back without time marks stands at the start of that part.
    /// Throws when a part cannot be cut or transcribed at all; the part files are removed in any case.
    /// </summary>
    /// <param name="options">Options of the chunk's own request; every part is asked the same way.</param>
    /// <param name="activity">Lines for the chunk's activity log (shown in the UI).</param>
    public async Task<TranscriptionClientResult> TranscribeInPartsAsync(
        string chunkPath,
        double chunkDurationSec,
        int parts,
        double overlapSec,
        string ffmpegPath,
        TranscriptionClientOptions options,
        TranscriptionServerErrorException cause,
        Action<string>? activity = null,
        CancellationToken ct = default)
    {
        var plan = OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap(chunkDurationSec, parts, overlapSec);
        var chunkStem = Path.GetFileNameWithoutExtension(chunkPath);
        var extension = Path.GetExtension(chunkPath);
        var partNames = Enumerable.Range(0, plan.Count).Select(k => $"{chunkStem}_auto_{k:D2}{extension}").ToList();
        // Same arithmetic as an operator split: where a part starts and how much of it repeats the previous one.
        var timeline = SubChunkTimelineFile.FromPlan(options.ChunkIndex ?? 0, chunkDurationSec, plan, partNames);

        var workDir = Path.Combine(Path.GetTempPath(), "agent04_auto_split_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var partResults = new List<(int SubIndex, TranscriptionResult Result)>(plan.Count);
            var partRecords = new List<object?>(plan.Count);
            var usages = new List<IReadOnlyDictionary<string, object?>>(plan.Count);
            var withoutTimeMarks = 0;

            for (var k = 0; k < plan.Count; k++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = timeline.SubChunks[k];
                var partPath = Path.Combine(workDir, partNames[k]);
                _audioUtils.ExtractAudioSegmentCopyOrReencode(ffmpegPath, chunkPath, entry.OffsetSec, entry.DurationSec, partPath);

                // A part is asked the usual way: if the server refuses it too, the client's own fallbacks apply.
                var partOptions = options.Clone();
                partOptions.StopAtServerError = false;
                var answer = await _client.TranscribeAsync(partPath, partOptions, ct).ConfigureAwait(false);

                var timed = HasTimeMarks(answer.RawResponse);
                if (!timed)
                    withoutTimeMarks++;
                partResults.Add((k, new TranscriptionResult(partNames[k], entry.OffsetSec, entry.EmitGuardSec, answer.Segments, answer.RawResponse)));

                var record = new Dictionary<string, object?>
                {
                    ["index"] = k,
                    ["start_sec"] = Round(entry.OffsetSec),
                    ["duration_sec"] = Round(entry.DurationSec),
                    ["emit_guard_sec"] = Round(entry.EmitGuardSec),
                    ["segments"] = answer.Segments.Count,
                    ["timed"] = timed
                };
                if (answer.RawResponse.TryGetValue("usage", out var usage) && usage is IReadOnlyDictionary<string, object?> usageDict)
                {
                    usages.Add(usageDict);
                    record["usage"] = usageDict;
                }
                partRecords.Add(record);

                var note = timed
                    ? $"Auto split: part {k + 1}/{plan.Count} OK, {answer.Segments.Count} segments"
                    : $"Auto split: part {k + 1}/{plan.Count} came back without time marks";
                activity?.Invoke(note);
                _logger?.LogInformation(
                    "Chunk {ChunkIndex} auto split part {Part}/{Parts}: start={Start:F2}s duration={Duration:F2}s segments={Segments} timed={Timed}",
                    options.ChunkIndex, k + 1, plan.Count, entry.OffsetSec, entry.DurationSec, answer.Segments.Count, timed);
            }

            var merged = _merger.MergeTranscriptions(partResults, 0.0);
            var segments = merged.Segments
                .Select((s, i) =>
                {
                    var seg = new Dictionary<string, object?>
                    {
                        ["id"] = $"seg_{i}",
                        ["start"] = Round(s.Start),
                        ["end"] = Round(Math.Max(s.Start, s.End)),
                        ["text"] = s.Text
                    };
                    if (!string.IsNullOrEmpty(s.Speaker))
                        seg["speaker"] = s.Speaker;
                    return (object?)seg;
                })
                .ToArray();

            var raw = new Dictionary<string, object?>
            {
                ["task"] = "transcribe",
                ["duration"] = Round(chunkDurationSec),
                ["text"] = string.Join(" ", merged.Segments.Select(s => s.Text.Trim()).Where(t => t.Length > 0)),
                ["segments"] = segments
            };
            if (usages.Count > 0)
                raw["usage"] = SumUsage(usages);
            raw[RawBlockKey] = new Dictionary<string, object?>
            {
                ["reason"] = "server_error",
                ["status"] = cause.StatusCode,
                ["attempts"] = cause.Attempts,
                ["detail"] = cause.Detail,
                ["overlap_sec"] = Round(overlapSec),
                ["parts_without_time_marks"] = withoutTimeMarks,
                ["parts"] = partRecords.ToArray()
            };

            return new TranscriptionClientResult
            {
                RawResponse = raw,
                Segments = OpenAITranscriptionClient.ParseSegments(raw)
            };
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Auto split: could not remove the part files in {Dir}", workDir);
            }
        }
    }

    /// <summary>True when the answer has segments (time marks), false for a plain-text answer.</summary>
    internal static bool HasTimeMarks(IReadOnlyDictionary<string, object?> raw) =>
        raw.TryGetValue("segments", out var segments) && segments is object[];

    /// <summary>
    /// Sum of the parts' <c>usage</c> blocks: numbers are added (nested blocks too), the first text value is kept.
    /// </summary>
    internal static Dictionary<string, object?> SumUsage(IEnumerable<IReadOnlyDictionary<string, object?>> usages)
    {
        var total = new Dictionary<string, object?>();
        foreach (var usage in usages)
        {
            foreach (var (key, value) in usage)
            {
                switch (value)
                {
                    case IReadOnlyDictionary<string, object?> nested:
                        var soFar = total.TryGetValue(key, out var t) && t is Dictionary<string, object?> d
                            ? d
                            : new Dictionary<string, object?>();
                        total[key] = SumUsage(new IReadOnlyDictionary<string, object?>[] { soFar, nested });
                        break;
                    case double or int or long:
                        var add = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                        var had = total.TryGetValue(key, out var h) && h is double hd ? hd : 0.0;
                        total[key] = had + add;
                        break;
                    default:
                        if (!total.ContainsKey(key))
                            total[key] = value;
                        break;
                }
            }
        }

        return total;
    }

    private static double Round(double seconds) => Math.Round(seconds, 3, MidpointRounding.AwayFromZero);
}
