using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
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
        var connectionContext = new CHttp2ConnectionContext()
        {
            ConnectionId = 1,
            Features = features,
            ServerOptions = new CHttpServerOptions(),
            Transport = pipe.Input.AsStream(),
            TransportPipe = pipe,
            ConnectionCancellation = new()
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
        var connectionContext = new CHttp2ConnectionContext()
        {
            ConnectionId = 1,
            Features = features,
            ServerOptions = new CHttpServerOptions(),
            Transport = pipe.Input.AsStream(),
            TransportPipe = pipe,
            ConnectionCancellation = new()
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
        var connectionContext = new CHttp2ConnectionContext()
        {
            ConnectionId = 1,
            Features = features,
            ServerOptions = new CHttpServerOptions(),
            Transport = pipe.Input.AsStream(),
            TransportPipe = pipe,
            ConnectionCancellation = new()
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
        var connectionContext = new CHttp2ConnectionContext()
        {
            ConnectionId = 1,
            Features = features,
            ServerOptions = new CHttpServerOptions(),
            Transport = pipe.Input.AsStream(),
            TransportPipe = pipe,
            ConnectionCancellation = new()
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
        Assert.True(headers.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.OK).ToString());
        Assert.True(headers.TryGetValue("server", out var server) && server == "CHttp");
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
        var (client, connectionProcessing) = CreateApp(pipe, connection, async (HttpContext ctx) => { try { await Task.Delay(TimeSpan.FromSeconds(5), ctx.RequestAborted); } catch (TaskCanceledException) { } finally { cancellationProcessed.TrySetResult(ctx.RequestAborted.IsCancellationRequested); } });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([], true);
        await client.SendRstStreamAsync();

        await cancellationProcessed.Task.WaitAsync(TestContext.Current.CancellationToken);
        await AssertRstStreamAsync(pipe);

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
        await AssertRstStreamAsync(pipe);

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
        Assert.True(responseHeaders.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.OK).ToString());
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
        Assert.True(responseHeaders.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.OK).ToString());
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
        var (pipe, connection) = CreateConnnection(new CHttpServerOptions() { Http2MaxRequestHeaderLength = 100_001 });

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
        Assert.True(responseHeaders.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.OK).ToString());
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
        var (pipe, connection) = CreateConnnection(new CHttpServerOptions() { Http2MaxRequestHeaderLength = 100_100 });

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
        Assert.True(responseHeaders.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.OK).ToString());
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
        var (pipe, connection) = CreateConnnection(new CHttpServerOptions() { Http2MaxRequestHeaderLength = 90_000 });

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
        var (pipe, connection) = CreateConnnection(new CHttpServerOptions() { Http2MaxRequestHeaderLength = 90_000 });

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

    [Fact]
    public async Task LargeResponseHeaderValue_UsesContinuation()
    {
        var headerName = "x-test-header";
        var headerValue = new string('a', 100_000);
        var (pipe, connection) = CreateConnnection(new CHttpServerOptions() { Http2MaxRequestHeaderLength = 100_001 });

        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            ctx.Response.Headers[headerName] = headerValue;
            return Task.CompletedTask;
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([], endHeaders: true, endStream: true);

        var (frame, responseHeaders) = await AssertResponseHeadersAndContinuations(pipe, 100_000);
        Assert.True(responseHeaders.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.OK).ToString());
        Assert.True(responseHeaders.TryGetValue(headerName, out var responseValue) && responseValue == headerValue);
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task LargeResponseHeaderName_UsesContinuation()
    {
        var headerName = $"x-test-header-{new string('a', 100_000)}";
        var headerValue = "true";
        var (pipe, connection) = CreateConnnection(new CHttpServerOptions() { Http2MaxRequestHeaderLength = 100_100 });

        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            ctx.Response.Headers[headerName] = headerValue;
            return Task.CompletedTask;
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send request
        await client.SendHeadersAsync([new(headerName, headerValue)], endHeaders: true, endStream: true);

        var (frame, responseHeaders) = await AssertResponseHeadersAndContinuations(pipe, 100_014);
        Assert.True(responseHeaders.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.OK).ToString());
        Assert.True(responseHeaders.TryGetValue(headerName, out _));
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task PingTest()
    {
        var (pipe, connection) = CreateConnnection();

        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            return Task.CompletedTask;
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);

        // Send PING
        await client.SendPingAsync();
        await AssertPingAckAsync(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 0, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task UsePriorty_Sends_NoRfc7540Priorities()
    {
        var (pipe, connection) = CreateConnnection(new() { UsePriority = true });
        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            Assert.True(ctx.Request.Headers.TryGetValue("priority", out var values));
#pragma warning disable xUnit2017
            Assert.True(values.Contains("u=2, i"));
#pragma warning restore xUnit2017
            return Task.CompletedTask;

        });
        await AssertSettingsFrameAsync(pipe, withNoRfc7540Priorities: true);
        await AssertWindowUpdateFrameAsync(pipe);

        var requestHeaders = new RequestHeaderCollection();
        requestHeaders.Add("priority", "u=2, i");
        await client.SendHeadersAsync(requestHeaders);

        // Assert response
        var (frame, headers) = await AssertResponseHeaders(pipe);
        Assert.True(headers.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.OK).ToString());
        Assert.False(headers.TryGetValue("priority", out var responsePriority)); // No change
        Assert.True(frame.EndHeaders);
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Theory]
    [InlineData("u=1,i", 1, true)]
    [InlineData("u=1, i", 1, true)]
    [InlineData("u=2, i=1", 2, true)]
    [InlineData("u=4, i=0", 4, false)]
    [InlineData("u=5", 5, false)]
    [InlineData("i", 3, true)]
    [InlineData("u=9, i=2", 3, false)]
    [InlineData("invalid", 3, false)]
    [InlineData("u=7, i=1;u=4, i=0", 7, false)]
    [InlineData("u=9, i=2;u=0, i", 3, true)]
    [InlineData("u=2,i=1 ", 2, true)]
    [InlineData("i=1 ,u=2 ", 2, true)]
    [InlineData("ii=1 ,uu=2 ", 3, false)]
    public async Task UsePriorty_Sends_RequestUsesPriorityFeature(string header, byte urgency, bool incremental)
    {
        var (pipe, connection) = CreateConnnection(new() { UsePriority = true });
        // Initiate connection
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            var priority = ctx.Priority();
            Assert.Equal(urgency, priority.Urgency);
            Assert.Equal(incremental, priority.Incremental);
            return Task.CompletedTask;

        });
        await AssertSettingsFrameAsync(pipe, withNoRfc7540Priorities: true);
        await AssertWindowUpdateFrameAsync(pipe);

        var requestHeaders = new RequestHeaderCollection();
        requestHeaders.Add("priority", header);
        await client.SendHeadersAsync(requestHeaders);

        // Assert response
        var (frame, headers) = await AssertResponseHeaders(pipe);
        Assert.True(headers.TryGetValue(":status", out var status) && status == ((int)HttpStatusCode.OK).ToString());
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Theory]
    [InlineData(1, true, "u=1,i")]
    [InlineData(2, false, "u=2")]
    public async Task UsePriorty_Response_AddsPriorityHeader(byte urgency, bool incremental, string expectedHeader)
    {
        var (pipe, connection) = CreateConnnection(new() { UsePriority = true });
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            ctx.SetPriority(new Priority9218(urgency, incremental));
            return Task.CompletedTask;

        });
        await AssertSettingsFrameAsync(pipe, withNoRfc7540Priorities: true);
        await AssertWindowUpdateFrameAsync(pipe);
        await client.SendHeadersAsync([]);

        // Assert response
        var (frame, headers) = await AssertResponseHeaders(pipe);
        Assert.True(headers.TryGetValue("priority", out var responsePriority) && responsePriority == expectedHeader);
        Assert.True(frame.EndHeaders);
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task NoUsePriorty_Response_Throws()
    {
        bool withPriority = false;
        var (pipe, connection) = CreateConnnection(new() { UsePriority = withPriority });
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            var priorityFeature = ctx.Features.Get<IPriority9218Feature>();
            Assert.NotNull(priorityFeature);
            Assert.Throws<InvalidOperationException>(() => priorityFeature.SetPriority(new Priority9218(2, true)));
            return Task.CompletedTask;

        });
        await AssertSettingsFrameAsync(pipe, withNoRfc7540Priorities: withPriority);
        await AssertWindowUpdateFrameAsync(pipe);
        await client.SendHeadersAsync([]);

        // Assert response
        var (frame, headers) = await AssertResponseHeaders(pipe);
        Assert.True(frame.EndHeaders);
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task ResponseStarted_SetPriorty_Throws()
    {
        bool withPriority = true;
        var (pipe, connection) = CreateConnnection(new() { UsePriority = withPriority });
        var (client, connectionProcessing) = CreateApp(pipe, connection, async (HttpContext ctx) =>
        {
            await ctx.Response.WriteAsync("response");
            var priorityFeature = ctx.Features.Get<IPriority9218Feature>();
            Assert.NotNull(priorityFeature);
            Assert.Throws<InvalidOperationException>(() => priorityFeature.SetPriority(new Priority9218(2, true)));
        });
        await AssertSettingsFrameAsync(pipe, withNoRfc7540Priorities: withPriority);
        await AssertWindowUpdateFrameAsync(pipe);
        await client.SendHeadersAsync([]);

        // Assert response
        var (frame, headers) = await AssertResponseHeaders(pipe);
        Assert.True(frame.EndHeaders);

        await AssertDataStream(pipe, new byte[8]);
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task DataFrames_Written()
    {
        string responseContent = "response";
        var (pipe, connection) = CreateConnnection();
        var (client, connectionProcessing) = CreateApp(pipe, connection, async (HttpContext ctx) =>
        {
            await ctx.Response.WriteAsync(responseContent);
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);
        await client.SendHeadersAsync([]);

        // Assert response
        var (frame, headers) = await AssertResponseHeaders(pipe);
        Assert.True(frame.EndHeaders);

        var data = new byte[8];
        await AssertDataStream(pipe, data);
        Assert.Equal(responseContent, Encoding.UTF8.GetString(data));
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task ExceptionWhenStreaming_RstStream()
    {
        string responseContent = "response";
        var (pipe, connection) = CreateConnnection();
        var (client, connectionProcessing) = CreateApp(pipe, connection, async (HttpContext ctx) =>
        {
            await ctx.Response.WriteAsync(responseContent);
            throw new InvalidOperationException("Test exception");
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);
        await client.SendHeadersAsync([]);

        // Assert response
        var (frame, headers) = await AssertResponseHeaders(pipe);
        Assert.True(frame.EndHeaders);

        frame = await ReadAllDataStream(pipe);
        await AssertRstStreamFrameAsync(frame, pipe, Http2ErrorCode.INTERNAL_ERROR);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
        await connectionProcessing;
    }

    [Fact]
    public async Task InputReaderClose_ShutsdownConnection()
    {
        string responseContent = "response";
        var (pipe, connection) = CreateConnnection();
        var (client, connectionProcessing) = CreateApp(pipe, connection, async (HttpContext ctx) =>
        {
            await ctx.Response.WriteAsync(responseContent);
        });
        await AssertSettingsFrameAsync(pipe);
        await AssertWindowUpdateFrameAsync(pipe);
        await client.SendHeadersAsync([]);

        // Assert response
        var (frame, headers) = await AssertResponseHeaders(pipe);
        await AssertDataStream(pipe, new byte[8]);
        await AssertEmptyEndStream(pipe);

        pipe.Response.Close(); // Closing the stream
        await pipe.RequestWriter.CompleteAsync(); // Closing the stream
        await connectionProcessing;
    }

    [Fact]
    public async Task UseHttp3_ReturnsAltSvcHeader()
    {
        bool withPriority = false;
        var (pipe, connection) = CreateConnnection(new() { UseHttp3 = true });
        var (client, connectionProcessing) = CreateApp(pipe, connection, (HttpContext ctx) =>
        {
            return Task.CompletedTask;
        });
        await AssertSettingsFrameAsync(pipe, withNoRfc7540Priorities: withPriority);
        await AssertWindowUpdateFrameAsync(pipe);
        await client.SendHeadersAsync([]);

        // Assert response
        var (frame, headers) = await AssertResponseHeaders(pipe);
        Assert.True(headers.TryGetValue("alt-svc", out var altSvc) && altSvc == "h3=\":443\"");
        Assert.True(frame.EndHeaders);
        await AssertEmptyEndStream(pipe);

        // Shutdown connection
        await client.ShutdownConnectionAsync();
        await AssertGoAwayAsync(pipe, 1, Http2ErrorCode.NO_ERROR);
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

    private static async Task<(Http2Frame Frame, Dictionary<string, string> Header)> AssertResponseHeadersAndContinuations(TestDuplexPipe pipe, int maxDecodedHeaderLength)
    {
        byte[] EnsureLength(byte[] data, int currentLength, int requiredLength)
        {
            if (data.Length >= currentLength + requiredLength)
                return data;

            var buffer = ArrayPool<byte>.Shared.Rent(data.Length * 2);
            data.CopyTo(buffer.AsMemory());
            ArrayPool<byte>.Shared.Return(data);
            return buffer;
        }

        int totalReceivedLength = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(16_384 * 4);
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.HEADERS, frame.Type);
        buffer = EnsureLength(buffer, 0, (int)frame.PayloadLength);
        await pipe.Response.ReadExactlyAsync(buffer.AsMemory(0, (int)frame.PayloadLength)).AsTask().WaitAsync(TestContext.Current.CancellationToken);
        totalReceivedLength += (int)frame.PayloadLength;
        while (!frame.EndHeaders)
        {
            var continuationFrame = await ReadFrameHeaderAsync(pipe.Response);
            Assert.Equal(Http2FrameType.CONTINUATION, continuationFrame.Type);
            buffer = EnsureLength(buffer, totalReceivedLength, (int)continuationFrame.PayloadLength);
            await pipe.Response.ReadExactlyAsync(buffer.AsMemory(totalReceivedLength, (int)continuationFrame.PayloadLength)).AsTask().WaitAsync(TestContext.Current.CancellationToken);
            totalReceivedLength += (int)continuationFrame.PayloadLength;
            frame = continuationFrame;
        }

        var hpackDecoder = new HPackDecoder(maxHeadersLength: maxDecodedHeaderLength);
        var headerHandler = new TestHttpStreamHeadersHandler();
        hpackDecoder.Decode(buffer.AsSpan(0, totalReceivedLength), true, headerHandler);
        ArrayPool<byte>.Shared.Return(buffer);
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

    private static async Task<Http2Frame> AssertSettingsFrameAsync(TestDuplexPipe pipe,
        bool withNoRfc7540Priorities = false)
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
        Assert.Equal(128L, IntegerSerializer.ReadUInt32BigEndian(payload[2..6]));
        payload = payload[6..];

        // InitialWindowSize
        Assert.Equal(4, IntegerSerializer.ReadUInt16BigEndian(payload[..2]));
        Assert.Equal(65_535L, IntegerSerializer.ReadUInt32BigEndian(payload[2..6]));
        payload = payload[6..];

        // ReceiveMaxFrameSize
        Assert.Equal(5, IntegerSerializer.ReadUInt16BigEndian(payload[..2]));
        Assert.Equal(32_768L, IntegerSerializer.ReadUInt32BigEndian(payload[2..6]));

        if (withNoRfc7540Priorities)
        {
            payload = payload[6..];
            Assert.Equal(9, IntegerSerializer.ReadUInt16BigEndian(payload[..2]));
            Assert.Equal(1L, IntegerSerializer.ReadUInt32BigEndian(payload[2..6]));
        }

        return frame;
    }

    private static async Task<Http2Frame> AssertDataStream(TestDuplexPipe pipe, Memory<byte> destination)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.DATA, frame.Type);
        Assert.Equal((uint)destination.Length, frame.PayloadLength);
        Assert.False(frame.EndStream);
        await pipe.Response.ReadExactlyAsync(destination, TestContext.Current.CancellationToken);
        return frame;
    }

    private static async Task<Http2Frame> ReadAllDataStream(TestDuplexPipe pipe)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        while (frame.Type == Http2FrameType.DATA)
        {
            var buffer = new byte[frame.PayloadLength];
            await pipe.Response.ReadExactlyAsync(buffer, TestContext.Current.CancellationToken);
            frame = await ReadFrameHeaderAsync(pipe.Response);
        }
        return frame;
    }

    private async Task AssertPingAckAsync(TestDuplexPipe pipe)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.PING, frame.Type);
        Assert.Equal(1, frame.Flags);
        await pipe.Response.ReadExactlyAsync(new byte[8]).AsTask().WaitAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Http2Frame> AssertRstStreamAsync(TestDuplexPipe pipe, Http2ErrorCode? expectedErrorCode = null)
    {
        var frame = await ReadFrameHeaderAsync(pipe.Response);
        Assert.Equal(Http2FrameType.RST_STREAM, frame.Type);
        var buffer = new byte[4];
        await pipe.Response.ReadExactlyAsync(buffer).AsTask().WaitAsync(TestContext.Current.CancellationToken);
        var errorCode = IntegerSerializer.ReadUInt32BigEndian(buffer);
        if (expectedErrorCode.HasValue)
            Assert.Equal((uint)expectedErrorCode.Value, errorCode);
        return frame;
    }

    private async Task<Http2Frame> AssertRstStreamFrameAsync(Http2Frame frame, TestDuplexPipe pipe, Http2ErrorCode? expectedErrorCode = null)
    {
        Assert.Equal(Http2FrameType.RST_STREAM, frame.Type);
        var buffer = new byte[4];
        await pipe.Response.ReadExactlyAsync(buffer).AsTask().WaitAsync(TestContext.Current.CancellationToken);
        var errorCode = IntegerSerializer.ReadUInt32BigEndian(buffer);
        if (expectedErrorCode.HasValue)
            Assert.Equal((uint)expectedErrorCode.Value, errorCode);
        return frame;
    }
}
