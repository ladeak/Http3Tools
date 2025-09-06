using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Http;
using static CHttpServer.Tests.TestBase;

namespace CHttpServer.Tests;

public class Http2StreamTests
{
    [Fact]
    public async Task ReadFlowControlTests()
    {
        var features = new FeatureCollection();
        var taskCompletionSource = new TaskCompletionSource();
        var cts = new CancellationTokenSource();
        using var memoryStream = new MemoryStream();
        var connectionContext = new CHttp2ConnectionContext()
        {
            ConnectionId = 1,
            Features = features,
            ServerOptions = new CHttpServerOptions(),
            TransportPipe = new DuplexPipeStreamAdapter<MemoryStream>(memoryStream, new(), new())
        };
        var connection = new Http2Connection(connectionContext) { ResponseWriter = new Http2ResponseWriter(new FrameWriter(connectionContext), 1000) };
        var stream = new Http2Stream(connection, features);
        stream.Initialize(1, 10, connectionContext.ServerOptions.ServerStreamFlowControlSize);

        stream.OnCompleted(_ => { taskCompletionSource.SetResult(); cts.Cancel(); return Task.CompletedTask; }, new());
        var outputWriterTask = connection.ResponseWriter.RunAsync(cts.Token);
        stream.Execute(new TestApplication(_ => Task.CompletedTask));

        // Await response completion
        await taskCompletionSource.Task;

        // Await shutdown
        await outputWriterTask;

        memoryStream.Seek(0, SeekOrigin.Begin);
        var header = await ReadFrameHeaderAsync(memoryStream);
        Assert.Equal(Http2FrameType.HEADERS, header.Type);
        Assert.True(header.EndHeaders);
        Assert.Equal(9L, header.PayloadLength);
        var buffer = new byte[header.PayloadLength];
        memoryStream.ReadExactly(buffer);
        var decoder = new HPackDecoder();
        var headerHandler = new TestHttpStreamHeadersHandler();
        decoder.Decode(buffer.AsSpan(), true, headerHandler);
        Assert.Equal("200", headerHandler.Headers[":status"]);

        header = await ReadFrameHeaderAsync(memoryStream);
        Assert.Equal(Http2FrameType.DATA, header.Type);
        Assert.True(header.EndStream);
        Assert.Equal(0L, header.PayloadLength);
    }
}
