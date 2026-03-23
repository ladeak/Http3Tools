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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task InvalidFrameTypeOnControlStream_Aborts(int frameType)
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
        await WriteSettings(clientControlStream);

        // StreamType: 0-control, FrameType: 1-Invalid, Length: 0
        byte[] data = [0, (byte)frameType, 0];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);

        await readServerControlStream;
        await processing;
    }

    [Theory]
    [InlineData(1 * 31 + 33)]
    [InlineData(2 * 31 + 33)]
    [InlineData(0x0f0700)]
    public async Task UnknownFrameType_OnControlStream_ISgnored(int frameType)
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
        await WriteSettings(clientControlStream);

        // StreamType: 0-control, FrameType, Length: 0
        Span<byte> buffer = stackalloc byte[8];
        VariableLenghtIntegerDecoder.TryWrite(buffer, frameType, out var length);
        byte[] data = [0, .. buffer[..length], 0];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);

        await WriteGoAway(clientControlStream);
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

    [Fact]
    public async Task ChromiumProcessFrames()
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

        // Chromium control stream data: Settings Frame, Grease, Priorty Frames (Unknown)
        byte[] data = [0, 4, 27, 1, 128, 1, 0, 0, 6, 128, 4, 0, 0, 7, 64, 100, 51, 1, 192, 0, 0, 30, 67, 87, 179, 56, 154, 124, 38, 21, 192, 0, 0, 20, 86, 88, 150, 239, 2, 240, 76, 128, 15, 7, 0, 7, 0, 117, 61, 48, 44, 32, 105];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);

        await WriteGoAway(clientControlStream);
        await readServerControlStream;
        await processing;
        Assert.Equal(262144ul, sut.ClientMaxFieldSectionSize);
    }

    [Fact]
    public async Task FirefoxProcessFrames()
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

        // Chromium control stream data: Settings Frame, Grease, Priorty Frames (Unknown)
        byte[] data = [0, 4, 21, 1, 128, 1, 0, 0, 7, 20, 171, 96, 55, 66, 0, 128, 255, 210, 119, 1, 51, 1, 8, 1, 204, 152, 88, 54, 97, 42, 121, 101, 5, 199, 123, 140, 153, 87];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);

        await WriteGoAway(clientControlStream);
        await readServerControlStream;
        await processing;
        Assert.Null(sut.ClientMaxFieldSectionSize);
    }

    // TODO test headers from a browser

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

    private static async Task<IEnumerable<KeyValuePair<long, long>>> ReadSettigsAsync(Stream stream, CancellationToken token = default)
    {
        var headerBuffer = new byte[10];
        int readCount = await stream.ReadAtLeastAsync(headerBuffer, headerBuffer.Length, true, token);
        Assert.Equal(0x00, headerBuffer[0]); // Control Stream Type
        Assert.Equal(0x04, headerBuffer[1]); // Frame Type: SETTINGS
        if (!VariableLenghtIntegerDecoder.TryRead(headerBuffer[2..readCount], out var payloadLength, out var bytesCount))
            Assert.Fail();

        var payloadBuffer = new byte[payloadLength];
        headerBuffer.AsSpan()[(2 + bytesCount)..readCount].CopyTo(payloadBuffer);
        var remainingPayloadLength = readCount - 2 - bytesCount;
        await stream.ReadExactlyAsync(payloadBuffer.AsMemory(remainingPayloadLength));

        var data = payloadBuffer.AsSpan();
        List<KeyValuePair<long, long>> result = [];
        while (!data.IsEmpty)
        {
            if (!VariableLenghtIntegerDecoder.TryRead(data, out var settingIdentifier, out bytesCount))
                Assert.Fail();
            data = data.Slice(bytesCount);
            if (!VariableLenghtIntegerDecoder.TryRead(data, out var settingValue, out bytesCount))
                Assert.Fail();
            data = data.Slice(bytesCount);
            result.Add(new((long)settingIdentifier, (long)settingValue));
        }
        return result;
    }

    private static async Task WriteSettings(QuicStream clientControlStream)
    {
        // StreamType: 0-control, FrameType: 4-Settings, Length: 2, Setting Identifier: 33-Reserved, Value: 0
        byte[] data = [0, 4, 2, 33, 0];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);
    }

    private static async Task WriteGoAway(QuicStream clientControlStream)
    {
        // Write GOAWAY FrameType: 7, Length: 1, StreamId: 0
        byte[] data = [7, 1, 0];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);
    }
}

