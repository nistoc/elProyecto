using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class ChunkMarkdownStitcherTests
{
    [Fact]
    public void ExtractTranscriptBodyLines_StripsWrappers()
    {
        var content = ">>>>>>>\n- 0.05 speaker_0: \"Hola\"\n- 1.10 speaker_1: \"Sí\"\n<<<<<\n";
        var body = ChunkMarkdownStitcher.ExtractTranscriptBodyLines(content);
        Assert.Equal("- 0.05 speaker_0: \"Hola\"\n- 1.10 speaker_1: \"Sí\"\n", body);
    }

    [Fact]
    public void ExtractTranscriptBodyLines_NoWrappers_ReturnsTrimmed()
    {
        var body = ChunkMarkdownStitcher.ExtractTranscriptBodyLines("  - 0.05 speaker_0: \"x\"  ");
        Assert.Equal("- 0.05 speaker_0: \"x\"", body);
    }

    [Fact]
    public void WritePerChunkMarkdown_ThenStitch_ProducesWrappedConcatenation()
    {
        var root = Path.Combine(Path.GetTempPath(), "a04stitch_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var writer = new TranscriptionOutputWriter();
            var mdDir = Path.Combine(root, "chunks_md");

            writer.WritePerChunkMarkdown("job_part_000.wav",
                new[] { new ASRSegment(0.0, 1.0, "Hola", "A") }, 0.0, 0.0, mdDir);
            writer.WritePerChunkMarkdown("job_part_001.wav",
                new[] { new ASRSegment(0.0, 1.0, "Adiós", "B") }, 100.0, 0.0, mdDir);

            // Each per-chunk file is a standalone wrapped document starting fresh at speaker_0.
            var c0 = File.ReadAllText(Path.Combine(mdDir, "job_part_000.md"));
            Assert.StartsWith(">>>>>>>\n", c0);
            Assert.Contains("- 0.00 speaker_0: \"Hola\"", c0);
            Assert.EndsWith("<<<<<\n", c0);

            var finalMd = Path.Combine(root, "transcript.md");
            ChunkMarkdownStitcher.Stitch(root, new[] { "job_part_000", "job_part_001" }, "chunks_md", "split_chunks", finalMd, writer, null);

            var stitched = File.ReadAllText(finalMd);
            Assert.StartsWith(">>>>>>>\n", stitched);
            Assert.Contains("- 0.00 speaker_0: \"Hola\"", stitched);
            Assert.Contains("- 100.00 speaker_0: \"Adiós\"", stitched);
            Assert.EndsWith("<<<<<\n", stitched);
            // The inner >>>>>>>/<<<<< wrappers of each chunk must be stripped (only one pair remains).
            Assert.Equal(1, stitched.Split(">>>>>>>").Length - 1);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
