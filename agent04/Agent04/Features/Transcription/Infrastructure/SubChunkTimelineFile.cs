using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Where each operator sub-chunk sits inside its parent chunk:
/// <c>split_chunks/chunk_N/sub_chunks_timeline.json</c>, written when the chunk is split.
/// <para>
/// A sub-chunk is transcribed on its own, so its segments count seconds from the start of that
/// sub-chunk. The merge needs the start of every sub-chunk inside the parent; without it all parts
/// land at the start of the parent and their lines interleave.
/// </para>
/// </summary>
public static class SubChunkTimelineFile
{
    public const string FileName = "sub_chunks_timeline.json";
    public const int SchemaVersionLatest = 1;

    public static string ResolvePath(string chunkFolder) => Path.Combine(chunkFolder, FileName);

    /// <summary>Timeline of a split made by <see cref="OperatorChunkSplitPlanner"/>.</summary>
    public static SubChunkTimelineDocument FromPlan(
        int parentChunkIndex,
        double parentDurationSec,
        IReadOnlyList<OperatorChunkSplitPlanner.Segment> plan,
        IReadOnlyList<string> subChunkFileNames)
    {
        var doc = new SubChunkTimelineDocument
        {
            SchemaVersion = SchemaVersionLatest,
            ParentChunkIndex = parentChunkIndex,
            ParentDurationSec = parentDurationSec
        };
        for (var k = 0; k < plan.Count; k++)
        {
            var previousEnd = k > 0 ? plan[k - 1].StartSec + plan[k - 1].DurationSec : 0.0;
            doc.SubChunks.Add(new SubChunkTimelineEntry
            {
                Index = k,
                File = k < subChunkFileNames.Count ? subChunkFileNames[k] : "",
                OffsetSec = plan[k].StartSec,
                DurationSec = plan[k].DurationSec,
                EmitGuardSec = k > 0 ? Math.Max(0.0, previousEnd - plan[k].StartSec) : 0.0
            });
        }

        return doc;
    }

    /// <summary>
    /// Timeline of a split that has no file (made before the file existed), from what the audio says.
    /// Inverts <see cref="OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap"/>: all parts but the last
    /// have the duration of the first one, and each next part starts one stride later, where the strides
    /// spread the parts evenly over the parent.
    /// </summary>
    public static SubChunkTimelineDocument? Derive(
        int parentChunkIndex,
        double parentDurationSec,
        double firstSubChunkDurationSec,
        int subChunkCount)
    {
        if (subChunkCount < 2 || parentDurationSec <= 0 || firstSubChunkDurationSec <= 0
            || firstSubChunkDurationSec > parentDurationSec + 0.5)
            return null;

        var segment = Math.Min(firstSubChunkDurationSec, parentDurationSec);
        var stride = (parentDurationSec - segment) / (subChunkCount - 1);
        var doc = new SubChunkTimelineDocument
        {
            SchemaVersion = SchemaVersionLatest,
            ParentChunkIndex = parentChunkIndex,
            ParentDurationSec = parentDurationSec,
            Derived = true
        };
        for (var k = 0; k < subChunkCount; k++)
        {
            var start = k * stride;
            doc.SubChunks.Add(new SubChunkTimelineEntry
            {
                Index = k,
                OffsetSec = start,
                DurationSec = k == subChunkCount - 1 ? parentDurationSec - start : segment,
                EmitGuardSec = k > 0 ? Math.Max(0.0, segment - stride) : 0.0
            });
        }

        return doc;
    }

    public static async Task SaveAsync(string chunkFolder, SubChunkTimelineDocument doc, CancellationToken ct)
    {
        Directory.CreateDirectory(chunkFolder);
        var path = ResolvePath(chunkFolder);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(doc, SerializerOptions), ct).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Null when the file is missing or unreadable (splits made before it existed).</summary>
    public static async Task<SubChunkTimelineDocument?> TryLoadAsync(string chunkFolder, CancellationToken ct)
    {
        var path = ResolvePath(chunkFolder);
        if (!File.Exists(path))
            return null;
        try
        {
            await using var fs = File.OpenRead(path);
            var doc = await JsonSerializer.DeserializeAsync<SubChunkTimelineDocument>(fs, SerializerOptions, ct).ConfigureAwait(false);
            return doc?.SubChunks is { Count: > 0 } ? doc : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SubChunkTimelineEntry? Find(SubChunkTimelineDocument? doc, int subChunkIndex) =>
        doc?.SubChunks.FirstOrDefault(s => s.Index == subChunkIndex);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}

public sealed class SubChunkTimelineDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("parent_chunk_index")]
    public int ParentChunkIndex { get; set; }

    [JsonPropertyName("parent_duration_sec")]
    public double ParentDurationSec { get; set; }

    /// <summary>True when the offsets were worked out from audio durations instead of saved at split time.</summary>
    [JsonPropertyName("derived")]
    public bool Derived { get; set; }

    [JsonPropertyName("sub_chunks")]
    public List<SubChunkTimelineEntry> SubChunks { get; set; } = new();
}

public sealed class SubChunkTimelineEntry
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>Sub-chunk audio file name under <c>sub_chunks/</c>; empty in a derived timeline.</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    /// <summary>Start of the sub-chunk inside the parent chunk, in seconds.</summary>
    [JsonPropertyName("offset_sec")]
    public double OffsetSec { get; set; }

    [JsonPropertyName("duration_sec")]
    public double DurationSec { get; set; }

    /// <summary>Leading seconds that repeat the previous sub-chunk.</summary>
    [JsonPropertyName("emit_guard_sec")]
    public double EmitGuardSec { get; set; }
}
