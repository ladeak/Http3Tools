using System.Text;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;

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
        var connectionContext = new CHttpConnectionContext()
        {
            ConnectionId = 1,
            Features = features,
            ServerOptions = new CHttpServerOptions(),
            TransportPipe = new DuplexPipeStreamAdapter<MemoryStream>(memoryStream, new(), new())
        };
        var connection = new Http2Connection(connectionContext) { ResponseWriter = new Http2ResponseWriter(new FrameWriter(connectionContext), 1000) };
        var stream = new Http2Stream<TestHttpContext>(1, 10, connection, features, new TestApplication());

        stream.OnCompleted(_ => { taskCompletionSource.SetResult(); cts.Cancel(); return Task.CompletedTask; }, new());
        var outputWriterTask = connection.ResponseWriter.RunAsync(cts.Token);
        stream.Execute();

        // Await response completion
        await taskCompletionSource.Task;


        // Await shutdown
        await outputWriterTask;

        memoryStream.Seek(0, SeekOrigin.Begin);
        var header = ReadFrameHeader(memoryStream);
        Assert.Equal(Http2FrameType.HEADERS, header.Type);
        Assert.True(header.EndHeaders);
        Assert.Equal(1L, header.PayloadLength);
        var buffer = new byte[1];
        memoryStream.ReadExactly(buffer);
        var decoder = new HPackDecoder();
        var headerHandler = new TestHttpStreamHeadersHandler();
        decoder.Decode(buffer.AsSpan(), true, headerHandler);
        Assert.Equal("204", headerHandler.Headers[":status"]);

        header = ReadFrameHeader(memoryStream);
        Assert.Equal(Http2FrameType.DATA, header.Type);
        Assert.True(header.EndStream);
        Assert.Equal(0L, header.PayloadLength);
    }

    private static Http2Frame ReadFrameHeader(MemoryStream stream)
    {
        var frame = new Http2Frame();
        var headerBuffer = (new byte[9]).AsSpan();
        stream.ReadExactly(headerBuffer);
        frame.PayloadLength = IntegerSerializer.ReadUInt24BigEndian(headerBuffer[0..3]);
        frame.Type = (Http2FrameType)headerBuffer[3];
        frame.Flags = headerBuffer[4];
        var streamId = IntegerSerializer.ReadUInt32BigEndian(headerBuffer[5..]);
        return frame;
    }

    private class TestHttpContext
    {
    }

    private class TestApplication : IHttpApplication<TestHttpContext>
    {
        public TestHttpContext CreateContext(IFeatureCollection contextFeatures)
        {
            return new TestHttpContext();
        }

        public void DisposeContext(TestHttpContext context, Exception? exception)
        {

        }

        public Task ProcessRequestAsync(TestHttpContext context)
        {
            return Task.CompletedTask;
        }
    }

    private class TestHttpStreamHeadersHandler : IHttpStreamHeadersHandler
    {
        internal Dictionary<string, string> Headers { get; } = new();

        public void OnDynamicIndexedHeader(int? index, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
        {
        }

        public void OnHeader(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
        {
            Headers.Add(Encoding.Latin1.GetString(name), Encoding.Latin1.GetString(value));
        }

        public void OnHeadersComplete(bool endStream)
        {
        }

        public void OnStaticIndexedHeader(int index)
        {
            var field = H2StaticTable.Get(index);
            Headers.Add(Encoding.Latin1.GetString(field.Name), Encoding.Latin1.GetString(field.Value));
        }

        public void OnStaticIndexedHeader(int index, ReadOnlySpan<byte> value)
        {
            var field = H2StaticTable.Get(index);
            Headers.Add(Encoding.Latin1.GetString(field.Name), Encoding.Latin1.GetString(value));
        }
    }
}
