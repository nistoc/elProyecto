using System.Text.Json;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Reads <c>pending_chunks.json</c> written by EnqueueTranscriptionWork; consumed (deleted) after successful read.
/// </summary>
public static class PendingChunksReader
{
    public const string FileName = "pending_chunks.json";

    public static async Task<HashSet<int>?> TryLoadAndConsumeAsync(string artifactRoot, CancellationToken ct)
    {
        var path = Path.Combine(artifactRoot, FileName);
        if (!File.Exists(path))
            return null;

        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("chunk_indices", out var arr))
            return null;
        var set = new HashSet<int>();
        foreach (var e in arr.EnumerateArray())
        {
            if (e.TryGetInt32(out var v))
                set.Add(v);
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            /* best-effort consume */
        }

        return set.Count > 0 ? set : null;
    }
}
