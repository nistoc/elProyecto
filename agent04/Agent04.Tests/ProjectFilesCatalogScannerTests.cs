using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class ProjectFilesCatalogScannerTests
{
    [Fact]
    public async Task Scan_categorizes_layout_like_job_project_files_scanner()
    {
        var root = Path.Combine(Path.GetTempPath(), "a04-catalog-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "chunks"));
            Directory.CreateDirectory(Path.Combine(root, "chunks_json"));
            Directory.CreateDirectory(Path.Combine(root, "intermediate_results"));
            Directory.CreateDirectory(Path.Combine(root, "converted_wav"));
            var split0 = Path.Combine(root, "split_chunks", "chunk_0");
            Directory.CreateDirectory(Path.Combine(split0, "sub_chunks"));
            Directory.CreateDirectory(Path.Combine(split0, "results"));

            await File.WriteAllTextAsync(Path.Combine(root, "input.m4a"), "");
            await File.WriteAllTextAsync(Path.Combine(root, "transcript_notes.md"), "# t");
            await File.WriteAllTextAsync(Path.Combine(root, "chunks", "a_part_000.wav"), "");
            await File.WriteAllTextAsync(Path.Combine(root, "chunks_json", "chunk0.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "intermediate_results", "x.txt"), "line");
            await File.WriteAllTextAsync(Path.Combine(root, "converted_wav", "c.wav"), "");
            await File.WriteAllTextAsync(Path.Combine(split0, "sub_chunks", "seg_sub_00.wav"), "");
            await File.WriteAllTextAsync(Path.Combine(split0, "results", "sub_chunk_00_result.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(split0, "chunk_0_merged.json"), "{}");

            var cat = ProjectFilesCatalogScanner.Scan(root);

            Assert.Single(cat.Original);
            Assert.Equal("input.m4a", cat.Original[0].Name);
            Assert.Equal("audio", cat.Original[0].Kind);

            Assert.Contains(cat.Transcripts, t => t.Name == "transcript_notes.md");

            Assert.Single(cat.Chunks);
            Assert.Equal("chunks/a_part_000.wav", cat.Chunks[0].RelativePath.Replace('\\', '/'));

            Assert.Single(cat.ChunkJson);
            Assert.Equal("chunks_json/chunk0.json", cat.ChunkJson[0].RelativePath.Replace('\\', '/'));

            Assert.Single(cat.Intermediate);
            Assert.Equal("intermediate_results/x.txt", cat.Intermediate[0].RelativePath.Replace('\\', '/'));

            Assert.Single(cat.Converted);
            Assert.Equal("converted_wav/c.wav", cat.Converted[0].RelativePath.Replace('\\', '/'));

            Assert.Equal(3, cat.SplitChunks.Count);
            var rels = cat.SplitChunks.Select(f => f.RelativePath.Replace('\\', '/')).OrderBy(s => s).ToList();
            Assert.Contains("split_chunks/chunk_0/sub_chunks/seg_sub_00.wav", rels);
            Assert.Contains("split_chunks/chunk_0/results/sub_chunk_00_result.json", rels);
            Assert.Contains("split_chunks/chunk_0/chunk_0_merged.json", rels);

            var merged = cat.SplitChunks.First(f => f.Name == "chunk_0_merged.json");
            Assert.Equal(0, merged.ParentIndex);
            Assert.Null(merged.SubIndex);
            Assert.True(merged.IsTranscript);

            var subAudio = cat.SplitChunks.First(f => f.Name == "seg_sub_00.wav");
            Assert.Equal(0, subAudio.ParentIndex);
            Assert.Equal(0, subAudio.SubIndex);
            Assert.True(subAudio.HasTranscript);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }
}
