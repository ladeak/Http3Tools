using System;
using System.Buffers;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using CHttpServer.System.Net.Http.HPack;

namespace CHttpServer;

internal class Http2ResponseWriter
{
    private readonly DynamicHPackEncoder _hpackEncoder;
    private readonly FrameWriter _frameWriter;
    private readonly int _maxFrameSize;
    private readonly Lock _writeLock;

    public Http2ResponseWriter(FrameWriter frameWriter, uint maxFrameSize)
    {
        _frameWriter = frameWriter;
        _maxFrameSize = (int)maxFrameSize;
        _hpackEncoder = new DynamicHPackEncoder();
        _writeLock = new Lock();
    }

    public async Task WriteHeadersAsync(uint streamId, int statusCode, HeaderCollection headers)
    {
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(_maxFrameSize);
        var buffer = rentedBuffer.AsSpan(0, _maxFrameSize);
        HPackEncoder.EncodeStatusHeader(statusCode, buffer, out var writtenLength);
        int totalLength = writtenLength;
        lock (_writeLock)
        {
            foreach (var header in headers)
            {
                var staticTableIndex = H2StaticTable.GetStaticTableHeaderIndex(header.Key);

                // This is stateful, hence under lock.
                _hpackEncoder.EncodeHeader(buffer.Slice(totalLength), staticTableIndex, GetHeaderEncodingHint(staticTableIndex),
                    header.Key, header.Value.ToString(), Encoding.Latin1, out writtenLength);
                totalLength += writtenLength;
            }
            _frameWriter.WriteResponseHeader(streamId, rentedBuffer.AsMemory(0, totalLength));
        }
        await _frameWriter.FlushAsync();

        ArrayPool<byte>.Shared.Return(rentedBuffer);
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

    private static char GetSeparatorChar(string headerName)
    {
        return headerName switch
        {
            "User-Agent" => ';',
            "Cookie" => ';',
            "Server" => ';',
            _ => ',',
        };
    }
}
