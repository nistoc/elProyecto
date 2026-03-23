using System.Globalization;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Writes retry diagnostics to per-chunk VM metadata and job-level footer hint.
/// </summary>
public sealed class TranscriptionDiagnosticsSink : ITranscriptionDiagnosticsSink
{
    private readonly INodeModel? _nodeModel;
    private readonly TranscriptionTelemetryHub _hub;

    public TranscriptionDiagnosticsSink(INodeModel? nodeModel, TranscriptionTelemetryHub hub)
    {
        _nodeModel = nodeModel;
        _hub = hub;
    }

    public void OnTranscriptionHttpRequestStarting(
        string? agentJobId,
        int chunkIndex,
        int? subChunkIndex,
        string httpAttemptId,
        string model,
        int parallelWorkersConfigured,
        int inFlight,
        string audioFileName,
        long bytes)
    {
        var file = string.IsNullOrEmpty(audioFileName) ? "?" : audioFileName;
        var subPart = subChunkIndex is >= 0
            ? FormattableString.Invariant($" sub={subChunkIndex.Value}")
            : "";
        var attempt = string.IsNullOrEmpty(httpAttemptId) ? "?" : httpAttemptId;
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:o} Transcribe HTTP start HttpAttemptId={1} chunk={2}{3} model={4} workers={5} inFlight={6} file={7} bytes={8}",
            DateTimeOffset.UtcNow,
            attempt,
            chunkIndex,
            subPart,
            model,
            parallelWorkersConfigured,
            inFlight,
            file,
            bytes);

        if (!string.IsNullOrEmpty(agentJobId))
            _hub.SetFooterHint(agentJobId, line);

        if (_nodeModel == null || string.IsNullOrEmpty(agentJobId) || chunkIndex < 0)
            return;

        var nodeId = subChunkIndex is >= 0
            ? $"{agentJobId}:transcribe:chunk-{chunkIndex}:sub-{subChunkIndex.Value}"
            : $"{agentJobId}:transcribe:chunk-{chunkIndex}";
        _nodeModel.AppendTranscriptActivityLog(nodeId, line);
    }

    public void OnTranscriptionHttpRetryScheduled(
        string? agentJobId,
        int chunkIndex,
        int? subChunkIndex,
        string audioFileName,
        int nextAttempt,
        int statusCode,
        string category,
        string shortDetail)
    {
        var file = string.IsNullOrEmpty(audioFileName) ? "?" : audioFileName;
        var subPart = subChunkIndex is >= 0
            ? FormattableString.Invariant($" sub={subChunkIndex.Value}")
            : "";
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:o} Retry {1}/3 chunk={2}{3} file={4} HTTP {5} ({6}) {7}",
            DateTimeOffset.UtcNow,
            nextAttempt,
            chunkIndex,
            subPart,
            file,
            statusCode,
            category,
            shortDetail);

        if (!string.IsNullOrEmpty(agentJobId))
            _hub.SetFooterHint(agentJobId, line);

        if (_nodeModel == null || string.IsNullOrEmpty(agentJobId) || chunkIndex < 0)
            return;

        var nodeId = subChunkIndex is >= 0
            ? $"{agentJobId}:transcribe:chunk-{chunkIndex}:sub-{subChunkIndex.Value}"
            : $"{agentJobId}:transcribe:chunk-{chunkIndex}";
        _nodeModel.AppendTranscriptActivityLog(nodeId, line);
    }

    public void OnTranscriptionHttpRetryAttemptStarting(
        string? agentJobId,
        int chunkIndex,
        int? subChunkIndex,
        string audioFileName,
        int attemptNumber,
        string model)
    {
        var file = string.IsNullOrEmpty(audioFileName) ? "?" : audioFileName;
        var subPart = subChunkIndex is >= 0
            ? FormattableString.Invariant($" sub={subChunkIndex.Value}")
            : "";
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:o} Retry attempt {1}/3 for model {2} chunk={3}{4} file={5}",
            DateTimeOffset.UtcNow,
            attemptNumber,
            model,
            chunkIndex,
            subPart,
            file);

        if (!string.IsNullOrEmpty(agentJobId))
            _hub.SetFooterHint(agentJobId, line);

        if (_nodeModel == null || string.IsNullOrEmpty(agentJobId) || chunkIndex < 0)
            return;

        var nodeId = subChunkIndex is >= 0
            ? $"{agentJobId}:transcribe:chunk-{chunkIndex}:sub-{subChunkIndex.Value}"
            : $"{agentJobId}:transcribe:chunk-{chunkIndex}";
        _nodeModel.AppendTranscriptActivityLog(nodeId, line);
    }
}
