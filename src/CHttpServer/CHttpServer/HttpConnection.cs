using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;

namespace CHttpServer;

internal sealed class Http2Connection<TContext> where TContext : notnull
{
    private readonly IHttpApplication<TContext> _application;

    public Http2Connection(IHttpApplication<TContext> application)
    {
        _application = application;
    }

    public Task OnConnectionAsync(CHttpConnectionContext connectionContext)
    {
        connectionContext.Features.Get<IConnectionHeartbeatFeature>()?.OnHeartbeat(OnHeartbeat, this);

        throw new NotImplementedException();
        //var memoryPoolFeature = connectionContext.Features.Get<IMemoryPoolFeature>();
        //var protocols = connectionContext.Features.Get<HttpProtocolsFeature>()?.HttpProtocols ?? _endpointDefaultProtocols;
        //var metricContext = connectionContext.Features.GetRequiredFeature<IConnectionMetricsContextFeature>().MetricsContext;
        //var localEndPoint = connectionContext.LocalEndPoint as IPEndPoint;

        //var httpConnectionContext = new HttpConnectionContext(
        //    connectionContext.ConnectionId,
        //    protocols,
        //    altSvcHeader,
        //    connectionContext,
        //    _serviceContext,
        //    connectionContext.Features,
        //    memoryPoolFeature?.MemoryPool ?? System.Buffers.MemoryPool<byte>.Shared,
        //    localEndPoint,
        //    connectionContext.RemoteEndPoint as IPEndPoint,
        //    metricContext);
        //httpConnectionContext.Transport = connectionContext.Transport;

        //var connection = new HttpConnection(httpConnectionContext);

        //return connection.ProcessRequestsAsync(_application);
    }

    private static void OnHeartbeat(object state) 
    { 
    
    }
}