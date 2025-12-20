using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

public class Http3ConnectionTests
{
    [Fact]
    public async Task SettingsFrameReceived()
    {
        var cts = new CancellationTokenSource();
        TestContext.Current.CancellationToken.Register(() => cts.Cancel());
        var (clientConnection, serverConnection) = await SetupConnectionAsync(cts);
        var readServerControlStream = Task.Run(async () =>
        {
            var inboundStream = await clientConnection.AcceptInboundStreamAsync(cts.Token);
            var pipe = PipeReader.Create(inboundStream);
            await AssertReadSettigsAsync(pipe, cts.Token);
            cts.Cancel();
            await AssertGoAwayAsync(pipe, 0);
        }, cts.Token);

        var connectionContext = new CHttp3ConnectionContext()
        {
            ServerOptions = new(),
            Features = new FeatureCollection(),
            ConnectionId = 1,
            Transport = serverConnection
        };
        var sut = new Http3Connection(connectionContext);

        // Token cancelled when settings frame is read
        await sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask), cts.Token);
        
        // Assert
        await readServerControlStream;
    }

    private static async Task<(QuicConnection quicClientConnection, QuicConnection quicServerConnection)> SetupConnectionAsync(CancellationTokenSource cts)
    {
        ValueTask<QuicConnection> quicServerConnecting = await CreateServerAsync(cts.Token);
        QuicConnection quicClientConnection = await ConnectClientAsync(cts);
        var quicServerConnection = await quicServerConnecting;
        return (quicClientConnection, quicServerConnection);
    }

    private static async Task<QuicConnection> ConnectClientAsync(CancellationTokenSource cts)
    {
        return await QuicConnection.ConnectAsync(new QuicClientConnectionOptions()
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 6000),
            DefaultCloseErrorCode = 0x0100,
            DefaultStreamErrorCode = 0x010C,
            MaxInboundUnidirectionalStreams = 1,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions() { RemoteCertificateValidationCallback = (_, _, _, _) => true, ApplicationProtocols = [new SslApplicationProtocol("h3"u8.ToArray())] }
        }, cts.Token);
    }

    private static async Task<ValueTask<QuicConnection>> CreateServerAsync(CancellationToken token)
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
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 6000),
            ListenBacklog = 1,
            ApplicationProtocols = [new SslApplicationProtocol("h3"u8.ToArray())],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(serverConnectionOptions)
        }, token);
        var quicServerConnecting = listener.AcceptConnectionAsync(token);
        return quicServerConnecting;
    }

    private static async Task AssertGoAwayAsync(PipeReader pipe, int expectedStreamId)
    {
        var readResult = await pipe.ReadAtLeastAsync(3, TestContext.Current.CancellationToken);
        var buffer = readResult.Buffer.ToArray();
        Assert.Equal(0x07, buffer[0]); // Frame Type: GOAWAY
        Assert.Equal(0x01, buffer[1]); // Length 
        Assert.Equal(expectedStreamId, buffer[2]); // Stream ID
        pipe.AdvanceTo(readResult.Buffer.GetPosition(3));
        readResult = await pipe.ReadAtLeastAsync(1, TestContext.Current.CancellationToken);
        Assert.True(readResult.IsCompleted);
    }

    private static async Task AssertReadSettigsAsync(PipeReader pipe, CancellationToken token)
    {
        var readResult = await pipe.ReadAtLeastAsync(5, token);
        Assert.False(readResult.IsCompleted);
        var buffer = readResult.Buffer.ToArray();
        Assert.Equal(0x00, buffer[0]); // Control Stream Type
        Assert.Equal(0x04, buffer[1]); // Frame Type: SETTINGS
        Assert.Equal(0x02, buffer[2]); // Length byte
        Assert.Equal(0x21, buffer[3]); // Reserved Id
        Assert.Equal(0x00, buffer[4]); // Value
        pipe.AdvanceTo(readResult.Buffer.GetPosition(5));
    }
}


