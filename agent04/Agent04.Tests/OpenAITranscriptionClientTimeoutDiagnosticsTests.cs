using System.Net;
using System.Net.Http;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class OpenAITranscriptionClientTimeoutDiagnosticsTests
{
    /// <summary>
    /// When HttpClient times out (not user cancellation), SendRequestAsync logs http_client_timeout
    /// and must append the same diagnostic to the chunk VM transcript activity log via the sink.
    /// </summary>
    [Fact]
    public async Task TranscribeAsync_HttpClientTimeout_AppendsTranscriptActivityLog()
    {
        var handler = new InfiniteDelayHandler();
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.invalid/"),
            Timeout = TimeSpan.FromMilliseconds(50)
        };

        var store = new InMemoryNodeStore();
        const string agentJobId = "diag-job";
        const int chunkIndex = 0;
        var nodeId = $"{agentJobId}:transcribe:chunk-{chunkIndex}";
        store.EnsureNode(nodeId, null, agentJobId, "transcribe");

        var sink = new TranscriptionDiagnosticsSink(store, new TranscriptionTelemetryHub());
        var client = new OpenAITranscriptionClient(
            http,
            apiKey: "test-key",
            model: "whisper-1",
            fallbackModels: new[] { "whisper-1" },
            logger: null,
            diagnosticsSink: sink);

        var path = Path.GetTempFileName();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.TranscribeAsync(
                    path,
                    new TranscriptionClientOptions
                    {
                        AgentJobId = agentJobId,
                        ChunkIndex = chunkIndex
                    }));

            var node = store.GetNodeByScopeAndId(agentJobId, nodeId);
            Assert.NotNull(node?.Metadata);
            Assert.True(node!.Metadata!.TryGetValue("transcript_activity_log", out var raw));
            var log = raw as string ?? "";
            Assert.Contains("http_client_timeout", log, StringComparison.Ordinal);
            Assert.Contains("OpenAI transcription HTTP timeout", log, StringComparison.Ordinal);
        }
        finally
        {
            try { File.Delete(path); }
            catch
            {
                // best-effort temp cleanup
            }
        }
    }

    private sealed class InfiniteDelayHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
