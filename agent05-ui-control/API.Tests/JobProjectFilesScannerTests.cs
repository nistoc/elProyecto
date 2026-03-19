using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class JobProjectFilesScannerTests
{
    [Fact]
    public void Scan_categorizes_root_and_subdirs_like_agent_browser()
    {
        var root = Path.Combine(Path.GetTempPath(), "XtractManagerScanTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "chunks"));
        Directory.CreateDirectory(Path.Combine(root, "chunks_json"));
        Directory.CreateDirectory(Path.Combine(root, "intermediate_results"));
        Directory.CreateDirectory(Path.Combine(root, "converted_wav"));

        File.WriteAllText(Path.Combine(root, "source.m4a"), "fake");
        File.WriteAllText(Path.Combine(root, "transcript.md"), "a\nb\n");
        File.WriteAllText(Path.Combine(root, "response.json"), "{}");
        File.WriteAllText(Path.Combine(root, "chunks", "x_part_000.wav"), "");
        File.WriteAllText(Path.Combine(root, "chunks", "x_part_001.wav"), "");
        File.WriteAllText(Path.Combine(root, "chunks_json", "x_part_000.json"), "{}");
        File.WriteAllText(Path.Combine(root, "intermediate_results", "chunk_000_result.json"), "{}");
        File.WriteAllText(Path.Combine(root, "converted_wav", "full.wav"), "");

        try
        {
            var files = JobProjectFilesScanner.Scan(root);

            Assert.Single(files.Original);
            Assert.Equal("source.m4a", files.Original[0].Name);
            Assert.Equal(2, files.Transcripts.Count);
            Assert.Contains(files.Transcripts, t => t.Name == "transcript.md");
            Assert.Contains(files.Transcripts, t => t.Name == "response.json");

            Assert.Equal(2, files.Chunks.Count);
            Assert.Equal(0, files.Chunks[0].Index);
            Assert.Equal(1, files.Chunks[1].Index);

            Assert.Single(files.ChunkJson);
            Assert.Equal(0, files.ChunkJson[0].Index);

            Assert.Single(files.Intermediate);
            Assert.Single(files.Converted);
            Assert.Empty(files.SplitChunks);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Scan_empty_dir_returns_empty_collections()
    {
        var root = Path.Combine(Path.GetTempPath(), "XtractManagerScanTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var files = JobProjectFilesScanner.Scan(root);
            Assert.Empty(files.Original);
            Assert.Empty(files.Transcripts);
            Assert.Empty(files.Chunks);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
