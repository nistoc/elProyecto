using System.Collections.Generic;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Builds the job-level transcript by concatenating per-chunk markdown files and/or
/// <c>split_chunks/chunk_N/chunk_N_merged.md</c> (operator split) in chunk index order.
/// </summary>
public static class ChunkMarkdownStitcher
{
    /// <summary>
    /// Strips optional agent01-style <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> / <c>&lt;&lt;&lt;&lt;&lt;</c> wrappers; otherwise returns trimmed content.
    /// </summary>
    public static string ExtractTranscriptBodyLines(string fileContent)
    {
        if (string.IsNullOrEmpty(fileContent))
            return "";
        var lines = fileContent.Replace("\r\n", "\n").Split('\n');
        var start = 0;
        var end = lines.Length;
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.Equals(lines[i].Trim(), ">>>>>>>", StringComparison.Ordinal))
            {
                start = i + 1;
                break;
            }
        }

        for (var i = start; i < lines.Length; i++)
        {
            if (string.Equals(lines[i].Trim(), "<<<<<", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        if (start == 0 && end == lines.Length)
            return fileContent.Trim();

        var inner = new List<string>();
        for (var i = start; i < end; i++)
            inner.Add(lines[i]);
        return string.Join("\n", inner).TrimEnd() + "\n";
    }

    /// <summary>
    /// For each chunk index: prefer <c>chunk_i_merged.md</c> under split layout; else use
    /// <c>{per_chunk_md_dir}/{chunk_basename}.md</c>. Missing sources are skipped with a warning.
    /// </summary>
    public static void StitchFinalMarkdown(
        TranscriptionConfig config,
        string artifactRoot,
        IReadOnlyList<ChunkInfo> chunkInfos,
        string finalMdPath,
        ITranscriptionOutputWriter writer,
        ILogger? logger)
    {
        var perChunkMdDir = config.Get<string>("per_chunk_md_dir") ?? "chunks_md";
        var splitDir = config.SplitChunksDir;

        writer.ResetSpeakerMap();
        writer.InitializeMarkdown(finalMdPath);

        for (var i = 0; i < chunkInfos.Count; i++)
        {
            var mergedPath = Path.Combine(artifactRoot, splitDir, $"chunk_{i}", $"chunk_{i}_merged.md");
            string? text = null;
            if (File.Exists(mergedPath))
            {
                text = File.ReadAllText(mergedPath);
            }
            else
            {
                var basename = Path.GetFileNameWithoutExtension(chunkInfos[i].Path);
                var chunkMd = Path.Combine(artifactRoot, perChunkMdDir, basename + ".md");
                if (File.Exists(chunkMd))
                    text = File.ReadAllText(chunkMd);
            }

            if (string.IsNullOrEmpty(text))
            {
                logger?.LogWarning(
                    "stitch: skipping chunk {Index} — no merged md at {Merged} nor per-chunk md for {Base}",
                    i,
                    mergedPath,
                    Path.GetFileNameWithoutExtension(chunkInfos[i].Path));
                continue;
            }

            var body = ExtractTranscriptBodyLines(text);
            if (string.IsNullOrWhiteSpace(body))
                continue;

            using var fs = new FileStream(finalMdPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var w = new StreamWriter(fs);
            w.Write(body);
            if (!body.EndsWith('\n'))
                w.WriteLine();
        }

        writer.FinalizeMarkdown(finalMdPath);
    }
}
