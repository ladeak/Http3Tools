using System.Net.Quic;
using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

[CollectionDefinition(DisableParallelization = true)]
public class Http3ConnectionTests
{
    private const int Port = 6000;

    [Fact]
    public async Task StopAsync_WithOpenRequestStream_ForcedCancelling_ClosesConnection()
    {
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(() => fixture.ClientConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, TestContext.Current.CancellationToken);
        var clientDataStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        await WriteSettings(clientControlStream);

        // FrameType: 1-Headers, Length: 2
        byte[] data = [1, 2, 0];
        await clientDataStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientDataStream.FlushAsync(TestContext.Current.CancellationToken);

        // Let time for the data stream to start processing
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Stop should trigger abortion, that will abort all streams.
        await sut.StopAsync(new CancellationToken(true));

        await processing;
        await Assert.ThrowsAsync<QuicException>(
            async () => await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken),
            ex => ex.QuicError == QuicError.ConnectionAborted ? null : ex.Message);
        await readServerControlStream;
    }

    [Fact]
    public async Task StopAsyncForcedCancelling_ClosesConnection()
    {
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(() => fixture.ClientConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, TestContext.Current.CancellationToken);
        await WriteSettings(clientControlStream);

        await sut.StopAsync(new CancellationToken(true));

        await processing;
        await Assert.ThrowsAsync<QuicException>(
            async () => await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken),
            ex => ex.QuicError == QuicError.ConnectionAborted ? null : ex.Message);
        await readServerControlStream;
    }

    [Fact]
    public async Task StopAsync_ClosesConnection()
    {
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(() => fixture.ClientConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, TestContext.Current.CancellationToken);
        await WriteSettings(clientControlStream);

        await sut.StopAsync(TestContext.Current.CancellationToken);

        await processing;
        await Assert.ThrowsAsync<QuicException>(
            async () => await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken),
            ex => ex.QuicError == QuicError.ConnectionAborted ? null : ex.Message);
        await readServerControlStream;
    }

    [Fact]
    public async Task ClientControlStreamAborts()
    {
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(() => fixture.ClientConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, TestContext.Current.CancellationToken);
        await WriteSettings(clientControlStream);
        clientControlStream.Abort(QuicAbortDirection.Both, 1);

        await readServerControlStream;
        await processing;
    }

    [Fact]
    public async Task ClientAbortsConnection()
    {
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var readServerControlStream = Task.Run(() => fixture.ClientConnection.AcceptInboundStreamAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var clientControlStream = await fixture.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, TestContext.Current.CancellationToken);
        await WriteSettings(clientControlStream);
        await fixture.ClientConnection.CloseAsync(ErrorCodes.H3NoError, TestContext.Current.CancellationToken);

        await readServerControlStream;
        await processing;
    }

    [Fact]
    public async Task InvalidFrameTypeOnControlStream_Aborts()
    {
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
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
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, cts.Token);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection, connectionCancellation: cts);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
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
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
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
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection);
        var processing = sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
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
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, cts.Token);
        var readServerControlStream = Task.Run(async () =>
        {
            var inboundStream = await fixture.ClientConnection.AcceptInboundStreamAsync(cts.Token);
            await AssertReadSettigsAsync(inboundStream, 6, maxRequestHeaderLength, cts.Token);
            cts.Cancel();
            await AssertGoAwayAsync(inboundStream, 0);
        }, cts.Token);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection, new() { Http3MaxRequestHeaderLength = maxRequestHeaderLength }, connectionCancellation: cts);

        // Token cancelled when settings frame is read
        await sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await readServerControlStream;
    }

    [Fact]
    public async Task Default_SettingsFrame_Received()
    {
        var cts = new CancellationTokenSource();
        TestContext.Current.CancellationToken.Register(() => cts.Cancel());
        await using var fixture = await QuicConnectionFixture.SetupConnectionAsync(Port, cts.Token);
        var readServerControlStream = Task.Run(async () =>
        {
            var inboundStream = await fixture.ClientConnection.AcceptInboundStreamAsync(cts.Token);
            await AssertReadSettigsAsync(inboundStream, token: cts.Token);
            cts.Cancel();
            await AssertGoAwayAsync(inboundStream, 0);
        }, cts.Token);
        Http3Connection sut = CreateHttp3Connection(fixture.ServerConnection, connectionCancellation: cts);

        // Token cancelled when settings frame is read
        await sut.ProcessConnectionAsync(new TestBase.TestApplication(_ => Task.CompletedTask))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await readServerControlStream;
    }

    private static Http3Connection CreateHttp3Connection(QuicConnection serverConnection, CHttpServerOptions? options = null, CancellationTokenSource? connectionCancellation = null)
    {
        var connectionContext = new CHttp3ConnectionContext()
        {
            ServerOptions = options ?? new(),
            Features = new FeatureCollection(),
            ConnectionId = 1,
            Transport = serverConnection,
            ConnectionCancellation = connectionCancellation ?? new()
        };
        var sut = new Http3Connection(connectionContext);
        return sut;
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

    private static async Task WriteSettings(QuicStream clientControlStream)
    {
        // StreamType: 0-control, FrameType: 4-Settings, Length: 2, Setting Identifier: 33-Reserved, Value: 0
        byte[] data = [0, 4, 2, 33, 0];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);
    }
}

