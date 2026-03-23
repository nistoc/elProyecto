using System.Text.Json;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class RefinerCheckpointProgressReaderTests
{
    [Fact]
    public void TryRead_parses_next_and_total_and_remaining()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ckpt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "refiner_threads"));
        try
        {
            var json = JsonSerializer.Serialize(new { nextBatchIndex = 3, totalBatches = 10 });
            File.WriteAllText(Path.Combine(dir, "refiner_threads", "checkpoint.json"), json);
            Assert.True(RefinerCheckpointProgressReader.TryRead(dir, out var sum));
            Assert.NotNull(sum);
            Assert.Equal(3, sum!.NextBatchIndex0);
            Assert.Equal(10, sum.TotalBatches);
            Assert.Equal(7, sum.RemainingBatches);
            Assert.True(sum.CanResume);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryRead_CanResume_false_when_done()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ckpt-done-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "refiner_threads"));
        try
        {
            var json = JsonSerializer.Serialize(new { nextBatchIndex = 10, totalBatches = 10 });
            File.WriteAllText(Path.Combine(dir, "refiner_threads", "checkpoint.json"), json);
            Assert.True(RefinerCheckpointProgressReader.TryRead(dir, out var sum));
            Assert.NotNull(sum);
            Assert.False(sum!.CanResume);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
