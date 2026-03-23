using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class TranscriptionDiagnosticsSinkTests
{
    [Fact]
    public void OnTranscriptionHttpDiagnosticLine_AppendsIsoPrefixedLine_ToChunkNode()
    {
        var store = new InMemoryNodeStore();
        const string agentJobId = "job-a";
        const int chunkIndex = 2;
        var nodeId = $"{agentJobId}:transcribe:chunk-{chunkIndex}";
        store.EnsureNode(nodeId, null, agentJobId, "transcribe");

        var hub = new TranscriptionTelemetryHub();
        var sink = new TranscriptionDiagnosticsSink(store, hub);

        sink.OnTranscriptionHttpDiagnosticLine(
            agentJobId,
            chunkIndex,
            subChunkIndex: null,
            "OpenAI transcription HTTP timeout HttpAttemptId=… Category=timeout Reason=http_client_timeout");

        var node = store.GetNodeByScopeAndId(agentJobId, nodeId);
        Assert.NotNull(node?.Metadata);
        Assert.True(node!.Metadata!.TryGetValue("transcript_activity_log", out var raw));
        var log = raw as string ?? "";
        Assert.Contains("[warn] OpenAI transcription HTTP timeout", log, StringComparison.Ordinal);
        Assert.Contains("http_client_timeout", log, StringComparison.Ordinal);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T", log.Split('\n')[^1].Trim());

        Assert.Contains("OpenAI transcription HTTP timeout", hub.GetFooterHint(agentJobId), StringComparison.Ordinal);
    }

    [Fact]
    public void OnTranscriptionHttpDiagnosticLine_UsesSubChunkNodeId_WhenSubChunkIndexPresent()
    {
        var store = new InMemoryNodeStore();
        const string agentJobId = "job-b";
        const int chunkIndex = 1;
        const int sub = 0;
        var nodeId = $"{agentJobId}:transcribe:chunk-{chunkIndex}:sub-{sub}";
        store.EnsureNode(nodeId, null, agentJobId, "transcribe");

        var sink = new TranscriptionDiagnosticsSink(store, new TranscriptionTelemetryHub());
        sink.OnTranscriptionHttpDiagnosticLine(
            agentJobId,
            chunkIndex,
            subChunkIndex: sub,
            "diagnostic test line");

        var node = store.GetNodeByScopeAndId(agentJobId, nodeId);
        Assert.NotNull(node?.Metadata);
        Assert.True(node!.Metadata!.TryGetValue("transcript_activity_log", out var raw));
        Assert.Contains("diagnostic test line", raw as string ?? "", StringComparison.Ordinal);
    }
}
