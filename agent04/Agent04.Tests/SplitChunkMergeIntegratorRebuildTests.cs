using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agent04.Tests;

public sealed class SplitChunkMergeIntegratorRebuildTests
{
    [Fact]
    public async Task TryRebuildSplitMergedForChunkAsync_writes_merged_json_and_md_when_all_sub_results_exist()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent04_split_merge_rebuild_" + Guid.NewGuid().ToString("N"));
        var chunkFolder = Path.Combine(root, "split_chunks", "chunk_0");
        var subChunksDir = Path.Combine(chunkFolder, "sub_chunks");
        var resultsDir = Path.Combine(chunkFolder, "results");
        Directory.CreateDirectory(subChunksDir);
        Directory.CreateDirectory(resultsDir);

        await File.WriteAllTextAsync(Path.Combine(subChunksDir, "clip_sub_00.m4a"), "");
        await File.WriteAllTextAsync(Path.Combine(subChunksDir, "clip_sub_01.m4a"), "");

        var tr0 = new TranscriptionResult(
            "clip_sub_00.m4a",
            0,
            0,
            new[] { new ASRSegment(0.1, 0.5, "uno", "spk_0") },
            new Dictionary<string, object?>());
        var tr1 = new TranscriptionResult(
            "clip_sub_01.m4a",
            0,
            0,
            new[] { new ASRSegment(0.1, 0.5, "dos", "spk_0") },
            new Dictionary<string, object?>());
        SubChunkResultWriter.Save(resultsDir, 0, tr0);
        SubChunkResultWriter.Save(resultsDir, 1, tr1);

        var config = new TranscriptionConfig(new Dictionary<string, object?>());
        var merger = new TranscriptionMerger();

        try
        {
            var (ok, msg) = await SplitChunkMergeIntegrator.TryRebuildSplitMergedForChunkAsync(
                config,
                root,
                "job-test",
                0,
                merger,
                NullLogger.Instance,
                CancellationToken.None);

            Assert.True(ok, msg);
            Assert.Equal("rebuild_split_merged_ok", msg);
            Assert.True(File.Exists(Path.Combine(chunkFolder, "chunk_0_merged.json")));
            Assert.True(File.Exists(Path.Combine(chunkFolder, "chunk_0_merged.md")));
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
