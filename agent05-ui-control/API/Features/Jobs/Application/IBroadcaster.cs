using System.Threading.Channels;

namespace XtractManager.Features.Jobs.Application;

/// <summary>One SSE event; <see cref="Json"/> is the complete serialized payload written to the wire.</summary>
public sealed record BroadcastEvent(string Type, string Json);

public interface IBroadcaster
{
    /// <summary>Registers a subscriber channel for a job. Dispose the returned handle to unsubscribe.</summary>
    IDisposable Subscribe(string jobId, ChannelWriter<BroadcastEvent> writer);
    void Publish(string jobId, BroadcastEvent evt);
}
