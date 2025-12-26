using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Options;

namespace CHttpServer;

public class Http2CHttpServer
{
    private readonly CHttpServerOptions _options;
    private readonly FeatureCollection _features;
    private readonly ConnectionsManager _connectionManager;
    private readonly CancellationTokenSource _serverShutdownToken;
    private Socket? _listenSocket;
    private Task? _acceptingConnections;

    public Http2CHttpServer(
        IOptions<CHttpServerOptions> options,
        FeatureCollection features)
    {
        _options = options.Value;
        _features = features;
        _serverShutdownToken = new();
        _connectionManager = new ConnectionsManager();
    }

    public Task StartAsync<TContext>(
        IPEndPoint endpoint,
        IHttpApplication<TContext> application,
        CancellationToken cancellationToken) where TContext : notnull
    {
        var listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (endpoint.Address.Equals(IPAddress.IPv6Any))
            listenSocket.DualMode = true;
        listenSocket.Bind(endpoint);
        listenSocket.Listen(512);
        _listenSocket = listenSocket;

        var httpConnection = new ConnectionDispatcher<TContext>(application);
        var httpsMiddleware = new HttpsConnectionMiddleware(httpConnection.OnConnectionAsync, new Microsoft.AspNetCore.Server.Kestrel.Https.HttpsConnectionAdapterOptions()
        {
            ServerCertificate = _options.GetCertificate()
        });
        Func<CHttp2ConnectionContext, Task> connectionDelegate = httpsMiddleware.OnConnectionAsync;

        _acceptingConnections = StartAcceptAsync<TContext>(connectionDelegate);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _serverShutdownToken.Cancel();
        if (_acceptingConnections != null)
            await _acceptingConnections.AllowCancellation();
    }

    private async Task StartAcceptAsync<TContext>(Func<CHttp2ConnectionContext, Task> connectionDelegate) where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(_listenSocket);
        while (true)
        {
            var connection = await _listenSocket.AcceptAsync(_serverShutdownToken.Token);
            if (connection == null)
            {
                break;
            }

            var networkStream = new NetworkStream(connection);
            var connectionId = _connectionManager.GetNewConnectionId();
            var connectionContext = new CHttp2ConnectionContext()
            {
                Features = _features.ToContextAware<TContext>(),
                Transport = networkStream,
                ConnectionId = connectionId,
                ServerOptions = _options,
                ConnectionCancellation = new()
            };
            var chttpConnection = new CHttp2Connection<TContext>(connectionContext, _connectionManager, connectionDelegate);
            _connectionManager.AddConnection(connectionId, chttpConnection);
#if DEBUG
            chttpConnection.Execute();
#else
            ThreadPool.UnsafeQueueUserWorkItem(chttpConnection, preferLocal: false);
#endif
        }
    }
}
