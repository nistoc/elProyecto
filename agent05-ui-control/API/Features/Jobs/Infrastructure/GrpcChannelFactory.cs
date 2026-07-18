using Grpc.Net.Client;

namespace XtractManager.Features.Jobs.Infrastructure;

internal static class GrpcChannelFactory
{
    /// <summary>
    /// Long-lived h2c channel with HTTP/2 keepalive pings so dead sockets are detected
    /// (and streams fail fast) instead of hanging forever. One channel per client class; dispose with the host.
    /// </summary>
    public static GrpcChannel CreateKeepAliveChannel(string address)
    {
        var handler = new SocketsHttpHandler
        {
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            EnableMultipleHttp2Connections = true,
        };
        return GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = handler,
            // After an outage the balancer's default reconnect backoff grows to minutes,
            // so calls kept failing long after the service was back. Cap it at 5s.
            InitialReconnectBackoff = TimeSpan.FromSeconds(1),
            MaxReconnectBackoff = TimeSpan.FromSeconds(5),
        });
    }
}
