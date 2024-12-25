using System.Buffers;
using System.IO;
using System.Text;
using System.Threading.Channels;
using CHttpServer.System.Net.Http.HPack;

namespace CHttpServer;

internal class Http2ResponseWriter
{
    private const string WriteHeaders = nameof(WriteHeaders);
    private const string WriteData = nameof(WriteData);

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
        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(token))
            {
                if (request.OperationName == WriteData)
                    await WriteDataAsync(request.Stream);
                else if (request.OperationName == WriteHeaders)
                    await WriteHeadersAsync(request.Stream);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }

    public void ScheduleWriteHeaders(Http2Stream source) =>
        _channel.Writer.TryWrite(new StreamWriteRequest(source, WriteHeaders));

    public void ScheduleWriteData(Http2Stream source) =>
        _channel.Writer.TryWrite(new StreamWriteRequest(source, WriteData));

    private async Task WriteDataAsync(Http2Stream stream)
    {
        while (true)
        {
            if (!stream.ResponseContent.TryRead(out var readResult))
                return;

            if (readResult.IsCanceled)
                return;

            var responseContent = readResult.Buffer;
            while (responseContent.Length > _maxFrameSize)
            {
                _frameWriter.WriteData(stream.StreamId, responseContent.Slice(0, _maxFrameSize), endStream: false);
                responseContent = responseContent.Slice(_maxFrameSize);
            }

            // TODO: end stream if no trailers
            _frameWriter.WriteData(stream.StreamId, responseContent, endStream: true);
            await _frameWriter.FlushAsync();

            if (readResult.IsCompleted)
                return;
        }
    }


    private async Task WriteHeadersAsync(Http2Stream stream)
    {
        var buffer = _buffer.AsSpan(0, _maxFrameSize);
        HPackEncoder.EncodeStatusHeader(stream.StatusCode, buffer, out var writtenLength);
        int totalLength = writtenLength;
        foreach (var header in stream.Headers)
        {
            var staticTableIndex = H2StaticTable.GetStaticTableHeaderIndex(header.Key);

            // This is stateful, hence under lock.
            _hpackEncoder.EncodeHeader(buffer.Slice(totalLength), staticTableIndex, GetHeaderEncodingHint(staticTableIndex),
                header.Key, header.Value.ToString(), Encoding.Latin1, out writtenLength);
            totalLength += writtenLength;
        }

        // TODO: end stream if no data?
        _frameWriter.WriteResponseHeader(stream.StreamId, _buffer.AsMemory(0, totalLength), endStream: false);
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
