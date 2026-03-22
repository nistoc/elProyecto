using System.Text.Json;
using System.Text.Json.Serialization;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Persists per-chunk work state next to job artifacts for restart / agent05 hydration.
/// Schema v2 adds optional sub-chunk rows (<see cref="TranscriptionWorkStateChunk.IsSubChunk"/>).
/// </summary>
public static class TranscriptionWorkStateFile
{
    public const string DefaultFileName = "transcription_work_state.json";
    public const int SchemaVersionLatest = 2;

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

        byte[] utf8;
        await using (var ms = new MemoryStream())
        {
            await JsonSerializer.SerializeAsync(ms, doc, SerializerOptions, ct).ConfigureAwait(false);
            utf8 = ms.ToArray();
        }

        const int maxAttempts = 8;
        Exception? lastEx = null;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(tmp, utf8, ct).ConfigureAwait(false);
                if (!File.Exists(path))
                {
                    File.Move(tmp, path, overwrite: false);
                    return;
                }

                File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastEx = ex;
                TryDeleteQuiet(tmp);
                if (attempt < maxAttempts - 1)
                    await Task.Delay(40 * (attempt + 1), ct).ConfigureAwait(false);
            }
        }

        throw new IOException("Failed to save transcription work state after retries: " + path, lastEx);
    }

    private static void TryDeleteQuiet(string p)
    {
        try
        {
            if (File.Exists(p))
                File.Delete(p);
        }
        catch
        {
            /* best-effort */
        }
    }

    /// <summary>
    /// Removes the sub-chunk row matching <paramref name="parentChunkIndex"/> / <paramref name="subChunkIndex"/>.
    /// If the file is missing or no row matches, returns true (idempotent).
    /// </summary>
    public static async Task<bool> TryRemoveSubChunkRowAsync(
        string artifactRoot,
        int parentChunkIndex,
        int subChunkIndex,
        CancellationToken ct)
    {
        var path = ResolvePath(artifactRoot);
        if (!File.Exists(path))
            return true;

        var doc = await TryLoadAsync(artifactRoot, ct).ConfigureAwait(false);
        if (doc == null)
            return false;
        if (doc.Chunks is not { Count: > 0 })
            return true;

        var n = doc.Chunks.RemoveAll(c =>
            c.IsSubChunk
            && c.ParentChunkIndex == parentChunkIndex
            && c.SubChunkIndex == subChunkIndex);
        if (n == 0)
            return true;

        await SaveAsync(artifactRoot, doc, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Merge main-chunk row and write atomically.</summary>
    public static Task UpsertChunkAsync(
        string artifactRoot,
        int schemaVersion,
        int totalChunks,
        int chunkIndex,
        JobState state,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? error,
        bool recoveredFromArtifacts,
        CancellationToken ct) =>
        UpsertChunkRowAsync(
            artifactRoot,
            schemaVersion,
            totalChunks,
            chunkIndex,
            isSubChunk: false,
            parentChunkIndex: 0,
            subChunkIndex: 0,
            state,
            startedAt,
            completedAt,
            error,
            recoveredFromArtifacts,
            ct);

    /// <summary>Merge sub-chunk row (same <see cref="TranscriptionWorkStateChunk.Index"/> as parent chunk index).</summary>
    public static Task UpsertSubChunkAsync(
        string artifactRoot,
        int schemaVersion,
        int totalChunks,
        int parentChunkIndex,
        int subChunkIndex,
        JobState state,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        string? error,
        CancellationToken ct) =>
        UpsertChunkRowAsync(
            artifactRoot,
            schemaVersion,
            totalChunks,
            parentChunkIndex,
            isSubChunk: true,
            parentChunkIndex,
            subChunkIndex,
            state,
            startedAt,
            completedAt,
            error,
            recoveredFromArtifacts: false,
            ct);

    private static async Task UpsertChunkRowAsync(
        string artifactRoot,
        int schemaVersion,
        int totalChunks,
        int chunkIndex,
        bool isSubChunk,
        int parentChunkIndex,
        int subChunkIndex,
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
        doc.SchemaVersion = Math.Max(doc.SchemaVersion, schemaVersion);
        doc.TotalChunks = Math.Max(doc.TotalChunks, totalChunks);
        if (recoveredFromArtifacts)
            doc.RecoveredFromArtifacts = true;
        doc.Chunks ??= new List<TranscriptionWorkStateChunk>();
        var row = doc.Chunks.FirstOrDefault(c => RowKeyEquals(c, chunkIndex, isSubChunk, parentChunkIndex, subChunkIndex));
        if (row == null)
        {
            row = new TranscriptionWorkStateChunk
            {
                Index = chunkIndex,
                IsSubChunk = isSubChunk,
                ParentChunkIndex = parentChunkIndex,
                SubChunkIndex = subChunkIndex
            };
            doc.Chunks.Add(row);
        }

        row.State = state.ToString();
        row.StartedAt = startedAt?.ToString("O");
        row.CompletedAt = completedAt?.ToString("O");
        row.ErrorMessage = error;
        SortChunkRows(doc.Chunks);
        await SaveAsync(artifactRoot, doc, ct).ConfigureAwait(false);
    }

    private static bool RowKeyEquals(TranscriptionWorkStateChunk c, int chunkIndex, bool isSubChunk, int parentChunkIndex, int subChunkIndex)
    {
        if (!isSubChunk)
            return !c.IsSubChunk && c.Index == chunkIndex;
        return c.IsSubChunk && c.ParentChunkIndex == parentChunkIndex && c.SubChunkIndex == subChunkIndex;
    }

    private static void SortChunkRows(List<TranscriptionWorkStateChunk> chunks)
    {
        chunks.Sort((a, b) =>
        {
            var idxCmp = a.Index.CompareTo(b.Index);
            if (idxCmp != 0) return idxCmp;
            var subFlag = a.IsSubChunk.CompareTo(b.IsSubChunk);
            if (subFlag != 0) return subFlag;
            return a.SubChunkIndex.CompareTo(b.SubChunkIndex);
        });
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
