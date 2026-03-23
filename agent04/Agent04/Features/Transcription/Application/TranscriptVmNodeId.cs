namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Stable virtual-model node ids for per-chunk / sub-chunk transcription activity (matches pipeline &amp; gRPC VM).
/// </summary>
public static class TranscriptVmNodeId
{
    public static string ForTranscribeChunk(string agentJobId, int chunkIndex, int? subChunkIndex)
    {
        if (string.IsNullOrEmpty(agentJobId) || chunkIndex < 0)
            return "";
        return subChunkIndex is >= 0
            ? $"{agentJobId}:transcribe:chunk-{chunkIndex}:sub-{subChunkIndex.Value}"
            : $"{agentJobId}:transcribe:chunk-{chunkIndex}";
    }
}
