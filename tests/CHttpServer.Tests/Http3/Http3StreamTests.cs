using System.Diagnostics;
using System.Net.Quic;
using System.Net.ServerSentEvents;
using System.Text;
using CHttpServer.Http3;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer.Tests.Http3;

public class Http3StreamTests
{
    private const int Port = 6010;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SingleWrite_HeaderFrame()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var sut = new Http3Stream([]);

        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        await WriteHeaders(clientStream);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Initialize(null, await serverStreamTask);
        var testApp = new TestBase.TestApplication(ctx =>
        {
            Assert.Equal("/", ctx.Request.Path);
            Assert.Equal("https", ctx.Request.Scheme);
            Assert.Equal("localhost", ctx.Request.Host.ToString());
            Assert.Equal(HttpMethod.Get.ToString(), ctx.Request.Method);
            tcs.SetResult();
            return Task.CompletedTask;
        });
        var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);

        await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
        clientStream.Close();
        await processing;
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task MultipleWrites_HeaderFrame()
    {
        var headersFrame = Http3FrameFixture.GetHeadersFrame();
        for (int i = 1; i < headersFrame.Length; i++)
        {
            var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
            var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
            var sut = new Http3Stream([]);
            var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);

            // First Write
            await clientStream.WriteAsync(headersFrame[..i], TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.Initialize(null, await serverStreamTask);
            var testApp = new TestBase.TestApplication(async ctx =>
            {
                Assert.Equal("/", ctx.Request.Path);
                Assert.Equal("https", ctx.Request.Scheme);
                Assert.Equal("localhost", ctx.Request.Host.ToString());
                Assert.Equal(HttpMethod.Get.ToString(), ctx.Request.Method);

                // This callback shall not complete before the 2nd flush, otherwise the
                // server might close the stream before the flush succeeds.
                await tcs.Task;
            });
            var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);

            // Second Write
            await clientStream.WriteAsync(headersFrame[i..], TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);
            tcs.SetResult();

            clientStream.Close();
            await processing;
            await quicConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task SingleWrite_HeaderFrame_ReservedFrame()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var sut = new Http3Stream([]);

        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        byte[] data = [.. Http3FrameFixture.GetHeadersFrame(), .. Http3FrameFixture.GetReservedFrame(10000)];
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientStream.FlushAsync(TestContext.Current.CancellationToken);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.Initialize(null, await serverStreamTask);
        var testApp = new TestBase.TestApplication(ctx =>
        {
            Assert.Equal("/", ctx.Request.Path);
            Assert.Equal("https", ctx.Request.Scheme);
            Assert.Equal("localhost", ctx.Request.Host.ToString());
            Assert.Equal(HttpMethod.Get.ToString(), ctx.Request.Method);
            tcs.SetResult();
            return Task.CompletedTask;
        });
        var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);

        await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
        clientStream.Close();
        await processing;
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task MultipleWrites_HeaderFrame_ReservedFrame()
    {
        byte[] data = [.. Http3FrameFixture.GetHeadersFrame(), .. Http3FrameFixture.GetReservedFrame(10)];
        await using var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        for (int i = 1; i < data.Length; i++)
        {
            var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
            var sut = new Http3Stream([]);

            await using var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
            await clientStream.WriteAsync(data.AsMemory(0, i), TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            TaskCompletionSource secondFlushCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.Initialize(null, await serverStreamTask);
            var testApp = new TestBase.TestApplication(async ctx =>
            {
                Assert.Equal("/", ctx.Request.Path);
                Assert.Equal("https", ctx.Request.Scheme);
                Assert.Equal("localhost", ctx.Request.Host.ToString());
                Assert.Equal(HttpMethod.Get.ToString(), ctx.Request.Method);

                // This callback shall not complete before the 2nd flush, otherwise the
                // server might close the stream before the flush succeeds.
                await secondFlushCompleted.Task;
            });
            var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);

            await clientStream.WriteAsync(data.AsMemory(i), TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);
            secondFlushCompleted.SetResult();

            await processing;
            clientStream.Close();
        }
    }

    [Fact]
    public async Task SingleWrite_HeaderFrame_ReservedFrame_DataFrame()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        for (int i = 0; i < 100; i++)
        {
            var sut = new Http3Stream([]);
            var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
            var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
            byte[] data = [.. Http3FrameFixture.GetHeadersFrame(), .. Http3FrameFixture.GetDataFrame(30), .. Http3FrameFixture.GetReservedFrame(20), .. Http3FrameFixture.GetDataFrame(30), .. Http3FrameFixture.GetReservedFrame(20), .. Http3FrameFixture.GetDataFrame(1)];
            await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.Initialize(null, await serverStreamTask);
            var testApp = new TestBase.TestApplication(async ctx =>
            {
                Assert.Equal("/", ctx.Request.Path);
                Assert.Equal("https", ctx.Request.Scheme);
                Assert.Equal("localhost", ctx.Request.Host.ToString());
                Assert.Equal(HttpMethod.Get.ToString(), ctx.Request.Method);
                var content = new MemoryStream();
                long totalLength = 0;
                while (true)
                {
                    var readResult = await ctx.Request.BodyReader.ReadAsync();
                    totalLength += readResult.Buffer.Length;
                    ctx.Request.BodyReader.AdvanceTo(readResult.Buffer.End);
                    if (readResult.IsCanceled || readResult.IsCompleted)
                        break;
                }
                Assert.Equal(61L, totalLength);
                tcs.SetResult();
            });
            var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);

            clientStream.Close();
            await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);

            await processing;
        }
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task SingleWrite_InOrderFrames_Throw()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var sut = new Http3Stream([]);

        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        byte[] data = [.. Http3FrameFixture.GetDataFrame(30), .. Http3FrameFixture.GetHeadersFrame()]; // DATA before HEADERS is in-order
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientStream.FlushAsync(TestContext.Current.CancellationToken);

        sut.Initialize(null, await serverStreamTask);
        var testApp = new TestBase.TestApplication(ctx => Task.CompletedTask);
        var processing = await Assert.ThrowsAsync<Http3ConnectionException>(() => sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken));
        clientStream.Close();
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task ReadMultipleDataFrames()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        for (int i = 0; i < 100; i++)
        {
            var sut = new Http3Stream([]);
            var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
            var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
            byte[] data = [.. Http3FrameFixture.GetHeadersFrame(), .. Http3FrameFixture.GetDataFrame(30), .. Http3FrameFixture.GetReservedFrame(20), .. Http3FrameFixture.GetDataFrame(30), .. Http3FrameFixture.GetReservedFrame(20), .. Http3FrameFixture.GetDataFrame(1)];
            await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.Initialize(null, await serverStreamTask);
            var testApp = new TestBase.TestApplication(async ctx =>
            {
                Assert.Equal("/", ctx.Request.Path);
                Assert.Equal("https", ctx.Request.Scheme);
                Assert.Equal("localhost", ctx.Request.Host.ToString());
                Assert.Equal(HttpMethod.Get.ToString(), ctx.Request.Method);
                var content = new MemoryStream();
                while (true)
                {
                    var readResult = await ctx.Request.BodyReader.ReadAsync();
                    if (readResult.Buffer.Length > 60)
                        ctx.Request.BodyReader.AdvanceTo(readResult.Buffer.End);
                    else
                        ctx.Request.BodyReader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    if (readResult.IsCanceled || readResult.IsCompleted)
                        break;
                }
                tcs.SetResult();
            });
            var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);

            clientStream.Close();
            await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);

            await processing;
        }
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task ReadLargeDataFrames()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        for (int i = 0; i < 100; i++)
        {
            var sut = new Http3Stream([]);
            var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
            var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
            byte[] data = [.. Http3FrameFixture.GetHeadersFrame(), .. Http3FrameFixture.GetDataFrame(30), .. Http3FrameFixture.GetReservedFrame(20), .. Http3FrameFixture.GetDataFrame(33300), .. Http3FrameFixture.GetReservedFrame(20), .. Http3FrameFixture.GetDataFrame(3)];
            await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.Initialize(null, await serverStreamTask);
            var testApp = new TestBase.TestApplication(async ctx =>
            {
                Assert.Equal("/", ctx.Request.Path);
                Assert.Equal("https", ctx.Request.Scheme);
                Assert.Equal("localhost", ctx.Request.Host.ToString());
                Assert.Equal(HttpMethod.Get.ToString(), ctx.Request.Method);
                long totalLength = 0;
                while (true)
                {
                    var readResult = await ctx.Request.BodyReader.ReadAsync();
                    if (readResult.Buffer.Length > 10000)
                    {
                        ctx.Request.BodyReader.AdvanceTo(readResult.Buffer.GetPosition(10000));
                        totalLength += 10000;
                    }
                    else
                    {
                        ctx.Request.BodyReader.AdvanceTo(readResult.Buffer.End);
                        totalLength += readResult.Buffer.Length;
                    }
                    if (readResult.IsCanceled || readResult.IsCompleted)
                        break;
                }
                Assert.Equal(33333, totalLength);
                tcs.SetResult();
            });
            var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);

            clientStream.Close();
            await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);

            await processing;
        }
        await quicConnection.DisposeAsync();
    }

    [Theory]
    [InlineData(31)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(1024)]
    public async Task ReadLargeHeaderLargeDataFrames_Split(int chunkSize)
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        byte[] data = [.. Http3FrameFixture.GetLargeHeadersFrame(), .. Http3FrameFixture.GetDataFrame(20000)];
        for (int i = 1; i < data.Length / chunkSize; i++)
        {
            var sut = new Http3Stream([]);
            var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
            var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
            await clientStream.WriteAsync(data.AsMemory(0, i * chunkSize), TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.Initialize(null, await serverStreamTask);
            var testApp = new TestBase.TestApplication(async ctx =>
            {
                Assert.Equal("/", ctx.Request.Path);
                Assert.Equal("https", ctx.Request.Scheme);
                Assert.Equal("localhost", ctx.Request.Host.ToString());
                Assert.Equal(HttpMethod.Get.ToString(), ctx.Request.Method);
                long totalLength = 0;
                while (true)
                {
                    var readResult = await ctx.Request.BodyReader.ReadAsync();
                    ctx.Request.BodyReader.AdvanceTo(readResult.Buffer.End);
                    totalLength += readResult.Buffer.Length;
                    if (readResult.IsCanceled || readResult.IsCompleted)
                        break;
                }
                Assert.Equal(20000, totalLength);
                tcs.SetResult();
            });
            var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);

            await clientStream.WriteAsync(data.AsMemory(i * chunkSize), TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            clientStream.Close();
            await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);

            await processing;
        }
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task Application_CopyTo()
    {
        int chunks = 512;
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        byte[] data = [.. Http3FrameFixture.GetLargeHeadersFrame(), .. Http3FrameFixture.GetDataFrame(10000)];
        for (int i = 1; i < data.Length / chunks; i++)
        {
            var sut = new Http3Stream([]);
            var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
            var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
            await clientStream.WriteAsync(data.AsMemory(0, i * chunks), TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            sut.Initialize(null, await serverStreamTask);
            var testApp = new TestBase.TestApplication(async ctx =>
            {
                Assert.Equal("/", ctx.Request.Path);
                Assert.Equal("https", ctx.Request.Scheme);
                Assert.Equal("localhost", ctx.Request.Host.ToString());
                Assert.Equal(HttpMethod.Get.ToString(), ctx.Request.Method);
                var ms = new MemoryStream();
                await ctx.Request.BodyReader.CopyToAsync(ms);
                Assert.Equal(10000, ms.Length);
                tcs.SetResult();
            });
            var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);

            await clientStream.WriteAsync(data.AsMemory(i * chunks), TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            clientStream.Close();
            await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);

            await processing;
        }
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task RequestCancellation_WritesClosed_AppDoesNotThrow()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        byte[] data = [.. Http3FrameFixture.GetHeadersFrame(), .. Http3FrameFixture.GetDataFrame(100)];

        var sut = new Http3Stream([]);
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverStream = await serverStreamTask;
        sut.Initialize(null, serverStream);
        var testApp = new TestBase.TestApplication(async ctx =>
        {
            var token = ctx.Features.Get<IHttpRequestLifetimeFeature>()?.RequestAborted;
            if (token is null)
                Assert.Fail("Request Cancellation feature is missing.");
            tcs.SetResult();
            while (!token.Value.IsCancellationRequested)
                await Task.Delay(100);
        });

        var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);
        await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);

        // Act
        clientStream.Abort(QuicAbortDirection.Write, ErrorCodes.H3RequestCancelled);

        await Assert.ThrowsAsync<QuicException>(() => clientStream.ReadsClosed,
            ex => ex.ApplicationErrorCode == 268 ? null : "Expected error code is 268");

        await processing.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task RequestCancellation_AbortClientRead_AppDoesNotThrow()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        byte[] data = [.. Http3FrameFixture.GetHeadersFrame(), .. Http3FrameFixture.GetDataFrame(100)];

        var sut = new Http3Stream([]);
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverStream = await serverStreamTask;
        sut.Initialize(null, serverStream);
        var testApp = new TestBase.TestApplication(async ctx =>
        {
            var token = ctx.Features.Get<IHttpRequestLifetimeFeature>()?.RequestAborted;
            if (token is null)
                Assert.Fail("Request Cancellation feature is missing.");
            tcs.SetResult();
            while (!token.Value.IsCancellationRequested)
                await Task.Delay(100);
        });

        var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);
        await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);

        // Act
        clientStream.Abort(QuicAbortDirection.Read, ErrorCodes.H3RequestCancelled);

        await Assert.ThrowsAsync<QuicException>(() => clientStream.WritesClosed,
            ex => ex.ApplicationErrorCode == 268 ? null : "Expected error code is 268");

        await processing.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task RequestCancellation_AbortClientRead_AppThrows()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        byte[] data = [.. Http3FrameFixture.GetHeadersFrame(), .. Http3FrameFixture.GetDataFrame(100)];

        var sut = new Http3Stream([]);
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverStream = await serverStreamTask;
        sut.Initialize(null, serverStream);
        var testApp = new TestBase.TestApplication(async ctx =>
        {
            var token = ctx.Features.Get<IHttpRequestLifetimeFeature>()?.RequestAborted;
            if (token is null)
                Assert.Fail("Request Cancellation feature is missing.");
            tcs.SetResult();
            while (true)
            {
                token.Value.ThrowIfCancellationRequested();
                await Task.Delay(100);
            }
        });

        var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);
        await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);

        // Act
        clientStream.Abort(QuicAbortDirection.Read, ErrorCodes.H3RequestCancelled);

        await Assert.ThrowsAsync<QuicException>(() => clientStream.WritesClosed,
            ex => ex.ApplicationErrorCode == 268 ? null : "Expected error code is 268");

        // Shutdown
        await processing.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task RequestCancellation_ReadsClosed_AppThrows()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        byte[] data = [.. Http3FrameFixture.GetHeadersFrame(), .. Http3FrameFixture.GetDataFrame(100)];

        var sut = new Http3Stream([]);
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverStream = await serverStreamTask;
        sut.Initialize(null, serverStream);
        var testApp = new TestBase.TestApplication(async ctx =>
        {
            var token = ctx.Features.Get<IHttpRequestLifetimeFeature>()?.RequestAborted;
            if (token is null)
                Assert.Fail("Request Cancellation feature is missing.");
            tcs.SetResult();
            while (true)
            {
                token.Value.ThrowIfCancellationRequested();
                await Task.Delay(100);
            }
        });

        var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);
        await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);

        // Act
        clientStream.Abort(QuicAbortDirection.Read, ErrorCodes.H3RequestCancelled);

        await Assert.ThrowsAsync<QuicException>(() => clientStream.WritesClosed,
            ex => ex.ApplicationErrorCode == 268 ? null : "Expected error code is 268");

        // Shutdown
        await processing.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task RequestCancellationByStream_BeforeAppProcessStarts()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);

        // Only send a partial header, so that appplication processing does not start.
        byte[] data = [.. Http3FrameFixture.GetLargeHeadersFrame()[0..^10]];

        var sut = new Http3Stream([]);
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverStream = await serverStreamTask;
        sut.Initialize(null, serverStream);
        var testApp = new TestBase.TestApplication(async ctx => { Assert.Fail(); });

        var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);
        clientStream.Abort(QuicAbortDirection.Write, ErrorCodes.H3RequestCancelled);

        await Assert.ThrowsAsync<QuicException>(() => clientStream.ReadsClosed,
            ex => ex.ApplicationErrorCode == 267 ? null : "Expected error code is 267");

        await processing.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task RequestCancellationByToken_BeforeAppProcessStarts()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);

        // Only send a partial header, so that appplication processing does not start.
        byte[] data = [.. Http3FrameFixture.GetLargeHeadersFrame()[0..^10]];

        var sut = new Http3Stream([]);
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverStream = await serverStreamTask;
        sut.Initialize(null, serverStream);
        var testApp = new TestBase.TestApplication(async ctx => { Assert.Fail(); });

        var processing = sut.ProcessStreamAsync(testApp, new CancellationToken(true));

        await Assert.ThrowsAsync<QuicException>(() => clientStream.WritesClosed,
            ex => ex.ApplicationErrorCode == 267 ? null : "Expected error code is 267");

        await processing.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
        await quicConnection.DisposeAsync();
    }

    [Fact]
    public async Task Test_ServerSideEvents()
    {
        var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
        byte[] data = [.. Http3FrameFixture.GetHeadersFrame()];

        var sut = new Http3Stream([]);
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverStream = await serverStreamTask;
        sut.Initialize(null, serverStream);
        var testApp = new TestBase.TestApplication(async ctx =>
        {
            ctx.Response.Headers.TryAdd("Cache-Control", "no-cache,no-store");
            ctx.Response.Headers.TryAdd("content-encoding", "identity");
            ctx.Response.Headers.TryAdd("Content-Type", "text/event-stream");
            ctx.Response.Headers.TryAdd("Pragma", "no-cache");
            await SseFormatter.WriteAsync(ResponseAsync(), ctx.Response.Body);
        });

        var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);
        await AssertHeadersAsync(clientStream, headerLength: 70);
        await AssertDataAsync(clientStream, "data: data0\n\n");
        await AssertDataAsync(clientStream, "data: data1\n\n");

        await processing.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
        await quicConnection.DisposeAsync();

        static async IAsyncEnumerable<SseItem<string>> ResponseAsync()
        {
            yield return new SseItem<string>("data0");
            await Task.Yield();
            yield return new SseItem<string>("data1");
        }
    }

    private static async Task AssertHeadersAsync(Stream stream, int headerLength)
    {
        var expectedLengthBytesCount = VariableLenghtIntegerDecoder.Write(headerLength).Length;
        var buffer = new byte[1 + expectedLengthBytesCount];
        await stream.ReadExactlyAsync(buffer.AsMemory(), TestContext.Current.CancellationToken);
        Assert.Equal(0x01, buffer[0]); // Frame Type: HEADERS
        Assert.True(VariableLenghtIntegerDecoder.TryRead(buffer[1..], out var actualLength, out _));
        Assert.Equal((ulong)headerLength, actualLength); // Length 
        await stream.ReadExactlyAsync(new byte[headerLength], TestContext.Current.CancellationToken);
    }

    private static async Task AssertDataAsync(Stream stream, string expectedData)
    {
        var expectedLengthBytesCount = VariableLenghtIntegerDecoder.Write(expectedData.Length).Length;
        var buffer = new byte[1 + expectedLengthBytesCount];
        await stream.ReadExactlyAsync(buffer.AsMemory(), TestContext.Current.CancellationToken);
        Assert.Equal(0x00, buffer[0]); // Frame Type: DATA
        Assert.True(VariableLenghtIntegerDecoder.TryRead(buffer[1..], out var actualLength, out _));
        Assert.Equal((ulong)expectedData.Length, actualLength);
        var data = new byte[expectedData.Length];
        await stream.ReadExactlyAsync(data, TestContext.Current.CancellationToken);
        Assert.Equal(expectedData, Encoding.Latin1.GetString(data));
    }

    private static async Task WriteHeaders(QuicStream clientStream)
    {
        byte[] data = Http3FrameFixture.GetHeadersFrame();
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientStream.FlushAsync(TestContext.Current.CancellationToken);
    }
}
