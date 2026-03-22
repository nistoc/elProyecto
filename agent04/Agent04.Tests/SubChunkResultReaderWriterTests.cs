using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class SubChunkResultReaderWriterTests
{
    [Fact]
    public void RoundTrip_preserves_segments_offset_and_raw()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agent04_subchunk_rw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var raw = new Dictionary<string, object?> { ["text"] = "hello" };
            var tr = new TranscriptionResult(
                "clip_sub_00.m4a",
                1.5,
                0.25,
                new[] { new ASRSegment(0.1, 0.9, "hola", "spk_0") },
                raw);

            SubChunkResultWriter.Save(dir, 0, tr);

            var path = Path.Combine(dir, "sub_chunk_00_result.json");
            Assert.True(File.Exists(path));

            var loaded = SubChunkResultReader.TryLoad(path);
            Assert.NotNull(loaded);
            Assert.Equal(tr.ChunkBasename, loaded!.ChunkBasename);
            Assert.Equal(tr.Offset, loaded.Offset, 4);
            Assert.Equal(tr.EmitGuard, loaded.EmitGuard, 4);
            Assert.Single(loaded.Segments);
            Assert.Equal(0.1, loaded.Segments[0].Start, 4);
            Assert.Equal(0.9, loaded.Segments[0].End, 4);
            Assert.Equal("hola", loaded.Segments[0].Text);
            Assert.Equal("spk_0", loaded.Segments[0].Speaker);
            Assert.True(loaded.RawResponse.ContainsKey("text"));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
