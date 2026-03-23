namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Optional sink for operator-visible transcription diagnostics (HTTP retries, etc.).
/// </summary>
public interface ITranscriptionDiagnosticsSink
{
    /// <summary>
    /// Called immediately before <c>SendAsync</c> for an OpenAI transcription HTTP request (matches console "HTTP start").
    /// </summary>
    void OnTranscriptionHttpRequestStarting(
        string? agentJobId,
        int chunkIndex,
        int? subChunkIndex,
        string httpAttemptId,
        string model,
        int parallelWorkersConfigured,
        int inFlight,
        string audioFileName,
        long bytes);

    /// <summary>
    /// Called when the client will retry after a failed HTTP attempt (e.g. OpenAI 5xx).
    /// </summary>
    void OnTranscriptionHttpRetryScheduled(
        string? agentJobId,
        int chunkIndex,
        int? subChunkIndex,
        string audioFileName,
        int nextAttempt,
        int statusCode,
        string category,
        string shortDetail);

    /// <summary>
    /// Logged immediately before a follow-up HTTP attempt (matches console "Retry attempt N/3 for model …").
    /// </summary>
    void OnTranscriptionHttpRetryAttemptStarting(
        string? agentJobId,
        int chunkIndex,
        int? subChunkIndex,
        string audioFileName,
        int attemptNumber,
        string model);
}
