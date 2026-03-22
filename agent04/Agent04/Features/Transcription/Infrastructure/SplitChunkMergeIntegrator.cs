using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// When all operator sub-chunks for <c>chunk_N</c> have results, merges them (overlap dedupe),
/// writes <c>split_chunks/chunk_N/chunk_N_merged.*</c>, and rebuilds job-level <c>transcript.md</c> / combined JSON if artifacts exist.
/// </summary>
public static class SplitChunkMergeIntegrator
{
    private static readonly Regex SubAudioIndex = new(
        @"_sub_(\d{2})(?:\.[^.]+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MergeLocksByArtifactAndParent =
        new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim MergeLock(string artifactRoot, int parentChunkIndex) =>
        MergeLocksByArtifactAndParent.GetOrAdd(
            artifactRoot + "|" + parentChunkIndex,
            static _ => new SemaphoreSlim(1, 1));

    public static async Task TryAfterSubChunkCompletedAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int parentChunkIndex,
        int totalChunks,
        ITranscriptionMerger merger,
        ILogger? logger,
        CancellationToken ct)
    {
        var splitRoot = Path.Combine(artifactRoot, config.SplitChunksDir);
        var chunkFolder = Path.Combine(splitRoot, $"chunk_{parentChunkIndex}");
        var subChunksDir = Path.Combine(chunkFolder, "sub_chunks");
        var resultsDir = Path.Combine(chunkFolder, "results");
        if (!Directory.Exists(subChunksDir) || !Directory.Exists(resultsDir))
            return;

        var sem = MergeLock(artifactRoot, parentChunkIndex);
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var expectedSubs = CountExpectedSubChunks(subChunksDir);
            if (expectedSubs <= 0)
                return;

            var pairs = new List<(int SubIdx, TranscriptionResult Result)>();
            for (var i = 0; i < expectedSubs; i++)
            {
                var path = Path.Combine(resultsDir, $"sub_chunk_{i:D2}_result.json");
                if (!File.Exists(path))
                    return;
                var tr = SubChunkResultReader.TryLoad(path);
                if (tr == null)
                    return;
                pairs.Add((i, tr));
            }

            double parentOffset;
            try
            {
                parentOffset = await ResolveParentChunkOffsetAsync(config, artifactRoot, agentJobId, parentChunkIndex, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Split merge: could not resolve parent offset for chunk {Chunk}", parentChunkIndex);
                return;
            }

            TranscriptionResult mergedAbs;
            try
            {
                mergedAbs = merger.MergeTranscriptions(pairs, parentOffset);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Split merge: merger failed for chunk {Chunk}", parentChunkIndex);
                return;
            }

            await SaveChunkMergedArtifactsAsync(chunkFolder, parentChunkIndex, mergedAbs, logger, ct).ConfigureAwait(false);

            await TryRebuildMainJobOutputsAsync(
                    config,
                    artifactRoot,
                    agentJobId,
                    parentChunkIndex,
                    mergedAbs,
                    parentOffset,
                    logger,
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            sem.Release();
        }
    }

    private static int CountExpectedSubChunks(string subChunksDir)
    {
        var max = -1;
        foreach (var path in Directory.EnumerateFiles(subChunksDir))
        {
            var m = SubAudioIndex.Match(Path.GetFileName(path));
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out var idx))
                continue;
            if (idx > max) max = idx;
        }

        return max < 0 ? 0 : max + 1;
    }

    private static async Task<double> ResolveParentChunkOffsetAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int parentChunkIndex,
        CancellationToken ct)
    {
        var baseName = TryInferAudioBaseName(artifactRoot, config);
        if (string.IsNullOrEmpty(baseName))
            return 0;

        var jsonRel = ResolveOutputRel(config.RawJsonOutputPath, baseName, agentJobId);
        var jsonPath = Path.Combine(artifactRoot, jsonRel);
        if (!File.Exists(jsonPath))
            return 0;

        await using var fs = File.OpenRead(jsonPath);
        var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("chunks", out var chunksEl) ||
            chunksEl.ValueKind != JsonValueKind.Array)
            return 0;

        var idx = 0;
        foreach (var el in chunksEl.EnumerateArray())
        {
            if (idx == parentChunkIndex)
            {
                if (el.TryGetProperty("offset", out var o) && o.TryGetDouble(out var off))
                    return off;
                return 0;
            }

            idx++;
        }

        return 0;
    }

    private static string? TryInferAudioBaseName(string artifactRoot, TranscriptionConfig config)
    {
        var chunksDir = Path.Combine(artifactRoot, config.SplitWorkdir);
        if (!Directory.Exists(chunksDir))
            return null;
        var partRe = new Regex(@"^(.+)_part_\d+\.(m4a|wav|mp3|flac|ogg)$", RegexOptions.IgnoreCase);
        foreach (var path in Directory.EnumerateFiles(chunksDir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var m = partRe.Match(Path.GetFileName(path));
            if (m.Success)
                return m.Groups[1].Value;
        }

        var first = Directory.EnumerateFiles(chunksDir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        return first != null ? Path.GetFileNameWithoutExtension(first) : null;
    }

    private static string ResolveOutputRel(string pattern, string baseName, string? jobId)
    {
        var s = pattern.Replace("{base}", baseName, StringComparison.OrdinalIgnoreCase)
            .Replace("{jobId}", jobId ?? "", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(jobId) && !pattern.Contains("{jobId}", StringComparison.OrdinalIgnoreCase))
            s = Path.Combine(Path.GetDirectoryName(s) ?? ".", Path.GetFileNameWithoutExtension(s) + "_" + jobId + Path.GetExtension(s));
        return s;
    }

    private static async Task SaveChunkMergedArtifactsAsync(
        string chunkFolder,
        int parentChunkIndex,
        TranscriptionResult mergedAbs,
        ILogger? logger,
        CancellationToken ct)
    {
        var baseName = $"chunk_{parentChunkIndex}_merged";
        var jsonPath = Path.Combine(chunkFolder, $"{baseName}.json");
        var mdPath = Path.Combine(chunkFolder, $"{baseName}.md");

        var segDtos = mergedAbs.Segments.Select(s => new
        {
            start = s.Start,
            end = s.End,
            text = s.Text,
            speaker = s.Speaker
        }).ToList();
        var jsonObj = new
        {
            chunk_basename = mergedAbs.ChunkBasename,
            offset = mergedAbs.Offset,
            emit_guard = mergedAbs.EmitGuard,
            segments = segDtos
        };
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(jsonObj, TranscriptionJsonSerializerOptions.Indented), ct)
            .ConfigureAwait(false);

        var writer = new TranscriptionOutputWriter();
        writer.InitializeMarkdown(mdPath);
        writer.AppendSegmentsToMarkdown(mdPath, mergedAbs.Segments, 0, 0);
        writer.FinalizeMarkdown(mdPath);
        logger?.LogInformation("Split merge: wrote {Json} and {Md}", jsonPath, mdPath);
    }

    private static async Task TryRebuildMainJobOutputsAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int parentChunkIndex,
        TranscriptionResult mergedAbs,
        double parentOffset,
        ILogger? logger,
        CancellationToken ct)
    {
        var baseName = TryInferAudioBaseName(artifactRoot, config);
        if (string.IsNullOrEmpty(baseName))
        {
            logger?.LogDebug("Split merge: skip main outputs — could not infer base name");
            return;
        }

        var jsonRel = ResolveOutputRel(config.RawJsonOutputPath, baseName, agentJobId);
        var mdRel = ResolveOutputRel(config.MdOutputPath, baseName, agentJobId);
        var combinedJsonPath = Path.Combine(artifactRoot, jsonRel);
        var mdPath = Path.Combine(artifactRoot, mdRel);
        if (!File.Exists(combinedJsonPath))
        {
            logger?.LogDebug("Split merge: no combined JSON at {Path}; skip main integration", combinedJsonPath);
            return;
        }

        // Read fully then close before overwriting the same path (Windows blocks WriteAllText while OpenRead is active).
        var jsonBytes = await File.ReadAllBytesAsync(combinedJsonPath, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(jsonBytes);
        if (!doc.RootElement.TryGetProperty("chunks", out var chunksEl) ||
            chunksEl.ValueKind != JsonValueKind.Array)
            return;

        var entries = chunksEl.EnumerateArray().ToList();
        if (parentChunkIndex < 0 || parentChunkIndex >= entries.Count)
        {
            logger?.LogWarning(
                "Split merge: parent index {Parent} out of range (chunks={Count})",
                parentChunkIndex,
                entries.Count);
            return;
        }

        var rebuilt = new List<TranscriptionResult>();
        var idx = 0;
        foreach (var el in entries)
        {
            if (idx == parentChunkIndex)
            {
                var relSegs = mergedAbs.Segments
                    .Select(s => new ASRSegment(
                        s.Start - parentOffset,
                        s.End - parentOffset,
                        s.Text,
                        s.Speaker))
                    .ToList();
                var mergedRaw = BuildSyntheticRawFromSegments(mergedAbs.Segments);
                rebuilt.Add(new TranscriptionResult(mergedAbs.ChunkBasename, parentOffset, 0, relSegs, mergedRaw));
            }
            else
            {
                var chunkName = el.TryGetProperty("chunk", out var cEl) ? cEl.GetString() ?? "" : "";
                var offset = el.TryGetProperty("offset", out var oEl) && oEl.TryGetDouble(out var o) ? o : 0.0;
                var guard = el.TryGetProperty("emit_guard", out var gEl) && gEl.TryGetDouble(out var g) ? g : 0.0;
                if (!el.TryGetProperty("response", out var respEl))
                {
                    logger?.LogWarning("Split merge: chunk {Idx} missing response; abort main rebuild", idx);
                    return;
                }

                var dict = JsonElementToObjectDict(respEl);
                var segments = OpenAITranscriptionClient.ParseSegments(dict);
                rebuilt.Add(new TranscriptionResult(chunkName, offset, guard, segments, dict));
            }

            idx++;
        }

        var outWriter = new TranscriptionOutputWriter();
        outWriter.InitializeMarkdown(mdPath);
        foreach (var r in rebuilt)
            outWriter.AppendSegmentsToMarkdown(mdPath, r.Segments, r.Offset, r.EmitGuard);
        outWriter.FinalizeMarkdown(mdPath);
        outWriter.SaveCombinedJson(combinedJsonPath, rebuilt);
        logger?.LogInformation("Split merge: rebuilt main {Md} and {Json}", mdPath, combinedJsonPath);
    }

    private static Dictionary<string, object?> BuildSyntheticRawFromSegments(IReadOnlyList<ASRSegment> absoluteSegments)
    {
        var arr = absoluteSegments.Select(s => new Dictionary<string, object?>
        {
            ["start"] = s.Start,
            ["end"] = s.End,
            ["text"] = s.Text,
            ["speaker"] = s.Speaker ?? ""
        }).ToArray();
        return new Dictionary<string, object?> { ["segments"] = arr };
    }

    private static Dictionary<string, object?> JsonElementToObjectDict(JsonElement el)
    {
        var d = new Dictionary<string, object?>();
        if (el.ValueKind != JsonValueKind.Object) return d;
        foreach (var p in el.EnumerateObject())
            d[p.Name] = JsonToObject(p.Value);
        return d;
    }

    private static object? JsonToObject(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetDouble(out var d) ? d : e.GetInt32(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => e.EnumerateArray().Select(JsonToObject).ToArray(),
            JsonValueKind.Object => JsonElementToObjectDict(e),
            _ => null
        };
}
