using System.Collections.Concurrent;
using System.Threading.Channels;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class Broadcaster : IBroadcaster
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, ChannelWriter<BroadcastEvent>>> _subs = new();

    public IDisposable Subscribe(string jobId, ChannelWriter<BroadcastEvent> writer)
    {
        // Empty per-job dictionaries are intentionally never removed: removing them would race
        // with a concurrent Subscribe that already obtained the doomed instance.
        var perJob = _subs.GetOrAdd(jobId, _ => new ConcurrentDictionary<Guid, ChannelWriter<BroadcastEvent>>());
        var id = Guid.NewGuid();
        perJob[id] = writer;
        return new Subscription(perJob, id);
    }

    public void Publish(string jobId, BroadcastEvent evt)
    {
        if (!_subs.TryGetValue(jobId, out var perJob))
            return;
        foreach (var writer in perJob.Values)
            writer.TryWrite(evt);
    }

    private sealed class Subscription(ConcurrentDictionary<Guid, ChannelWriter<BroadcastEvent>> perJob, Guid id) : IDisposable
    {
        public void Dispose() => perJob.TryRemove(id, out _);
    }
}
