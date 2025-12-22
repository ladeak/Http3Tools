using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

[CollectionDefinition(DisableParallelization = true)]
public class Http3ConnectionTests
{
    [Fact]
    public async Task ClientControlStreamAborts()
    {
        await using var fixture = await SetupConnectionAsync(TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(() => fixture.ClientConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, TestContext.Current.CancellationToken);

        // StreamType: 0-control, FrameType: 4-Settings, Length: 2, Setting Identifier: 6-SETTINGS_MAX_FIELD_SECTION_SIZE, Value: 3
        byte[] data = [0, 4, 2, 6, 3];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);
        clientControlStream.Abort(QuicAbortDirection.Both, 1);

        await readServerControlStream;
        await processing;
    }

    [Fact]
    public async Task ClientAbortsConnection() 
    {
        await using var fixture = await SetupConnectionAsync(TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(() => fixture.ClientConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, TestContext.Current.CancellationToken);

        // StreamType: 0-control, FrameType: 4-Settings, Length: 2, Setting Identifier: 6-SETTINGS_MAX_FIELD_SECTION_SIZE, Value: 3
        byte[] data = [0, 4, 2, 6, 3];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);
        await fixture.ClientConnection.CloseAsync(ErrorCodes.H3NoError, TestContext.Current.CancellationToken);

        await readServerControlStream;
        await processing;
    }

    [Fact]
    public async Task InvalidFrameTypeOnControlStream_Aborts()
    {
        await using var fixture = await SetupConnectionAsync(TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(async () =>
        {
            var controlStream = await fixture.ClientConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken);
            await AssertReadSettigsAsync(controlStream);
            await AssertGoAwayAsync(controlStream, 2);
        }, TestContext.Current.CancellationToken);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, TestContext.Current.CancellationToken);

        // StreamType: 0-control, FrameType: 4-Headers, Length: 0
        byte[] data = [0, 1, 0];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);

        await readServerControlStream;
        await processing;
    }

    [Fact]
    public async Task InvalidStreamType_AbortsConnection()
    {
        CancellationTokenSource cts = new();
        TestContext.Current.CancellationToken.Register(() => cts.Cancel());
        await using var fixture = await SetupConnectionAsync(cts.Token);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask), cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(() => fixture.ClientConnection.AcceptInboundStreamAsync(cts.Token), cts.Token);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cts.Token);

        byte[] data = [4];
        await clientControlStream.WriteAsync(data, cts.Token);
        await clientControlStream.FlushAsync(cts.Token);
        await Assert.ThrowsAsync<QuicException>(() => clientControlStream.WritesClosed);
        cts.Cancel();
        await processing;
    }

    [Fact]
    public async Task ClientSendsMaxHeaderSize_PartialDataSegments_FeatureParses()
    {
        await using var fixture = await SetupConnectionAsync(TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(() => fixture.ClientConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, TestContext.Current.CancellationToken);

        // StreamType: 0-control, FrameType: 4-Settings, Length: 2, Setting Identifier: 6-SETTINGS_MAX_FIELD_SECTION_SIZE, Value: 3
        byte[] data = [0, 4];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);

        data = [2, 6];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);

        data = [3];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);

        // Write GOAWAY FrameType: 7, Length: 1, StreamId: 0
        data = [7];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);

        data = [1, 0];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);

        await processing;

        Assert.Equal(3ul, sut.ClientMaxFieldSectionSize);
    }

    [Fact]
    public async Task ClientSendsMaxHeaderSize_FeatureParses()
    {
        await using var fixture = await SetupConnectionAsync(TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(() => fixture.ClientConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, TestContext.Current.CancellationToken);

        // StreamType: 0-control, FrameType: 4-Settings, Length: 2, Setting Identifier: 6-SETTINGS_MAX_FIELD_SECTION_SIZE, Value: 3
        byte[] data = [0, 4, 2, 6, 3];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);

        // Write GOAWAY FrameType: 7, Length: 1, StreamId: 0
        data = [7, 1, 0];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);

        await processing;

        Assert.Equal(3ul, sut.ClientMaxFieldSectionSize);
    }

    [Fact]
    public async Task Customized_SettingsFrame_Received()
    {
        const int maxRequestHeaderLength = 32656;
        var cts = new CancellationTokenSource();
        TestContext.Current.CancellationToken.Register(() => cts.Cancel());
        await using var fixture = await SetupConnectionAsync(cts.Token);
        var readServerControlStream = Task.Run(async () =>
        {
            var inboundStream = await fixture.ClientConnection.AcceptInboundStreamAsync(cts.Token);
            await AssertReadSettigsAsync(inboundStream, 6, maxRequestHeaderLength, cts.Token);
            cts.Cancel();
            await AssertGoAwayAsync(inboundStream, 0);
        }, cts.Token);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection, new() { Http3MaxRequestHeaderLength = maxRequestHeaderLength });

        // Token cancelled when settings frame is read
        await sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask), cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await readServerControlStream;
    }

    [Fact]
    public async Task Default_SettingsFrame_Received()
    {
        var cts = new CancellationTokenSource();
        TestContext.Current.CancellationToken.Register(() => cts.Cancel());
        await using var fixture = await SetupConnectionAsync(cts.Token);
        var readServerControlStream = Task.Run(async () =>
        {
            var inboundStream = await fixture.ClientConnection.AcceptInboundStreamAsync(cts.Token);
            await AssertReadSettigsAsync(inboundStream, token: cts.Token);
            cts.Cancel();
            await AssertGoAwayAsync(inboundStream, 0);
        }, cts.Token);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);

        // Token cancelled when settings frame is read
        await sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask), cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await readServerControlStream;
    }

    private static Http3Connection CreateHttp3Connection(QuicConnection serverConnection, CHttpServerOptions? options = null)
    {
        var connectionContext = new CHttp3ConnectionContext()
        {
            ServerOptions = options ?? new(),
            Features = new FeatureCollection(),
            ConnectionId = 1,
            Transport = serverConnection
        };
        var sut = new Http3Connection(connectionContext);
        return sut;
    }

    private static async Task<ClientServerConnection> SetupConnectionAsync(CancellationToken token)
    {
        (ValueTask<QuicConnection> quicServerConnecting, QuicListener listener) = await CreateServerAsync(token);
        var quicClientConnection = await ConnectClientAsync(token);
        var quicServerConnection = await quicServerConnecting;
        return new ClientServerConnection(quicClientConnection, quicServerConnection, listener);
    }

    private static async Task<QuicConnection> ConnectClientAsync(CancellationToken token)
    {
        return await QuicConnection.ConnectAsync(new QuicClientConnectionOptions()
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 6000),
            DefaultCloseErrorCode = 0x0100,
            DefaultStreamErrorCode = 0x010C,
            MaxInboundUnidirectionalStreams = 1,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions() { RemoteCertificateValidationCallback = (_, _, _, _) => true, ApplicationProtocols = [new SslApplicationProtocol("h3"u8.ToArray())] }
        }, token);
    }

    private static async Task<(ValueTask<QuicConnection>, QuicListener)> CreateServerAsync(CancellationToken token)
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
        return (quicServerConnecting, listener);
    }

    private static async Task AssertGoAwayAsync(Stream stream, int expectedStreamId)
    {
        var buffer = new byte[3];
        var readResult = await stream.ReadAtLeastAsync(buffer, buffer.Length, true, TestContext.Current.CancellationToken);
        Assert.Equal(0x07, buffer[0]); // Frame Type: GOAWAY
        Assert.Equal(0x01, buffer[1]); // Length 
        Assert.Equal(expectedStreamId, buffer[2]); // Stream ID
        var end = await stream.ReadAsync(Memory<byte>.Empty, TestContext.Current.CancellationToken);
        Assert.Equal(0, end);
    }

    private static async Task AssertReadSettigsAsync(Stream stream, byte expectedSettingId = 33, int expectedValue = 0, CancellationToken token = default)
    {
        var encodedValue = new byte[8];
        VariableLenghtIntegerDecoder.TryWrite(encodedValue, (ulong)expectedValue, out int valueBytesWritten);
        var buffer = new byte[4 + valueBytesWritten];
        await stream.ReadAtLeastAsync(buffer, buffer.Length, true, token);
        Assert.Equal(0x00, buffer[0]); // Control Stream Type
        Assert.Equal(0x04, buffer[1]); // Frame Type: SETTINGS
        Assert.Equal(1 + valueBytesWritten, buffer[2]); // Length byte
        Assert.Equal(expectedSettingId, buffer[3]); // Reserved Id
        encodedValue.SequenceEqual(buffer[4..]);
    }

    private record class ClientServerConnection(QuicConnection ClientConnection, QuicConnection ServerConnection, QuicListener Listener) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await ClientConnection.DisposeAsync();
            await ServerConnection.DisposeAsync();
            await Listener.DisposeAsync();
        }
    }

}


