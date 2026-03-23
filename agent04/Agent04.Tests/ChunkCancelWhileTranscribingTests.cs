using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class ChunkCancelWhileTranscribingTests
{
    [Fact]
    public async Task PollChunkCancelWhileTranscribingAsync_cancels_linked_cts_when_chunk_marked()
    {
        var cancelDir = Path.Combine(Path.GetTempPath(), "agent04-cancel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cancelDir);
        try
        {
            var cm = new CancellationManager(cancelDir);
            using var transcribeCts = new CancellationTokenSource();
            using var stopPolling = new CancellationTokenSource();

            var poll = TranscriptionPipeline.PollChunkCancelWhileTranscribingAsync(
                cm,
                chunkIndex: 2,
                transcribeCts,
                stopPolling.Token);

            await Task.Delay(80);
            cm.MarkCancelled(2);

            var completed = await Task.WhenAny(poll, Task.Delay(5000));
            Assert.Same(poll, completed);
            Assert.True(transcribeCts.IsCancellationRequested);

            stopPolling.Cancel();
            try
            {
                await poll;
            }
            catch
            {
                /* ignore */
            }
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
