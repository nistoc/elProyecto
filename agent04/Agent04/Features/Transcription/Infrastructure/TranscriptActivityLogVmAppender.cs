using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// One code path for footer hint + <see cref="INodeModel.AppendTranscriptActivityLog"/> for transcribe chunk VM rows.
/// </summary>
internal static class TranscriptActivityLogVmAppender
{
    public static void Append(
        INodeModel? nodeModel,
        TranscriptionTelemetryHub hub,
        string? agentJobId,
        int chunkIndex,
        int? subChunkIndex,
        string messageWithoutTimestamp,
        TranscriptActivityLogKind kind,
        bool setFooterHint)
    {
        var line = TranscriptActivityLogFormatter.FormatLine(messageWithoutTimestamp, kind);
        if (setFooterHint && !string.IsNullOrEmpty(agentJobId))
            hub.SetFooterHint(agentJobId, line);

        if (nodeModel == null || string.IsNullOrEmpty(agentJobId) || chunkIndex < 0)
            return;

        var nodeId = TranscriptVmNodeId.ForTranscribeChunk(agentJobId, chunkIndex, subChunkIndex);
        if (string.IsNullOrEmpty(nodeId))
            return;
        nodeModel.AppendTranscriptActivityLog(nodeId, line);
    }
}
