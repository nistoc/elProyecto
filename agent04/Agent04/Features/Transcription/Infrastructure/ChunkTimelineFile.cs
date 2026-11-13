using System.Text.Json;
using System.Text.Json.Serialization;
using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Where each main chunk sits on the timeline of the audio that was split:
/// <c>timeline/chunks.json</c> in the job folder, written right after chunking.
/// <para>
/// Per-chunk JSON keeps chunk-local times, so anything that turns it back into a transcript after the
/// first run (rebuild, retranscribe) needs the chunk's offset and emit guard. Without them a rebuilt
/// chunk transcript restarts from zero and repeats the overlap with the previous chunk.
/// </para>
/// </summary>
public static class ChunkTimelineFile
{
    public const string FolderName = "timeline";
    public const string FileName = "chunks.json";
    public const int SchemaVersionLatest = 1;

    public static string ResolvePath(string artifactRoot) =>
        Path.Combine(artifactRoot, FolderName, FileName);

    public static async Task SaveAsync(string artifactRoot, IReadOnlyList<ChunkInfo> chunks, CancellationToken ct)
    {
        var doc = new ChunkTimelineDocument
        {
            SchemaVersion = SchemaVersionLatest,
            Chunks = chunks
                .Select((c, i) => new ChunkTimelineEntry
                {
                    Index = i,
                    File = Path.GetFileName(c.Path),
                    OffsetSec = c.Offset,
                    EmitGuardSec = c.EmitGuard
                })
                .ToList()
        };

        var path = ResolvePath(artifactRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(doc, SerializerOptions), ct).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Null when the file is missing or unreadable (jobs transcribed before it existed).</summary>
    public static async Task<ChunkTimelineDocument?> TryLoadAsync(string artifactRoot, CancellationToken ct)
    {
        var path = ResolvePath(artifactRoot);
        if (!File.Exists(path))
            return null;
        try
        {
            await using var fs = File.OpenRead(path);
            var doc = await JsonSerializer.DeserializeAsync<ChunkTimelineDocument>(fs, SerializerOptions, ct).ConfigureAwait(false);
            return doc?.Chunks is { Count: > 0 } ? doc : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Entry for a chunk audio file: matched by file name first, then by index (a re-encoded chunk
    /// keeps its index but gets another file name).
    /// </summary>
    public static ChunkTimelineEntry? Find(ChunkTimelineDocument? doc, int index, string chunkAudioPath)
    {
        if (doc?.Chunks == null)
            return null;
        var name = Path.GetFileName(chunkAudioPath);
        return doc.Chunks.FirstOrDefault(c => string.Equals(c.File, name, StringComparison.OrdinalIgnoreCase))
               ?? doc.Chunks.FirstOrDefault(c => c.Index == index);
    }

    /// <summary>
    /// Offset and emit guard of chunk <paramref name="index"/> for a job that has no timeline file.
    /// Repeats the arithmetic of <see cref="AudioChunker.SliceWithOverlap"/>: every chunk is cut with the
    /// same whole-second window, so the window equals the first chunk's duration and each next chunk
    /// starts one stride (window minus overlap) later.
    /// </summary>
    public static (double OffsetSec, double EmitGuardSec) Derive(int index, double firstChunkDurationSec, double overlapSec)
    {
        if (index <= 0 || firstChunkDurationSec <= 0)
            return (0.0, 0.0);
        var window = Math.Max(1.0, Math.Round(firstChunkDurationSec));
        var overlap = Math.Max(0.0, Math.Min(overlapSec, Math.Max(0.0, window - 0.5)));
        var stride = Math.Max(1.0, window - overlap);
        return (index * stride, overlap);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}

public sealed class ChunkTimelineDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("chunks")]
    public List<ChunkTimelineEntry> Chunks { get; set; } = new();
}

public sealed class ChunkTimelineEntry
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>Chunk audio file name under the split folder (e.g. <c>lesson_part_003.wav</c>).</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    /// <summary>Start of the chunk on the timeline of the audio that was split, in seconds.</summary>
    [JsonPropertyName("offset_sec")]
    public double OffsetSec { get; set; }

    /// <summary>Leading seconds that repeat the previous chunk; segments starting inside are not emitted.</summary>
    [JsonPropertyName("emit_guard_sec")]
    public double EmitGuardSec { get; set; }
}
