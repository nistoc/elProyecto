using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class CancellationManagerSubChunkTests
{
    [Fact]
    public void MarkSubChunk_and_IsSubChunkCancelled_roundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agent04_cancel_sub_" + Guid.NewGuid().ToString("N"));
        try
        {
            var cm = new CancellationManager(dir);
            Assert.False(cm.IsSubChunkCancelled(2, 1));
            cm.MarkSubChunkCancelled(2, 1);
            Assert.True(cm.IsSubChunkCancelled(2, 1));
            Assert.False(cm.IsSubChunkCancelled(2, 0));
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

    [Fact]
    public void Poll_picks_up_cancel_sub_file_from_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agent04_cancel_sub_scan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "cancel_sub_5_2.flag"), "x");
            var cm = new CancellationManager(dir);
            Assert.True(cm.IsSubChunkCancelled(5, 2));
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
