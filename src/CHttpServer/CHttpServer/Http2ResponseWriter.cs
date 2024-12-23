using System.Buffers;
using System.Text;
using System.Threading.Channels;
using CHttpServer.System.Net.Http.HPack;

namespace CHttpServer;

internal class Http2ResponseWriter
{
    private const string WriteHeaders = nameof(WriteHeaders);

    private record StreamWriteRequest(Http2Stream Stream, string OperationName);

    private readonly DynamicHPackEncoder _hpackEncoder;
    private readonly FrameWriter _frameWriter;
    private readonly int _maxFrameSize;
    private readonly Channel<StreamWriteRequest> _channel;
    private byte[] _buffer;

    public Http2ResponseWriter(FrameWriter frameWriter, uint maxFrameSize)
    {
        _frameWriter = frameWriter;
        _maxFrameSize = (int)maxFrameSize;
        _buffer = [];
        _hpackEncoder = new DynamicHPackEncoder();
        _channel = Channel.CreateUnbounded<StreamWriteRequest>(new UnboundedChannelOptions() { SingleReader = true, AllowSynchronousContinuations = false });
    }

    public async Task RunAsync(CancellationToken token)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(_maxFrameSize);
        await foreach (var request in _channel.Reader.ReadAllAsync(token))
        {
            if (request.OperationName == WriteHeaders)
                await WriteHeadersAsync(request.Stream.StreamId, request.Stream.StatusCode, request.Stream.Headers);
        }
        ArrayPool<byte>.Shared.Return(_buffer);
    }

    public void ScheduleWriteHeaders(Http2Stream source) =>
        _channel.Writer.TryWrite(new StreamWriteRequest(source, WriteHeaders));

    private async Task WriteHeadersAsync(uint streamId, int statusCode, HeaderCollection headers)
    {
        var buffer = _buffer.AsSpan(0, _maxFrameSize);
        HPackEncoder.EncodeStatusHeader(statusCode, buffer, out var writtenLength);
        int totalLength = writtenLength;
        foreach (var header in headers)
        {
            var staticTableIndex = H2StaticTable.GetStaticTableHeaderIndex(header.Key);

            // This is stateful, hence under lock.
            _hpackEncoder.EncodeHeader(buffer.Slice(totalLength), staticTableIndex, GetHeaderEncodingHint(staticTableIndex),
                header.Key, header.Value.ToString(), Encoding.Latin1, out writtenLength);
            totalLength += writtenLength;
        }
        _frameWriter.WriteResponseHeader(streamId, _buffer.AsMemory(0, totalLength));
        await _frameWriter.FlushAsync();
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
