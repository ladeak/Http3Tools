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
    private readonly CancellationToken _serverShutdownToken;
    private QuicListener? _listener;

    /// <summary>
    /// Creates a new instance of an HTTP/3 server.
    /// </summary>
    /// <param name="options">Server options.</param>
    /// <param name="features">Feature set for the Server.</param>
    /// <param name="token">Triggered at shutdown.</param>
    public Http3CHttpServer(
        IOptions<CHttpServerOptions> options,
        FeatureCollection features,
        CancellationToken token)
    {
        _options = options.Value;
        _features = features;
        _serverShutdownToken = token;
        _connectionManager = new ConnectionsManager();
    }

    /// <summary>
    /// Starts the HTTP/3 server.
    /// </summary>
    /// <typeparam name="TContext">Context for the application.</typeparam>
    /// <param name="endpoint">Endpoint to listen on.</param>
    /// <param name="application">Application to serve the request.</param>
    /// <param name="startupCancellation">Cancels the startup of the server (not service itself).</param>
    /// <returns></returns>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public async Task StartAsync<TContext>(
        IPEndPoint endpoint,
        IHttpApplication<TContext> application,
        CancellationToken startupCancellation) where TContext : notnull
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
        }, startupCancellation);

        // No heartbeat, connection shutdown is managed by QUIC idle timeout.
        _ = RunAsync(application);
    }

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private async Task RunAsync<TContext>(IHttpApplication<TContext> application) where TContext : notnull
    {
        try
        {
            ArgumentNullException.ThrowIfNull(_listener);
            while (true)
            {
                var connection = await _listener.AcceptConnectionAsync(_serverShutdownToken);
                if (connection == null)
                {
                    break;
                }

                var connectionId = _connectionManager.GetNewConnectionId();
                var connectionContext = new CHttp3ConnectionContext()
                {
                    Features = _features.ToContextAware<TContext>(),
                    Transport = connection,
                    ConnectionId = connectionId,
                    ServerOptions = _options
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
        finally
        {
            // Shutdown, abort all connections.
            await _connectionManager.AbortAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
