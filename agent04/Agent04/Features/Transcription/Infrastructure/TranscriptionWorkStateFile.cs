using System.Text.Json;
using System.Text.Json.Serialization;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Persists per-chunk work state next to job artifacts for restart / agent05 hydration.
/// </summary>
public static class TranscriptionWorkStateFile
{
    public const string DefaultFileName = "transcription_work_state.json";

    public static string ResolvePath(string artifactRoot) =>
        Path.Combine(artifactRoot, DefaultFileName);

    public static async Task<TranscriptionWorkStateDocument?> TryLoadAsync(string artifactRoot, CancellationToken ct)
    {
        var path = ResolvePath(artifactRoot);
        if (!File.Exists(path)) return null;
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<TranscriptionWorkStateDocument>(fs, SerializerOptions, ct).ConfigureAwait(false);
    }

    public static async Task SaveAsync(string artifactRoot, TranscriptionWorkStateDocument doc, CancellationToken ct)
    {
        var path = ResolvePath(artifactRoot);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, doc, SerializerOptions, ct).ConfigureAwait(false);
        }

        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Merge chunk row and write atomically.</summary>
    public static async Task UpsertChunkAsync(
        string artifactRoot,
        int schemaVersion,
        int totalChunks,
        int chunkIndex,
        JobState state,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? error,
        bool recoveredFromArtifacts,
        CancellationToken ct)
    {
        var doc = await TryLoadAsync(artifactRoot, ct).ConfigureAwait(false) ?? new TranscriptionWorkStateDocument
        {
            SchemaVersion = schemaVersion,
            TotalChunks = totalChunks,
            RecoveredFromArtifacts = recoveredFromArtifacts
        };
        doc.SchemaVersion = schemaVersion;
        doc.TotalChunks = totalChunks;
        doc.Chunks ??= new List<TranscriptionWorkStateChunk>();
        var row = doc.Chunks.FirstOrDefault(c => c.Index == chunkIndex);
        if (row == null)
        {
            row = new TranscriptionWorkStateChunk { Index = chunkIndex };
            doc.Chunks.Add(row);
        }

        row.State = state.ToString();
        row.StartedAt = startedAt?.ToString("O");
        row.CompletedAt = completedAt?.ToString("O");
        row.ErrorMessage = error;
        doc.Chunks.Sort((a, b) => a.Index.CompareTo(b.Index));
        await SaveAsync(artifactRoot, doc, ct).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class TranscriptionWorkStateDocument
{
    public int SchemaVersion { get; set; } = 1;
    public int TotalChunks { get; set; }
    public bool RecoveredFromArtifacts { get; set; }
    public List<TranscriptionWorkStateChunk>? Chunks { get; set; }
}

public sealed class TranscriptionWorkStateChunk
{
    public int Index { get; set; }
    public string State { get; set; } = "";
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
