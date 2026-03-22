using System.Text.Json;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// When <see cref="TranscriptionWorkStateFile"/> is missing, infer initial state from legacy on-disk artifacts (§1a).
/// </summary>
public static class TranscriptionWorkStateBootstrapper
{
    /// <summary>
    /// If no state file exists, try to build one from <c>chunks/</c> + <c>chunks_json/</c> (+ optional cache manifest).
    /// Returns null if nothing to infer.
    /// </summary>
    public static async Task<TranscriptionWorkStateDocument?> TryBootstrapAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string baseName,
        IProjectArtifactService artifacts,
        CancellationToken ct)
    {
        var existingPath = TranscriptionWorkStateFile.ResolvePath(artifactRoot);
        if (File.Exists(existingPath))
            return null;

        var chunksDir = Path.Combine(artifactRoot, "chunks");
        var perChunkRel = config.Get<string>("per_chunk_json_dir") ?? "chunks_json";
        var jsonDir = Path.Combine(artifactRoot, perChunkRel);

        if (!Directory.Exists(chunksDir))
            return await TrySingleFileOrManifestOnlyAsync(config, artifactRoot, baseName, ct).ConfigureAwait(false);

        var wavFiles = Directory.GetFiles(chunksDir)
            .Where(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path: f, Name: Path.GetFileName(f)))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (wavFiles.Count == 0)
            return await TrySingleFileOrManifestOnlyAsync(config, artifactRoot, baseName, ct).ConfigureAwait(false);

        var indexToStem = new Dictionary<int, string>();
        foreach (var w in wavFiles)
        {
            var m = TranscriptionChunkOnDiskReader.PartIndexInFileName.Match(w.Name);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups[1].Value, out var idx)) continue;
            indexToStem[idx] = Path.GetFileNameWithoutExtension(w.Name);
        }

        if (indexToStem.Count == 0)
            return null;

        var maxIdx = indexToStem.Keys.Max();
        var total = maxIdx + 1;
        var chunks = new List<TranscriptionWorkStateChunk>();
        Directory.CreateDirectory(artifactRoot);

        for (var i = 0; i < total; i++)
        {
            JobState state;
            string? completedAt = null;
            if (!indexToStem.TryGetValue(i, out var stem))
            {
                state = JobState.Pending;
            }
            else
            {
                var jsonName = stem + ".json";
                var jsonPath = Path.Combine(jsonDir, jsonName);
                if (File.Exists(jsonPath) && new FileInfo(jsonPath).Length > 0)
                {
                    state = JobState.Completed;
                    if (!await LooksLikeTranscriptionJsonAsync(jsonPath, ct).ConfigureAwait(false))
                        state = JobState.Pending;
                    else
                        completedAt = File.GetLastWriteTimeUtc(jsonPath).ToString("O");
                }
                else
                    state = JobState.Pending;
            }

            chunks.Add(new TranscriptionWorkStateChunk
            {
                Index = i,
                State = state.ToString(),
                CompletedAt = completedAt
            });
        }

        var doc = new TranscriptionWorkStateDocument
        {
            SchemaVersion = 1,
            TotalChunks = total,
            RecoveredFromArtifacts = true,
            Chunks = chunks
        };
        await artifacts.SaveWorkStateAsync(artifactRoot, doc, ct).ConfigureAwait(false);
        return doc;
    }

    private static async Task<bool> LooksLikeTranscriptionJsonAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var fs = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && (root.TryGetProperty("segments", out _) || root.TryGetProperty("text", out _) || root.TryGetProperty("task", out _));
        }
        catch
        {
            return false;
        }
    }

    private static Task<TranscriptionWorkStateDocument?> TrySingleFileOrManifestOnlyAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string baseName,
        CancellationToken ct)
    {
        _ = config;
        _ = artifactRoot;
        _ = baseName;
        _ = ct;
        return Task.FromResult<TranscriptionWorkStateDocument?>(null);
    }
}
