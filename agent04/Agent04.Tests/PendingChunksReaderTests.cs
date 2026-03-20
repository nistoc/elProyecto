using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class PendingChunksReaderTests
{
    [Fact]
    public async Task TryLoadAndConsumeAsync_reads_indices_and_deletes_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agent04-pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, PendingChunksReader.FileName);
        await File.WriteAllTextAsync(path, """{"chunk_indices":[1,2,3]}""");

        var set = await PendingChunksReader.TryLoadAndConsumeAsync(dir, CancellationToken.None);
        Assert.NotNull(set);
        Assert.Equal(3, set!.Count);
        Assert.Contains(1, set);
        Assert.Contains(2, set);
        Assert.False(File.Exists(path));

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }
}
