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
        var body = string.Format(
            CultureInfo.InvariantCulture,
            "Transcribe HTTP start HttpAttemptId={0} chunk={1}{2} model={3} workers={4} inFlight={5} file={6} bytes={7}",
            attempt,
            chunkIndex,
            subPart,
            model,
            parallelWorkersConfigured,
            inFlight,
            file,
            bytes);

        TranscriptActivityLogVmAppender.Append(
            _nodeModel,
            _hub,
            agentJobId,
            chunkIndex,
            subChunkIndex,
            body,
            TranscriptActivityLogKind.Information,
            setFooterHint: true);
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
        var body = string.Format(
            CultureInfo.InvariantCulture,
            "Retry {0}/3 chunk={1}{2} file={3} HTTP {4} ({5}) {6}",
            nextAttempt,
            chunkIndex,
            subPart,
            file,
            statusCode,
            category,
            shortDetail);

        TranscriptActivityLogVmAppender.Append(
            _nodeModel,
            _hub,
            agentJobId,
            chunkIndex,
            subChunkIndex,
            body,
            TranscriptActivityLogKind.Warning,
            setFooterHint: true);
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
        var body = string.Format(
            CultureInfo.InvariantCulture,
            "Retry attempt {0}/3 for model {1} chunk={2}{3} file={4}",
            attemptNumber,
            model,
            chunkIndex,
            subPart,
            file);

        TranscriptActivityLogVmAppender.Append(
            _nodeModel,
            _hub,
            agentJobId,
            chunkIndex,
            subChunkIndex,
            body,
            TranscriptActivityLogKind.Information,
            setFooterHint: true);
    }

    public void OnTranscriptionHttpDiagnosticLine(
        string? agentJobId,
        int chunkIndex,
        int? subChunkIndex,
        string messageAfterTimestamp)
    {
        if (string.IsNullOrWhiteSpace(messageAfterTimestamp))
            return;

        TranscriptActivityLogVmAppender.Append(
            _nodeModel,
            _hub,
            agentJobId,
            chunkIndex,
            subChunkIndex,
            messageAfterTimestamp.Trim(),
            TranscriptActivityLogKind.Warning,
            setFooterHint: true);
    }
}
