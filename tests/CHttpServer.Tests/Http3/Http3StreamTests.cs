using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.InteropServices;
using CHttpServer.Http3;

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
        var headersFrame = (await GetHeadersFrame()).AsMemory();
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

            // Second Write
            await clientStream.WriteAsync(headersFrame[i..], TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
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
        byte[] data = [.. await GetHeadersFrame(), .. GetReservedFrame(10000)];
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
        byte[] data = [.. await GetHeadersFrame(), .. GetReservedFrame(10)];
        for (int i = 1; i < data.Length; i++)
        {
            var quicConnection = await QuicConnectionFixture.SetupConnectionAsync(Port, TestContext.Current.CancellationToken);
            var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
            var sut = new Http3Stream([]);

            var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
            await clientStream.WriteAsync(data.AsMemory(0, i), TestContext.Current.CancellationToken);
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

            await clientStream.WriteAsync(data.AsMemory(i), TestContext.Current.CancellationToken);
            await clientStream.FlushAsync(TestContext.Current.CancellationToken);

            await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
            clientStream.Close();
            await processing;
            await quicConnection.DisposeAsync();
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
            byte[] data = [.. await GetHeadersFrame(), .. GetData(30), .. GetReservedFrame(20), .. GetData(30), .. GetReservedFrame(20), .. GetData(1)];
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
        byte[] data = [.. GetData(30), .. await GetHeadersFrame()]; // DATA before HEADERS is in-order
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
            byte[] data = [.. await GetHeadersFrame(), .. GetData(30), .. GetReservedFrame(20), .. GetData(30), .. GetReservedFrame(20), .. GetData(1)];
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
            byte[] data = [.. await GetHeadersFrame(), .. GetData(30), .. GetReservedFrame(20), .. GetData(33300), .. GetReservedFrame(20), .. GetData(3)];
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
        byte[] data = [.. await GetLargeHeadersFrame(), .. GetData(20000)];
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
        byte[] data = [.. await GetLargeHeadersFrame(), .. GetData(10000)];
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

    private static async Task<byte[]> GetHeadersFrame()
    {
        var encoder = new QPackDecoder();
        var headers = new Http3ResponseHeaderCollection
        {
            { ":path", "/" },
            { ":authority", "localhost" },
            { ":method", "GET" },
            { ":scheme", "https" }
        };
        MemoryStream ms = new();
        var writer = PipeWriter.Create(ms);
        encoder.Encode(headers, writer);
        await writer.FlushAsync();
        var payloadLength = VariableLenghtIntegerDecoder.Write(ms.Length);

        return [1, .. payloadLength.Span, .. ms.GetBuffer().AsSpan(0, (int)ms.Length)];
    }


    private static async Task<byte[]> GetLargeHeadersFrame()
    {
        var encoder = new QPackDecoder();
        var headers = new Http3ResponseHeaderCollection
        {
            { ":path", "/" },
            { ":authority", "localhost" },
            { ":method", "GET" },
            { ":scheme", "https" },
            { "x-custom-header", new string('a', 4096*2) }
        };
        MemoryStream ms = new();
        var writer = PipeWriter.Create(ms);
        encoder.Encode(headers, writer);
        await writer.FlushAsync();
        var payloadLength = VariableLenghtIntegerDecoder.Write(ms.Length);

        return [1, .. payloadLength.Span, .. ms.GetBuffer().AsSpan(0, (int)ms.Length)];
    }

    private static async Task WriteHeaders(QuicStream clientStream)
    {
        byte[] data = await GetHeadersFrame();
        await clientStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientStream.FlushAsync(TestContext.Current.CancellationToken);
    }

    private static byte[] GetData(int length)
    {
        var payloadLength = VariableLenghtIntegerDecoder.Write(length);
        var payload = Enumerable.Sequence(0, length - 1, 1).Select(x => (byte)x);
        return [0, .. payloadLength.Span, .. payload];
    }

    private static byte[] GetReservedFrame(int length)
    {
        var frameType = VariableLenghtIntegerDecoder.Write(2 * 0x1f + 0x21);
        var payloadLength = VariableLenghtIntegerDecoder.Write(length);
        var payload = Enumerable.Sequence(0, length - 1, 1).Select(x => (byte)x);
        return [.. frameType.Span, .. payloadLength.Span, .. payload];
    }
}
