using System.Security.Cryptography;
using System.Text.Json;
using Agent04.Features.Transcription.Application;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class TranscriptionCache : ITranscriptionCache
{
    private readonly string _cacheDir;
    private readonly ILogger<TranscriptionCache>? _logger;

    public TranscriptionCache(string cacheDir, ILogger<TranscriptionCache>? logger = null)
    {
        _cacheDir = cacheDir;
        _logger = logger;
        Directory.CreateDirectory(_cacheDir);
    }

    public string GetManifestPath(string baseName)
    {
        var name = Path.GetFileNameWithoutExtension(baseName);
        if (string.IsNullOrEmpty(name))
            name = baseName;
        return Path.Combine(_cacheDir, name + ".manifest.json");
    }

    public async Task<TranscriptionManifest> LoadManifestAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(manifestPath))
            return new TranscriptionManifest();
        try
        {
            await using var fs = File.OpenRead(manifestPath);
            var doc = await JsonSerializer.DeserializeAsync<JsonElement>(fs, cancellationToken: cancellationToken);
            var manifest = new TranscriptionManifest();
            if (doc.TryGetProperty("chunks", out var chunks) && chunks.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in chunks.EnumerateObject())
                {
                    var entry = new ChunkCacheEntry
                    {
                        Fingerprint = prop.Value.TryGetProperty("fingerprint", out var fp) ? fp.GetString() ?? "" : "",
                        Response = prop.Value.TryGetProperty("response", out var resp) ? JsonSerializer.Deserialize<object>(resp.GetRawText()) : null
                    };
                    manifest.Chunks[prop.Name] = entry;
                }
            }
            return manifest;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load manifest {Path}", manifestPath);
            return new TranscriptionManifest();
        }
    }

    public async Task SaveManifestAsync(string manifestPath, TranscriptionManifest manifest, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var dto = new Dictionary<string, object>();
        var chunks = new Dictionary<string, object>();
        foreach (var (k, v) in manifest.Chunks)
            chunks[k] = new { fingerprint = v.Fingerprint, response = v.Response };
        dto["chunks"] = chunks;
        await using var fs = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(fs, dto, TranscriptionJsonSerializerOptions.Indented, cancellationToken);
    }

    public async Task<string> GetFileFingerprintAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var sha = SHA256.Create();
        await using var fs = File.OpenRead(filePath);
        var hash = await sha.ComputeHashAsync(fs, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public object? GetCachedResponse(TranscriptionManifest manifest, string chunkBasename, string fingerprint)
    {
        var name = Path.GetFileName(chunkBasename);
        if (!manifest.Chunks.TryGetValue(name, out var entry) || entry.Fingerprint != fingerprint)
            return null;
        return entry.Response;
    }

    public async Task CacheResponseAsync(string manifestPath, TranscriptionManifest manifest, string chunkBasename, string fingerprint, object response, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(chunkBasename);
        manifest.Chunks[name] = new ChunkCacheEntry { Fingerprint = fingerprint, Response = response };
        await SaveManifestAsync(manifestPath, manifest, cancellationToken);
    }
}
