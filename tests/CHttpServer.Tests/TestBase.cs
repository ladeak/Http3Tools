using System.Buffers;
using System.IO.Pipelines;
using System.Security.Authentication;
using System.Text;
using CHttpServer.System.Net.Http.HPack;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;

namespace CHttpServer.Tests;

internal class TestBase
{
    internal static async Task<Http2Frame> ReadFrameHeaderAsync(Stream stream)
    {
        const int FrameHeaderSize = 9;
        var frame = new Http2Frame();
        var headerBuffer = new byte[FrameHeaderSize];
        await stream.ReadExactlyAsync(headerBuffer).AsTask().WaitAsync(TestContext.Current.CancellationToken);
        frame.PayloadLength = IntegerSerializer.ReadUInt24BigEndian(headerBuffer[0..3]);
        frame.Type = (Http2FrameType)headerBuffer[3];
        frame.Flags = headerBuffer[4];
        var streamId = IntegerSerializer.ReadUInt32BigEndian(headerBuffer[5..]);
        return frame;
    }

    internal class TestHttpContext(IFeatureCollection features)
    {
        private readonly IFeatureCollection _features = features;
    }

    internal class TestApplication(Func<TestHttpContext, Task> handler) : IHttpApplication<TestHttpContext>
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

    internal class TestHttpStreamHeadersHandler : IHttpStreamHeadersHandler
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

    internal class TestDuplexPipe() : IDuplexPipe
    {
        private readonly Pipe _input = new Pipe(new PipeOptions(readerScheduler: PipeScheduler.ThreadPool, writerScheduler: PipeScheduler.ThreadPool));
        private readonly Pipe _output = new Pipe(new PipeOptions(readerScheduler: PipeScheduler.ThreadPool, writerScheduler: PipeScheduler.ThreadPool));

        public PipeReader Input => _input.Reader;

        public PipeWriter Output => _output.Writer;

        public Stream Response => _output.Reader.AsStream();

        public PipeReader ResponseReader => _output.Reader;

        public PipeWriter RequestWriter => _input.Writer;
    }

    internal class TestTls : ITlsHandshakeFeature
    {
        public SslProtocols Protocol => SslProtocols.Tls12;

        public CipherAlgorithmType CipherAlgorithm => throw new NotImplementedException();

        public int CipherStrength => throw new NotImplementedException();

        public HashAlgorithmType HashAlgorithm => throw new NotImplementedException();

        public int HashStrength => throw new NotImplementedException();

        public ExchangeAlgorithmType KeyExchangeAlgorithm => throw new NotImplementedException();

        public int KeyExchangeStrength => throw new NotImplementedException();
    }

    internal class TestHeartbeat : IConnectionHeartbeatFeature
    {
        List<(Action<object>, object)> callbacks = [];

        public void OnHeartbeat(Action<object> action, object state)
        {
            callbacks.Add((action, state));
        }

        internal void TriggerHeartbeat()
        {
            foreach (var (action, state) in callbacks)
                action(state);
        }
    }

    internal class TestClient
    {
        private readonly DynamicHPackEncoder _hpackEncoder;
        private readonly FrameWriter _frameWriter;
        private readonly PipeWriter _requestPipe;

        public TestClient(PipeWriter requestPipe, bool sendPreface = true)
        {
            _hpackEncoder = new DynamicHPackEncoder(false, 4096);
            _frameWriter = new FrameWriter(requestPipe);
            _requestPipe = requestPipe;
            if (sendPreface)
                SendPrefaceAsync().GetAwaiter().GetResult();
        }

        internal ValueTask<FlushResult> SendPrefaceAsync() => SendPrefaceAsync("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8);

        internal ValueTask<FlushResult> SendPrefaceAsync(ReadOnlySpan<byte> preface)
        {
            _requestPipe.Write(preface);
            return _requestPipe.FlushAsync();
        }

        internal async ValueTask SendHeadersAsync(HeaderCollection headers, bool endStream)
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
