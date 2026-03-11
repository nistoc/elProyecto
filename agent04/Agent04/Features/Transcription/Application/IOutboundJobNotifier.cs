namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Stub for outbound notification when a job completes or fails (e.g. push via gRPC to another service).
/// Default implementation is no-op; replace with a real gRPC client when a notification service exists.
/// </summary>
public interface IOutboundJobNotifier
{
    Task NotifyJobCompletedAsync(string jobId, JobState state, CancellationToken cancellationToken = default);
}
