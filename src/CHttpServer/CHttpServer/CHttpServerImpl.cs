using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace CHttpServer;

public class CHttpServerImpl : IServer
{
    private readonly CHttpServerOptions _options;
    private Socket? _listenSocket;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly FeatureCollection _features;
    private readonly ConnectionsManager _connectionManager;

    public CHttpServerImpl(IOptions<CHttpServerOptions> options)
    {
        _options = options.Value;
        _cancellationTokenSource = new CancellationTokenSource();
        _features = new FeatureCollection();
        var serverAddresses = new ServerAddressesFeature();
        Features.Set<IServerAddressesFeature>(serverAddresses);
        Features.Set<IMemoryPoolFeature>(new CHttpMemoryPool());
        _connectionManager = new ConnectionsManager();
    }

    public IFeatureCollection Features => _features;

    public void Dispose()
    {
    }

    public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull
    {
        var addresses = Features.Get<IServerAddressesFeature>()?.Addresses;
        var httpsAddress = addresses?.FirstOrDefault(x => x.StartsWith("https://"));

        Uri? uri = null;
        if (httpsAddress != null && !Uri.TryCreate(httpsAddress, UriKind.Absolute, out uri))
            throw new ArgumentNullException("Valid https address required");
        var ip = _options.Host;
        if (ip == null)
        {
            if (!IPAddress.TryParse(uri?.Host, out ip))
            {
                if (uri?.Host == "localhost")
                    ip = IPAddress.Loopback;
                else
                    ip = IPAddress.Any;
            }
        }
        var endpoint = new IPEndPoint(ip, uri?.Port ?? _options.Port ?? 5001);

        var listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        if (endpoint.Address.Equals(IPAddress.IPv6Any))
        {
            listenSocket.DualMode = true;
        }
        listenSocket.Bind(endpoint);
        if (addresses != null && !addresses.IsReadOnly && addresses.Count == 0)
            addresses.Add($"https://{endpoint.Address}:{endpoint.Port}");
        listenSocket.Listen(512);
        _listenSocket = listenSocket;

        cancellationToken.Register(() => _cancellationTokenSource.Cancel());

        var httpConnection = new ConnectionDispatcher<TContext>(application);
        var httpsMiddleware = new HttpsConnectionMiddleware(httpConnection.OnConnectionAsync, new Microsoft.AspNetCore.Server.Kestrel.Https.HttpsConnectionAdapterOptions()
        {
            ServerCertificate = _options.GetCertificate()
        });
        Func<CHttpConnectionContext, Task> connectionDelegate = httpsMiddleware.OnConnectionAsync;

        _ = StartAcceptAsync<TContext>(connectionDelegate);

        return Task.CompletedTask;
    }

    private async Task StartAcceptAsync<TContext>(Func<CHttpConnectionContext, Task> connectionDelegate)
    {
        ArgumentNullException.ThrowIfNull(_listenSocket);
        while (true)
        {
            var connection = await _listenSocket.AcceptAsync(_cancellationTokenSource.Token);
            if (connection == null)
            {
                break;
            }

            var networkStream = new NetworkStream(connection);
            var connectionId = _connectionManager.GetNewConnectionId();
            var connectionContext = new CHttpConnectionContext()
            {
                Features = _features.Copy(),
                Transport = networkStream,
                ConnectionId = connectionId,
                ServerOptions = _options,
            };
            var chttpConnection = new CHttpConnection<TContext>(connectionContext, _connectionManager, connectionDelegate);
            _connectionManager.AddConnection(connectionId, chttpConnection);

            chttpConnection.Execute();
            //ThreadPool.UnsafeQueueUserWorkItem(chttpConnection, preferLocal: false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }
}
