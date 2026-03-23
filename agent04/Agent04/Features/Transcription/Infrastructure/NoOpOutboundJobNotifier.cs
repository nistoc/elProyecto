using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// No-op implementation of IOutboundJobNotifier. Replace with a real gRPC client when needed.
/// </summary>
public sealed class NoOpOutboundJobNotifier : IOutboundJobNotifier
{
    public Task NotifyJobCompletedAsync(string jobId, JobState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
