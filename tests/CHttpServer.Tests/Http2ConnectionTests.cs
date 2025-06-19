using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection.PortableExecutable;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using static CHttpServer.Tests.TestBase;

namespace CHttpServer.Tests;

[SuppressMessage("Usage", "xUnit1051:Calls to methods which accept CancellationToken should use TestContext.Current.CancellationToken")]
public class Http2ConnectionTests
{
    [Fact]
    public async Task New_ConnectionReturns_Settings_WindowUpdate()
    {
        var features = new FeatureCollection();
        features.Add<ITlsHandshakeFeature>(new TestTls());
        var pipe = new TestDuplexPipe();
        var connectionContext = new CHttpConnectionContext()
        {
            ConnectionId = 1,
            Features = features,
            ServerOptions = new CHttpServerOptions(),
            Transport = pipe.Input.AsStream(),
            TransportPipe = pipe
        };
        var connection = new Http2Connection(connectionContext) { ResponseWriter = new Http2ResponseWriter(new FrameWriter(connectionContext), 1000) };

        // Initiate connection
        var client = new TestClient(pipe.RequestWriter);
        var connectionTask = connection.ProcessRequestAsync(new TestApplication(_ => Task.CompletedTask));

        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe, 65535u * 10);

        // Shutdown connection
        await client.ShutdownConnectionAsync();

        // Await shutdown
        await connectionTask;
    }

    [Fact]
    public async Task OpenConnection_Waits_Preface()
    {
        var features = new FeatureCollection();
        features.Add<ITlsHandshakeFeature>(new TestTls());
        var pipe = new TestDuplexPipe();
        var connectionContext = new CHttpConnectionContext()
        {
            ConnectionId = 1,
            Features = features,
            ServerOptions = new CHttpServerOptions(),
            Transport = pipe.Input.AsStream(),
            TransportPipe = pipe
        };
        var connection = new Http2Connection(connectionContext) { ResponseWriter = new Http2ResponseWriter(new FrameWriter(connectionContext), 1000) };

        // Initiate connection
        var client = new TestClient(pipe.RequestWriter, false);
        var connectionTask = connection.ProcessRequestAsync(new TestApplication(_ => Task.CompletedTask));

        Assert.False(pipe.ResponseReader.TryRead(out var _));
        await client.SendPrefaceAsync();

        Http2Frame frame = await AssertSettingsFrameAsync(pipe);
        await client.ShutdownConnectionAsync();
        await connectionTask;
    }

    [Fact]
    public async Task Invalid_Preface_ClosesConnection()
    {
        var features = new FeatureCollection();
        features.Add<ITlsHandshakeFeature>(new TestTls());
        var pipe = new TestDuplexPipe();
        var connectionContext = new CHttpConnectionContext()
        {
            ConnectionId = 1,
            Features = features,
            ServerOptions = new CHttpServerOptions(),
            Transport = pipe.Input.AsStream(),
            TransportPipe = pipe
        };
        var connection = new Http2Connection(connectionContext) { ResponseWriter = new Http2ResponseWriter(new FrameWriter(connectionContext), 1000) };

        // Initiate connection
        var client = new TestClient(pipe.RequestWriter, false);
        var connectionTask = connection.ProcessRequestAsync(new TestApplication(_ => Task.CompletedTask));
        await client.SendPrefaceAsync("INV * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8);
        await connectionTask;
        await AssertGoAwayAsync(pipe, 0, Http2ErrorCode.CONNECT_ERROR);
    }

    [Fact]
    public async Task TooShort_Preface_ClosesConnection()
    {
        var features = new FeatureCollection();
        features.Add<ITlsHandshakeFeature>(new TestTls());
        var heartbeater = new TestHeartbeat();
        features.Add<IConnectionHeartbeatFeature>(heartbeater);
        var pipe = new TestDuplexPipe();
        var connectionContext = new CHttpConnectionContext()
        {
            ConnectionId = 1,
            Features = features,
            ServerOptions = new CHttpServerOptions(),
            Transport = pipe.Input.AsStream(),
            TransportPipe = pipe
        };
        var connection = new Http2Connection(connectionContext) { ResponseWriter = new Http2ResponseWriter(new FrameWriter(connectionContext), 1000) };

        // Initiate connection
        var client = new TestClient(pipe.RequestWriter, false);
        var connectionTask = connection.ProcessRequestAsync(new TestApplication(_ => Task.CompletedTask));
        await client.SendPrefaceAsync("INV * HTTP/2.0\r\n\r\nSM\r\n\r"u8);
        heartbeater.TriggerHeartbeat();
        await connectionTask;
        await AssertGoAwayAsync(pipe, 0, Http2ErrorCode.CONNECT_ERROR);
    }

    [Fact]
    public async Task SendingHeaders()
    {
        var (pipe, connection) = CreateConnnection();

        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, _ => Task.CompletedTask);
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([], true);

        // Assert response
        var (frame, headers) = await AssertResponseHeaders(pipe);
        Assert.True(headers.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.NoContent).ToString());
        Assert.True(frame.EndHeaders);
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task RstStream_CancelsRequest()
    {
        var (pipe, connection) = CreateConnnection();

        // Initiate connection
        TaskCompletionSource<bool> cancellationProcessed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var (client, connectionProcessing) = CreateApp(pipe, connection, async (HttpContext ctx) => { try { await Task.Delay(TimeSpan.FromSeconds(5), ctx.RequestAborted); } finally { cancellationProcessed.TrySetResult(ctx.RequestAborted.IsCancellationRequested); } });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([], true);
        await client.SendRstStreamAsync();

        await cancellationProcessed.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task RstStream_NonCooperativeApplication_DoesNotFailWriteAsyncInApplication_DoesNotWriteResponseToStream()
    {
        var (pipe, connection) = CreateConnnection();

        // Initiate connection
        TaskCompletionSource requestCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var (client, connectionProcessing) = CreateApp(pipe, connection, async (HttpContext ctx) =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1000));
                await ctx.Response.WriteAsync("this is not a cooperative application");
            }
            finally { requestCompleted.TrySetResult(); }
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([], true);
        await client.SendRstStreamAsync();

        await requestCompleted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task SmallRequestHeader()
    {
        var headerName = "x-test-header";
        var headerValue = new string('a', 100);
        var (pipe, connection) = CreateConnnection();

        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            Assert.Contains(ctx.Request.Headers, h => h.Key == headerName && (string)h.Value! == headerValue);
            return Task.CompletedTask;
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([new(headerName, headerValue)], true);

        var (frame, responseHeaders) = await AssertResponseHeaders(pipe);
        Assert.True(responseHeaders.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.NoContent).ToString());
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task ManySmallRequestHeader_UsesContinuation()
    {
        var headerName = "x-test-header";
        var headerValue = new string('a', 100);
        var (pipe, connection) = CreateConnnection();

        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            Assert.Contains(ctx.Request.Headers, h => h.Key == headerName && (string)h.Value! == headerValue);
            for (int i = 0; i < 10; i++)
                Assert.Contains(ctx.Request.Headers, h => h.Key == $"{headerName}-{i}" && (string)h.Value! == headerValue);
            return Task.CompletedTask;
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([new(headerName, headerValue)], endHeaders: false, endStream: false);
        for (int i = 0; i < 10; i++)
            await client.SendContinuationAsync([new($"{headerName}-{i}", headerValue)], endHeaders: i == 9, endStream: i == 9);

        var (frame, responseHeaders) = await AssertResponseHeaders(pipe);
        Assert.True(responseHeaders.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.NoContent).ToString());
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task LargeRequestHeaderValue_UsesContinuation()
    {
        var headerName = "x-test-header";
        var headerValue = new string('a', 100_000);
        var (pipe, connection) = CreateConnnection(new CHttpServerOptions() { MaxRequestHeaderLength = 100_001 });

        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            Assert.Contains(ctx.Request.Headers, h => h.Key == headerName && (string)h.Value! == headerValue);
            return Task.CompletedTask;
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([new(headerName, headerValue)], endHeaders: true, endStream: true);

        var (frame, responseHeaders) = await AssertResponseHeaders(pipe);
        Assert.True(responseHeaders.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.NoContent).ToString());
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task LargeRequestHeaderName_UsesContinuation()
    {
        var headerName = $"x-test-header-{new string('a', 100_000)}";
        var headerValue = "true";
        var (pipe, connection) = CreateConnnection(new CHttpServerOptions() { MaxRequestHeaderLength = 100_100 });

        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            Assert.Contains(ctx.Request.Headers, h => h.Key == headerName);
            return Task.CompletedTask;
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([new(headerName, headerValue)], endHeaders: true, endStream: true);

        var (frame, responseHeaders) = await AssertResponseHeaders(pipe);
        Assert.True(responseHeaders.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.NoContent).ToString());
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task Too_LargeRequestHeaderValue_GoAway()
    {
        var headerName = "x-test-header";
        var headerValue = new string('a', 100_000);
        var (pipe, connection) = CreateConnnection(new CHttpServerOptions() { MaxRequestHeaderLength = 90_000 });

        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            Assert.Contains(ctx.Request.Headers, h => h.Key == headerName && (string)h.Value! == headerValue);
            return Task.CompletedTask;
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([new(headerName, headerValue)], endHeaders: true, endStream: true);

        await AssertGoAwayAsync(pipe);
        
        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await connectionProcessing;
    }

    [Fact]
    public async Task Too_LargeRequestHeaderName_GoAway()
    {
        var headerName = $"x-test-header-{new string('a', 100_000)}";
        var headerValue = "true";
        var (pipe, connection) = CreateConnnection(new CHttpServerOptions() { MaxRequestHeaderLength = 90_000 });

        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            Assert.Contains(ctx.Request.Headers, h => h.Key == headerName);
            return Task.CompletedTask;
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([new(headerName, headerValue)], endHeaders: true, endStream: true);

        await AssertGoAwayAsync(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await connectionProcessing;
    }

    private static async Task<Http2Frame> AssertEmptyEndStream(TestDuplexPipe pipe)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.DATA, frame.Type);
        Assert.Equal(0L, frame.PayloadLength);
        Assert.True(frame.EndStream);
        return frame;
    }

    private static async Task<(Http2Frame Frame, Dictionary<string, string> Header)> AssertResponseHeaders(TestDuplexPipe pipe)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.HEADERS, frame.Type);
        var payloadData = new byte[frame.PayloadLength];
        await pipe.Response.ReadExactlyAsync(payloadData).AsTask().WaitAsync(TestContext.Current.CancellationToken);
        var hpackDecoder = new HPackDecoder();
        var headerHandler = new TestHttpStreamHeadersHandler();
        hpackDecoder.Decode(payloadData, frame.EndHeaders, headerHandler);
        return (frame, headerHandler.Headers);
    }

    private static async Task<Http2Frame> AssertWindowUpdateFrameAsync(TestDuplexPipe pipe, uint? expectedSize = null)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.WINDOW_UPDATE, frame.Type);
        var payloadData = new byte[4];
        await pipe.Response.ReadExactlyAsync(payloadData).AsTask().WaitAsync(TestContext.Current.CancellationToken);
        var size = IntegerSerializer.ReadUInt32BigEndian(payloadData);
        if (expectedSize.HasValue)
            Assert.Equal(expectedSize, size);
        return frame;
    }

    private static async Task<Http2Frame> AssertGoAwayAsync(TestDuplexPipe pipe, uint? expectedLastStreamId = null, Http2ErrorCode? expectedErrorCode = null)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.GOAWAY, frame.Type);
        var payloadData = new byte[frame.PayloadLength];
        await pipe.Response.ReadExactlyAsync(payloadData).AsTask().WaitAsync(TestContext.Current.CancellationToken);
        var lastStreamId = IntegerSerializer.ReadUInt32BigEndian(payloadData);
        if (expectedLastStreamId.HasValue)
            Assert.Equal(expectedLastStreamId.Value, lastStreamId);
        var errorCode = IntegerSerializer.ReadUInt32BigEndian(payloadData[4..]);
        if (expectedErrorCode.HasValue)
            Assert.Equal((uint)expectedErrorCode.Value, errorCode);
        return frame;
    }

    private static async Task<Http2Frame> AssertSettingsFrameAsync(TestDuplexPipe pipe)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.SETTINGS, frame.Type);
        var payloadData = new byte[frame.PayloadLength];
        await pipe.Response.ReadExactlyAsync(payloadData).AsTask().WaitAsync(TestContext.Current.CancellationToken);
        var payload = payloadData.AsSpan();

        // HeaderTableSize
        Assert.Equal(1, IntegerSerializer.ReadUInt16BigEndian(payload[..2]));
        Assert.Equal(0L, IntegerSerializer.ReadUInt32BigEndian(payload[2..6]));
        payload = payload[6..];

        // EnablePush
        Assert.Equal(2, IntegerSerializer.ReadUInt16BigEndian(payload[..2]));
        Assert.Equal(0L, IntegerSerializer.ReadUInt32BigEndian(payload[2..6]));
        payload = payload[6..];

        // MaxConcurrentStream
        Assert.Equal(3, IntegerSerializer.ReadUInt16BigEndian(payload[..2]));
        Assert.Equal(100L, IntegerSerializer.ReadUInt32BigEndian(payload[2..6]));
        payload = payload[6..];

        // InitialWindowSize
        Assert.Equal(4, IntegerSerializer.ReadUInt16BigEndian(payload[..2]));
        Assert.Equal(65_535L, IntegerSerializer.ReadUInt32BigEndian(payload[2..6]));
        payload = payload[6..];

        // ReceiveMaxFrameSize
        Assert.Equal(5, IntegerSerializer.ReadUInt16BigEndian(payload[..2]));
        Assert.Equal(32_768L, IntegerSerializer.ReadUInt32BigEndian(payload[2..6]));

        return frame;
    }
}
