namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Cache for transcription results by file fingerprint (manifest + SHA256).
/// </summary>
public interface ITranscriptionCache
{
    string GetManifestPath(string baseName);

    /// <summary>Load manifest from file or return empty structure (chunks: {}).</summary>
    Task<TranscriptionManifest> LoadManifestAsync(string manifestPath, CancellationToken cancellationToken = default);

    Task SaveManifestAsync(string manifestPath, TranscriptionManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>Compute SHA256 fingerprint of file.</summary>
    Task<string> GetFileFingerprintAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Get cached API response for chunk if fingerprint matches; otherwise null.</summary>
    object? GetCachedResponse(TranscriptionManifest manifest, string chunkBasename, string fingerprint);

    /// <summary>Store response in manifest and persist.</summary>
    Task CacheResponseAsync(string manifestPath, TranscriptionManifest manifest, string chunkBasename, string fingerprint, object response, CancellationToken cancellationToken = default);
}

/// <summary>Manifest structure: chunks keyed by chunk basename, each with fingerprint and response.</summary>
public sealed class TranscriptionManifest
{
    public Dictionary<string, ChunkCacheEntry> Chunks { get; set; } = new();
}

public sealed class ChunkCacheEntry
{
    public string Fingerprint { get; set; } = "";
    public object? Response { get; set; }
}
