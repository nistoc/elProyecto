using System.Collections.Concurrent;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class Broadcaster : Application.IBroadcaster
{
    private readonly ConcurrentDictionary<string, List<Action<string>>> _subs = new();

    public void Subscribe(string jobId, Action<string> send)
    {
        _subs.AddOrUpdate(jobId, _ => new List<Action<string>> { send }, (_, list) =>
        {
            lock (list) list.Add(send);
            return list;
        });
    }

    public void Unsubscribe(string jobId, Action<string> send)
    {
        if (!_subs.TryGetValue(jobId, out var list))
            return;
        lock (list)
        {
            list.RemoveAll(a => a == send);
            if (list.Count == 0)
                _subs.TryRemove(jobId, out _);
        }
    }

    public void Publish(string jobId, string payload)
    {
        if (!_subs.TryGetValue(jobId, out var list))
            return;
        List<Action<string>> copy;
        lock (list)
        {
            copy = list.ToList();
        }
        foreach (var send in copy)
        {
            try { send(payload); } catch { /* ignore */ }
        }
    }
}
