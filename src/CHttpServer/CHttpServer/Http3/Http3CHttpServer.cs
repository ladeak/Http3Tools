using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Options;

namespace CHttpServer.Http3;

public class Http3CHttpServer
{
    private readonly CHttpServerOptions _options;
    private readonly FeatureCollection _features;
    private readonly ConnectionsManager _connectionManager;
    private readonly CancellationToken _cancellationToken;
    private QuicListener? _listener;

    public Http3CHttpServer(
        IOptions<CHttpServerOptions> options,
        FeatureCollection features,
        CancellationToken token)
    {
        _options = options.Value;
        _features = features;
        _cancellationToken = token;
        _connectionManager = new ConnectionsManager();
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public async Task StartAsync<TContext>(
        IPEndPoint endpoint,
        IHttpApplication<TContext> application,
        CancellationToken cancellationToken) where TContext : notnull
    {
        var certificate = _options.GetCertificate();
        var serverConnectionOptions = new QuicServerConnectionOptions
        {
            DefaultStreamErrorCode = 0x010C,
            DefaultCloseErrorCode = 0x0100,
            ServerAuthenticationOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ApplicationProtocols = [new SslApplicationProtocol("h3"u8.ToArray())],
                EnabledSslProtocols = SslProtocols.Tls13
            }
        };
        _listener = await QuicListener.ListenAsync(new QuicListenerOptions
        {
            ListenEndPoint = endpoint,
            ListenBacklog = 512,
            ApplicationProtocols = [new SslApplicationProtocol("h3"u8.ToArray())],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(serverConnectionOptions)
        });

        _ = StartAcceptAsync(application);
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private async Task StartAcceptAsync<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(_listener);
        while (true)
        {
            var connection = await _listener.AcceptConnectionAsync(_cancellationToken);
            if (connection == null)
            {
                break;
            }

            var connectionId = _connectionManager.GetNewConnectionId();
            var connectionContext = new CHttp3ConnectionContext()
            {
                Features = _features.Copy(),
                Transport = connection,
                ConnectionId = connectionId,
                ServerOptions = _options,
            };

            var chttpConnection = new CHttp3Connection<TContext>(connectionContext, _connectionManager, application);
            _connectionManager.AddConnection(connectionId, chttpConnection);
#if DEBUG
            chttpConnection.Execute();
#else
            ThreadPool.UnsafeQueueUserWorkItem(chttpConnection, preferLocal: false);
#endif
        }
    }
}
