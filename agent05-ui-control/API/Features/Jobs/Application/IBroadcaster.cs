namespace XtractManager.Features.Jobs.Application;

public interface IBroadcaster
{
    void Subscribe(string jobId, Action<string> send);
    void Unsubscribe(string jobId, Action<string> send);
    void Publish(string jobId, string payload);
}
