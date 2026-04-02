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
    public async Task PseudoHeadersProcessed()
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
    }

    private static async Task WriteHeaders(QuicStream clientControlStream)
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

        byte[] data = [1, .. payloadLength.Span, .. ms.GetBuffer().AsSpan(0, (int)ms.Length)];
        await clientControlStream.WriteAsync(data, TestContext.Current.CancellationToken);
        await clientControlStream.FlushAsync(TestContext.Current.CancellationToken);
    }

    private static async Task WriteData(QuicStream clientControlStream)
    {
        // TODO
    }

    private static async Task WriteReservedFrame(QuicStream clientControlStream)
    {
        // TODO
    }
}
