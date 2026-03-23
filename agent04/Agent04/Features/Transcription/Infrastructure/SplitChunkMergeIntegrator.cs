using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Merges operator sub-chunk results into <c>split_chunks/chunk_N/chunk_N_merged.md</c> (stable sub-order + in-file segment order),
/// and optionally rebuilds job-level <c>transcript.md</c> by stitching per-chunk markdown when pipeline + artifacts are provided.
/// Uses every existing <c>results/sub_chunk_XX_result.json</c> (at least one required), in index order.
/// </summary>
public static class SplitChunkMergeIntegrator
{
    private static readonly Regex SubResultFileName = new(
        @"^sub_chunk_(\d{2})_result\.json$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MergeLocksByArtifactAndParent =
        new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim MergeLock(string artifactRoot, int parentChunkIndex) =>
        MergeLocksByArtifactAndParent.GetOrAdd(
            artifactRoot + "|" + parentChunkIndex,
            static _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// Merges all existing <c>sub_chunk_XX_result.json</c> under <c>split_chunks/chunk_N/results</c> (at least one required).
    /// Uses the same per-(artifactRoot, parent) lock as <see cref="TryAfterSubChunkCompletedAsync"/>.
    /// </summary>
    /// <returns><c>(true, rebuild_split_merged_ok)</c> on success; otherwise a stable <c>split_merge_*</c> reason.</returns>
    public static async Task<(bool Ok, string Message)> TryRebuildSplitMergedForChunkAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int parentChunkIndex,
        ITranscriptionMerger merger,
        ILogger? logger,
        CancellationToken ct,
        ITranscriptionPipeline? pipeline = null,
        IProjectArtifactService? artifacts = null)
    {
        var splitRoot = Path.Combine(artifactRoot, config.SplitChunksDir);
        var chunkFolder = Path.Combine(splitRoot, $"chunk_{parentChunkIndex}");
        var resultsDir = Path.Combine(chunkFolder, "results");

        if (!Directory.Exists(resultsDir))
            return (false, "split_merge_no_split_layout");

        var sem = MergeLock(artifactRoot, parentChunkIndex);
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var indices = DiscoverExistingSubResultIndices(resultsDir);
            if (indices.Count == 0)
                return (false, "split_merge_no_sub_results");

            var pairs = new List<(int SubIdx, TranscriptionResult Result)>();
            foreach (var i in indices)
            {
                var path = Path.Combine(resultsDir, $"sub_chunk_{i:D2}_result.json");
                var tr = SubChunkResultReader.TryLoad(path);
                if (tr == null)
                    return (false, "split_merge_invalid_sub_result");
                pairs.Add((i, tr));
            }

            double parentOffset;
            try
            {
                parentOffset = await ResolveParentChunkOffsetAsync(
                        config,
                        artifactRoot,
                        agentJobId,
                        parentChunkIndex,
                        pipeline,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Split merge: could not resolve parent offset for chunk {Chunk}", parentChunkIndex);
                return (false, "split_merge_parent_offset_failed");
            }

            TranscriptionResult mergedAbs;
            try
            {
                mergedAbs = merger.MergeTranscriptions(pairs, parentOffset);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Split merge: merger failed for chunk {Chunk}", parentChunkIndex);
                return (false, "split_merge_merger_failed");
            }

            await SaveChunkMergedArtifactsAsync(chunkFolder, parentChunkIndex, mergedAbs, logger, ct).ConfigureAwait(false);

            await TryRebuildMainJobOutputsAsync(config, artifactRoot, agentJobId, logger, ct, pipeline, artifacts)
                .ConfigureAwait(false);

            return (true, "rebuild_split_merged_ok");
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>Called after a sub-chunk transcription finishes; attempts merge from whatever <c>sub_chunk_XX_result.json</c> files exist (no-op if merge fails).</summary>
    public static async Task TryAfterSubChunkCompletedAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int parentChunkIndex,
        int totalChunks,
        ITranscriptionMerger merger,
        ILogger? logger,
        CancellationToken ct,
        ITranscriptionPipeline? pipeline = null,
        IProjectArtifactService? artifacts = null)
    {
        _ = totalChunks;
        await TryRebuildSplitMergedForChunkAsync(
                config,
                artifactRoot,
                agentJobId,
                parentChunkIndex,
                merger,
                logger,
                ct,
                pipeline,
                artifacts)
            .ConfigureAwait(false);
    }

    /// <summary>Indices of <c>sub_chunk_XX_result.json</c> that exist under <paramref name="resultsDir"/>.</summary>
    private static List<int> DiscoverExistingSubResultIndices(string resultsDir)
    {
        var list = new List<int>();
        foreach (var path in Directory.EnumerateFiles(resultsDir))
        {
            var m = SubResultFileName.Match(Path.GetFileName(path));
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out var idx))
                continue;
            list.Add(idx);
        }

        list.Sort();
        return list;
    }

    private static async Task<double> ResolveParentChunkOffsetAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int parentChunkIndex,
        ITranscriptionPipeline? pipeline,
        CancellationToken ct)
    {
        if (pipeline == null)
            return 0;

        try
        {
            var chunkInfos = await pipeline.ResolveChunkInfosForRebuildAsync(config, artifactRoot, ct).ConfigureAwait(false);
            if (parentChunkIndex >= 0 && parentChunkIndex < chunkInfos.Count)
                return chunkInfos[parentChunkIndex].Offset;
        }
        catch
        {
            /* best-effort */
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

    private static Task SaveChunkMergedArtifactsAsync(
        string chunkFolder,
        int parentChunkIndex,
        TranscriptionResult mergedAbs,
        ILogger? logger,
        CancellationToken ct)
    {
        var mdPath = Path.Combine(chunkFolder, $"chunk_{parentChunkIndex}_merged.md");
        var writer = new TranscriptionOutputWriter();
        writer.InitializeMarkdown(mdPath);
        writer.AppendSegmentsToMarkdown(mdPath, mergedAbs.Segments, 0, 0);
        writer.FinalizeMarkdown(mdPath);
        logger?.LogInformation("Split merge: wrote {Md}", mdPath);
        return Task.CompletedTask;
    }

    private static async Task TryRebuildMainJobOutputsAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        ILogger? logger,
        CancellationToken ct,
        ITranscriptionPipeline? pipeline,
        IProjectArtifactService? artifacts)
    {
        var baseName = TryInferAudioBaseName(artifactRoot, config);
        if (string.IsNullOrEmpty(baseName))
        {
            logger?.LogDebug("Split merge: skip main outputs — could not infer base name");
            return;
        }

        if (pipeline == null || artifacts == null)
        {
            logger?.LogDebug("Split merge: skip main transcript stitch — pipeline or artifacts missing");
            return;
        }

        var mdRel = TranscriptionMdOutputPath.ResolveRelative(config.MdOutputPath, baseName, agentJobId);
        var mdPath = Path.Combine(artifactRoot, mdRel);
        await pipeline.EnsurePerChunkMarkdownFromJsonAsync(config, artifactRoot, null, ct).ConfigureAwait(false);
        var chunkInfos = await pipeline.ResolveChunkInfosForRebuildAsync(config, artifactRoot, ct).ConfigureAwait(false);
        artifacts.StitchChunkMarkdownFiles(config, artifactRoot, chunkInfos, mdPath);
        logger?.LogInformation("Split merge: rebuilt main {Md} via chunk markdown stitch", mdPath);
    }
}
