using System.IO.Pipelines;
using System.Net.Quic;
using System.Runtime.InteropServices;
using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

public class Http3StreamTests
{
    private const int Port = 6001;
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
        var serverStreamTask = Task.Run(async () => await quicConnection.ServerConnection.AcceptInboundStreamAsync());
        var sut = new Http3Stream([]);

        var clientStream = await quicConnection.ClientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, TestContext.Current.CancellationToken);
        byte[] data = [.. await GetHeadersFrame(), .. GetData(30), .. GetReservedFrame(750), .. GetData(30)];
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
            // TODO: assert data
            tcs.SetResult();
            return Task.CompletedTask;
        });
        var processing = sut.ProcessStreamAsync(testApp, TestContext.Current.CancellationToken);

        await tcs.Task.WaitAsync(DefaultTimeout, TestContext.Current.CancellationToken);
        clientStream.Close();
        await processing;
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
