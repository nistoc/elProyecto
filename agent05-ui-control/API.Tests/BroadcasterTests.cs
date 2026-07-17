using System.Threading.Channels;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class BroadcasterTests
{
    private static Channel<BroadcastEvent> NewChannel() =>
        Channel.CreateBounded<BroadcastEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    [Fact]
    public void Publish_WithoutSubscribers_DoesNotThrow()
    {
        var broadcaster = new Broadcaster();
        broadcaster.Publish("job", new BroadcastEvent("status", "{\"type\":\"status\"}"));
    }

    [Fact]
    public async Task TwoSubscribers_BothReceiveEvent()
    {
        var broadcaster = new Broadcaster();
        var first = NewChannel();
        var second = NewChannel();
        using var sub1 = broadcaster.Subscribe("job", first.Writer);
        using var sub2 = broadcaster.Subscribe("job", second.Writer);

        var evt = new BroadcastEvent("status", "{\"type\":\"status\"}");
        broadcaster.Publish("job", evt);

        Assert.Equal(evt, await first.Reader.ReadAsync());
        Assert.Equal(evt, await second.Reader.ReadAsync());
    }

    [Fact]
    public void DisposedSubscription_ReceivesNothing()
    {
        var broadcaster = new Broadcaster();
        var channel = NewChannel();
        var sub = broadcaster.Subscribe("job", channel.Writer);
        sub.Dispose();

        broadcaster.Publish("job", new BroadcastEvent("status", "{\"type\":\"status\"}"));

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public void Publish_ForOtherJob_IsNotDelivered()
    {
        var broadcaster = new Broadcaster();
        var channel = NewChannel();
        using var sub = broadcaster.Subscribe("job-a", channel.Writer);

        broadcaster.Publish("job-b", new BroadcastEvent("status", "{\"type\":\"status\"}"));

        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public void ConcurrentSubscribePublishDispose_DoesNotThrow_AndLiveSubscriberReceives()
    {
        var broadcaster = new Broadcaster();
        var live = NewChannel();
        using var keep = broadcaster.Subscribe("job", live.Writer);

        Parallel.For(0, 10_000, _ =>
        {
            var transient = NewChannel();
            var sub = broadcaster.Subscribe("job", transient.Writer);
            broadcaster.Publish("job", new BroadcastEvent("status", "{\"type\":\"status\"}"));
            sub.Dispose();
        });

        Assert.True(live.Reader.TryRead(out _));
    }
}
