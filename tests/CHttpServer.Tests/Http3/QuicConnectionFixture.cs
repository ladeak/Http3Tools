using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace CHttpServer.Tests.Http3;

internal static class QuicConnectionFixture
{
    internal record class ClientServerConnection(QuicConnection ClientConnection, QuicConnection ServerConnection, QuicListener Listener) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await ClientConnection.DisposeAsync();
            await ServerConnection.DisposeAsync();
            await Listener.DisposeAsync();
        }
    }

    internal static async Task<ClientServerConnection> SetupConnectionAsync(int port, CancellationToken token)
    {
        (ValueTask<QuicConnection> quicServerConnecting, QuicListener listener) = await CreateServerAsync(port, token);
        var quicClientConnection = await ConnectClientAsync(port, token);
        var quicServerConnection = await quicServerConnecting;
        return new ClientServerConnection(quicClientConnection, quicServerConnection, listener);
    }

    internal static async Task<(ValueTask<QuicConnection>, QuicListener)> CreateServerAsync(int port, CancellationToken token)
    {
        var serverConnectionOptions = new QuicServerConnectionOptions
        {
            DefaultStreamErrorCode = 0x010C,
            DefaultCloseErrorCode = 0x0100,
            ServerAuthenticationOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile("testCert.pfx", "testPassword"),
                ApplicationProtocols = [new SslApplicationProtocol("h3"u8.ToArray())],
                EnabledSslProtocols = SslProtocols.Tls13
            }
        };
        var listener = await QuicListener.ListenAsync(new QuicListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, port),
            ListenBacklog = 1,
            ApplicationProtocols = [new SslApplicationProtocol("h3"u8.ToArray())],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(serverConnectionOptions)
        }, token);
        var quicServerConnecting = listener.AcceptConnectionAsync(token);
        return (quicServerConnecting, listener);
    }

    internal static async Task<QuicConnection> ConnectClientAsync(int port, CancellationToken token)
    {
        return await QuicConnection.ConnectAsync(new QuicClientConnectionOptions()
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, port),
            DefaultCloseErrorCode = 0x0100,
            DefaultStreamErrorCode = 0x010C,
            MaxInboundUnidirectionalStreams = 1,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions() { RemoteCertificateValidationCallback = (_, _, _, _) => true, ApplicationProtocols = [new SslApplicationProtocol("h3"u8.ToArray())] }
        }, token);
    }
}


