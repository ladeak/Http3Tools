using System.Buffers;
using System.IO.Pipelines;
using System.Security.Authentication;
using System.Text;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Connections.Features;
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
        var stream = new Http2Stream<TestHttpContext>(1, 10, connection, features, new TestApplication(_ => Task.CompletedTask));

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

    [Fact]
    public async Task ConnectionTest()
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
        var client = new MemoryClient(pipe.RequestWriter);
        var connectionTask = connection.ProcessRequestAsync(new TestApplication(_ => Task.CompletedTask));

        // Send request
        await client.SendHeadersAsync([], true);

        // Shutdown connection
        await client.ShutdownConnectionAsync();

        // Await shutdown
        await connectionTask;

        // TODO asserts
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

    private class TestHttpContext(IFeatureCollection features)
    {
        private readonly IFeatureCollection _features = features;
    }

    private class TestApplication(Func<TestHttpContext, Task> handler) : IHttpApplication<TestHttpContext>
    {
        public TestHttpContext CreateContext(IFeatureCollection contextFeatures) => new TestHttpContext(contextFeatures);

        public void DisposeContext(TestHttpContext context, Exception? exception)
        {

        }

        public Task ProcessRequestAsync(TestHttpContext context)
        {
            return handler(context);
        }
    }

    private class TestHttpStreamHeadersHandler : IHttpStreamHeadersHandler
    {
        internal Dictionary<string, string> Headers { get; } = new();

        public void OnDynamicIndexedHeader(int? index, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
        {
            Headers.Add(Encoding.Latin1.GetString(name), Encoding.Latin1.GetString(value));
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

    private class TestDuplexPipe() : IDuplexPipe
    {
        private readonly Pipe _input = new Pipe(new PipeOptions(readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline));
        private readonly Pipe _output = new Pipe(new PipeOptions(readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline));

        public PipeReader Input => _input.Reader;

        public PipeWriter Output => _output.Writer;

        public Stream ResponseReader => _output.Writer.AsStream();

        public PipeWriter RequestWriter => _input.Writer;
    }

    public class TestTls : ITlsHandshakeFeature
    {
        public SslProtocols Protocol => SslProtocols.Tls12;

        public CipherAlgorithmType CipherAlgorithm => throw new NotImplementedException();

        public int CipherStrength => throw new NotImplementedException();

        public HashAlgorithmType HashAlgorithm => throw new NotImplementedException();

        public int HashStrength => throw new NotImplementedException();

        public ExchangeAlgorithmType KeyExchangeAlgorithm => throw new NotImplementedException();

        public int KeyExchangeStrength => throw new NotImplementedException();
    }

    public class MemoryClient
    {
        private readonly DynamicHPackEncoder _hpackEncoder;
        private readonly FrameWriter _frameWriter;

        public MemoryClient(PipeWriter requestPipe)
        {
            _hpackEncoder = new DynamicHPackEncoder(false, 4096);
            _frameWriter = new FrameWriter(requestPipe);
            var preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;
            requestPipe.Write(preface);
        }

        public async ValueTask SendHeadersAsync(HeaderCollection headers, bool endStream)
        {
            int totalLength = 0;
            var buffer = new byte[8096];

            foreach (var header in headers)
            {
                var staticTableIndex = H2StaticTable.GetStaticTableHeaderIndex(header.Key);

                // This is stateful
                if (!_hpackEncoder.EncodeHeader(buffer.AsSpan(totalLength), staticTableIndex, GetHeaderEncodingHint(staticTableIndex),
                    header.Key, header.Value.ToString(), Encoding.Latin1, out var writtenLength))
                    throw new InvalidOperationException("Header too large");
                totalLength += writtenLength;
            }
            _frameWriter.WriteHeader(0, buffer.AsMemory(0, totalLength), endStream);
            await _frameWriter.FlushAsync();
        }

        internal ValueTask<FlushResult> ShutdownConnectionAsync()
        {
            _frameWriter.WriteGoAway(0, Http2ErrorCode.NO_ERROR);
            return _frameWriter.FlushAsync();
        }

        private HeaderEncodingHint GetHeaderEncodingHint(int headerIndex)
        {
            return headerIndex switch
            {
                55 => HeaderEncodingHint.NeverIndex, // SetCookie
                25 => HeaderEncodingHint.NeverIndex, // Content-Disposition
                28 => HeaderEncodingHint.IgnoreIndex, // Content-Length
                _ => HeaderEncodingHint.Index
            };
        }
    }

}
