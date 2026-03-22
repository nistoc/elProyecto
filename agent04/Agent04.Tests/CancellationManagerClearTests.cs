using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class CancellationManagerClearTests
{
    [Fact]
    public void ClearChunkCancelFlag_removes_disk_file_so_fresh_manager_not_cancelled()
    {
        var cancelDir = Path.Combine(Path.GetTempPath(), "agent04-clear-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cancelDir);
        try
        {
            var cm = new CancellationManager(cancelDir);
            cm.MarkCancelled(2);
            Assert.True(cm.IsCancelled(2));

            Assert.True(cm.ClearChunkCancelFlag(2));

            var cm2 = new CancellationManager(cancelDir);
            Assert.False(cm2.IsCancelled(2));
        }
        finally
        {
            try
            {
                Directory.Delete(cancelDir, recursive: true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }
}
