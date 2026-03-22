using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class SplitChunkPathsTests
{
    [Fact]
    public void FindSubChunkAudioFile_matches_sub_index_with_leading_zeros()
    {
        var dir = Path.Combine(Path.GetTempPath(), "split-sub-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "x_sub_01.m4a"), "");
            File.WriteAllText(Path.Combine(dir, "x_sub_02.m4a"), "");
            Assert.Null(SplitChunkPaths.FindSubChunkAudioFile(dir, 0));
            Assert.Equal(Path.Combine(dir, "x_sub_01.m4a"), SplitChunkPaths.FindSubChunkAudioFile(dir, 1));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
