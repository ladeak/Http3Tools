﻿using System.Buffers;
using System.Text;
using System.Threading.Channels;
using CHttpServer.System.Net.Http.HPack;

namespace CHttpServer;

internal class Http2ResponseWriter : IResponseWriter
{
    private const string WriteHeaders = nameof(WriteHeaders);
    private const string WriteData = nameof(WriteData);
    private const string WriteEndStream = nameof(WriteEndStream);
    private const string WriteOutOfOrderFrame = nameof(WriteOutOfOrderFrame);
    private const string WriteTrailers = nameof(WriteTrailers);
    private const string WriteWindowUpdate = nameof(WriteWindowUpdate);
    private const string WriteRstStream = nameof(WriteRstStream);

    private readonly DynamicHPackEncoder _hpackEncoder;
    private readonly FrameWriter _frameWriter;
    private int _maxFrameSize;
    private readonly Channel<StreamWriteRequest> _channel;
    private readonly Channel<StreamWriteRequest> _priorityChannel;
    private byte[] _buffer;

    public Http2ResponseWriter(FrameWriter frameWriter, uint maxFrameSize)
    {
        _frameWriter = frameWriter;
        _maxFrameSize = (int)maxFrameSize;
        _buffer = [];
        _hpackEncoder = new DynamicHPackEncoder();
        _channel = Channel.CreateUnbounded<StreamWriteRequest>(new UnboundedChannelOptions() { SingleReader = true, AllowSynchronousContinuations = false });
        _priorityChannel = Channel.CreateUnbounded<StreamWriteRequest>(new UnboundedChannelOptions() { SingleReader = true, AllowSynchronousContinuations = false });
    }

    public async Task RunAsync(CancellationToken token)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(_maxFrameSize);
        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(token))
            {
                // Update the buffer size if _maxFrameSize has increased.
                if (_buffer.Length < _maxFrameSize)
                {
                    ArrayPool<byte>.Shared.Return(_buffer);
                    _buffer = ArrayPool<byte>.Shared.Rent(_maxFrameSize);
                }

                // Priority requests always have a corresponding regular channel request too.
                while (_priorityChannel.Reader.TryRead(out var priorityRequest))
                {
                    if (priorityRequest.OperationName == WriteOutOfOrderFrame)
                        await WritePingAckAsync(priorityRequest.Data);
                }
                if (request.OperationName == WriteData)
                    await WriteDataAsync(request);
                else if (request.OperationName == WriteHeaders)
                    await WriteHeadersAsync(request.Stream);
                else if (request.OperationName == WriteEndStream)
                    await WriteEndStreamAsync(request.Stream);
                else if (request.OperationName == WriteTrailers)
                    await WriteTrailersAsync(request.Stream);
                else if (request.OperationName == WriteWindowUpdate)
                    await WriteWindowUpdateAsync(request.Stream, request.Data);
                else if (request.OperationName == WriteRstStream)
                    await WriteRstStreamAsync(request.Stream, request.Data);
            }
        }
        catch (OperationCanceledException)
        {
            // Channel is closed by the connection.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }

    public void ScheduleWritePingAck(ulong value)
    {
        _priorityChannel.Writer.TryWrite(new StreamWriteRequest(null!, WriteOutOfOrderFrame, value));
        _channel.Writer.TryWrite(new StreamWriteRequest(null!, WriteOutOfOrderFrame));
    }

    public void ScheduleWriteHeaders(Http2Stream source) =>
        _channel.Writer.TryWrite(new StreamWriteRequest(source, WriteHeaders));

    /// <summary>
    /// Schedules the given Http2Stream to be written to the channel.
    /// It writes maximum <paramref name="size"/> bytes of data from the stream
    /// to respect flow control limits.
    /// </summary>
    public void ScheduleWriteData(Http2Stream source) =>
        _channel.Writer.TryWrite(new StreamWriteRequest(source, WriteData));

    public void ScheduleEndStream(Http2Stream source) =>
        _channel.Writer.TryWrite(new StreamWriteRequest(source, WriteEndStream));

    public void ScheduleWriteTrailers(Http2Stream source) =>
        _channel.Writer.TryWrite(new StreamWriteRequest(source, WriteTrailers));

    public void ScheduleWriteWindowUpdate(Http2Stream source, uint size) =>
        _channel.Writer.TryWrite(new StreamWriteRequest(source, WriteWindowUpdate, size));

    public void ScheduleResetStream(Http2Stream source, Http2ErrorCode errorCode) =>
        _channel.Writer.TryWrite(new StreamWriteRequest(source, WriteRstStream, (ulong)errorCode));

    public void Complete()
    {
        _channel.Writer.TryComplete();
        _priorityChannel.Writer.TryComplete();
    }

    private async ValueTask WriteDataAsync(StreamWriteRequest writeRequest)
    {
        var stream = writeRequest.Stream;
        var responseContent = stream.ResponseBodyBuffer;
        do
        {
            var maxFrameSize = _maxFrameSize; // Capture to avoid changing during data writes.
            var currentSize = responseContent.Length > maxFrameSize ? maxFrameSize : responseContent.Length;
            _frameWriter.WriteData(stream.StreamId, responseContent.Slice(0, currentSize));
            await _frameWriter.FlushAsync();
            responseContent = responseContent.Slice(currentSize);
        } while (!responseContent.IsEmpty);
        stream.OnResponseDataFlushed();
    }

    private async Task WriteEndStreamAsync(Http2Stream stream)
    {
        _frameWriter.WriteEndStream(stream.StreamId);
        await _frameWriter.FlushAsync();
        await stream.OnStreamCompletedAsync();
    }

    private async ValueTask WriteHeadersAsync(Http2Stream stream)
    {
        var currentMaxFrameSize = _maxFrameSize;
        var encodingBuffer = _buffer.AsSpan(0, currentMaxFrameSize);
        HPackEncoder.EncodeStatusHeader(stream.StatusCode, encodingBuffer, out var writtenLength);
        int totalLength = writtenLength;
        foreach (var header in stream.ResponseHeaders)
        {
            var staticTableIndex = H2StaticTable.GetStaticTableHeaderIndex(header.Key);

            // This is stateful
            while (!_hpackEncoder.EncodeHeader(encodingBuffer.Slice(totalLength), staticTableIndex, GetHeaderEncodingHint(staticTableIndex),
                header.Key, header.Value.ToString(), Encoding.Latin1, out writtenLength))
            {
                var newBuffer = ArrayPool<byte>.Shared.Rent(encodingBuffer.Length * 2);
                encodingBuffer.CopyTo(newBuffer);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newBuffer;
                encodingBuffer = _buffer.AsSpan();
            }
            totalLength += writtenLength;
        }

        // Fast path when all headers fit in a single frame.
        var flushingBuffer = _buffer.AsMemory(0, totalLength);
        if (flushingBuffer.Length <= currentMaxFrameSize)
        {
            _frameWriter.WriteHeader(stream.StreamId, flushingBuffer, endHeaders: true, endStream: false);
            await _frameWriter.FlushAsync();
            return;
        }

        // Slow path for large headers, write a single HEADER frame followed by CONTINUATION frames.
        _frameWriter.WriteHeader(stream.StreamId, flushingBuffer.Slice(0, currentMaxFrameSize), endHeaders: false, endStream: false);
        await _frameWriter.FlushAsync();
        flushingBuffer = flushingBuffer.Slice(currentMaxFrameSize);

        while (flushingBuffer.Length > currentMaxFrameSize)
        {
            _frameWriter.WriteContinuation(stream.StreamId, flushingBuffer.Slice(0, currentMaxFrameSize), endHeaders: false, endStream: false);
            await _frameWriter.FlushAsync();
            flushingBuffer = flushingBuffer.Slice(currentMaxFrameSize);
        }

        // Flush the remaining part and set endHeaders.
        _frameWriter.WriteContinuation(stream.StreamId, flushingBuffer, endHeaders: true, endStream: false);
        await _frameWriter.FlushAsync();

        if (totalLength > currentMaxFrameSize)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(_maxFrameSize);
        }
    }

    private async ValueTask WritePingAckAsync(ulong data)
    {
        _frameWriter.WritePingAck(data);
        await _frameWriter.FlushAsync();
    }

    private async Task WriteTrailersAsync(Http2Stream stream)
    {
        var buffer = _buffer.AsSpan(0, _maxFrameSize);
        int totalLength = 0;
        foreach (var header in stream.Trailers)
        {
            var staticTableIndex = H2StaticTable.GetStaticTableHeaderIndex(header.Key);

            // This is stateful
            if (!_hpackEncoder.EncodeHeader(buffer.Slice(totalLength), staticTableIndex, GetHeaderEncodingHint(staticTableIndex),
                header.Key, header.Value.ToString(), Encoding.Latin1, out var writtenLength))
                throw new InvalidOperationException("Trailer too large");
            totalLength += writtenLength;
        }

        _frameWriter.WriteHeader(stream.StreamId, _buffer.AsMemory(0, totalLength), endHeaders: true, endStream: true);
        await _frameWriter.FlushAsync();
        await stream.OnStreamCompletedAsync();
    }

    private async ValueTask WriteWindowUpdateAsync(Http2Stream stream, ulong data)
    {
        var size = (uint)data;
        _frameWriter.WriteWindowUpdate(stream.StreamId, size);
        _frameWriter.WriteWindowUpdate(0, size);
        await _frameWriter.FlushAsync();
    }

    private async ValueTask WriteRstStreamAsync(Http2Stream stream, ulong data)
    {
        _frameWriter.WriteRstStream(stream.StreamId, (Http2ErrorCode)data);
        await _frameWriter.FlushAsync();
        await stream.OnStreamCompletedAsync();
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

    public void UpdateFrameSize(uint size)
    {
        if (size < 16384 || size > 16777215)
            throw new Http2ProtocolException(); // Invalid frame size
        _maxFrameSize = (int)size;
    }
}
