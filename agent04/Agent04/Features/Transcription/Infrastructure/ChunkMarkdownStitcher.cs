using Agent04.Features.Transcription.Application;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Builds the job-level transcript by concatenating per-chunk markdown files (<c>chunks_md/&lt;basename&gt;.md</c>)
/// and/or <c>split_chunks/chunk_N/chunk_N_merged.md</c> (operator split) in chunk index order.
/// Ported from the 2026.03.23-run-refiner branch; the per-chunk .md files are the source of the combined transcript.
/// </summary>
public static class ChunkMarkdownStitcher
{
    /// <summary>Strips optional <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> / <c>&lt;&lt;&lt;&lt;&lt;</c> wrappers; otherwise returns trimmed content.</summary>
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
    /// For each chunk index: prefer <c>split_chunks/chunk_i/chunk_i_merged.md</c> (operator split); else use
    /// <c>{perChunkMdDir}/{basename}.md</c>. Missing sources are skipped with a warning (e.g. a failed chunk).
    /// </summary>
    public static void Stitch(
        string artifactRoot,
        IReadOnlyList<string> chunkBasenames,
        string perChunkMdDir,
        string splitChunksDir,
        string finalMdPath,
        ITranscriptionOutputWriter writer,
        ILogger? logger)
    {
        writer.ResetSpeakerMap();
        writer.InitializeMarkdown(finalMdPath);

        for (var i = 0; i < chunkBasenames.Count; i++)
        {
            var mergedPath = Path.Combine(artifactRoot, splitChunksDir, $"chunk_{i}", $"chunk_{i}_merged.md");
            string? text = null;
            if (File.Exists(mergedPath))
            {
                text = File.ReadAllText(mergedPath);
            }
            else
            {
                var chunkMd = Path.Combine(artifactRoot, perChunkMdDir, chunkBasenames[i] + ".md");
                if (File.Exists(chunkMd))
                    text = File.ReadAllText(chunkMd);
            }

            if (string.IsNullOrEmpty(text))
            {
                logger?.LogWarning(
                    "stitch: skipping chunk {Index} — no merged md nor per-chunk md for {Base}",
                    i,
                    chunkBasenames[i]);
                continue;
            }

            var body = ExtractTranscriptBodyLines(text);
            if (string.IsNullOrWhiteSpace(body))
                continue;

            using var fs = new FileStream(finalMdPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var w = new StreamWriter(fs);
            w.Write(body);
            if (!body.EndsWith('\n'))
                w.Write('\n');
        }

        writer.FinalizeMarkdown(finalMdPath);
    }
}
