using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CHttpServer;

internal sealed class FrameWriter
{
    private const int FrameHeaderSize = 9;

    private readonly CHttpConnectionContext _context;
    private readonly Http2Frame _frame;
    private readonly PipeWriter _destination;

    private uint _connectionWindowSize;
    private readonly Lock _connectionWindowLock;

    public FrameWriter(CHttpConnectionContext context, uint connectionWindowSize)
    {
        _context = context;
        _destination = context.TransportPipe!.Output;
        _frame = new Http2Frame();
        _connectionWindowLock = new Lock();
        _connectionWindowSize = connectionWindowSize;
    }

    internal void UpdateConnectionWindowSize(uint updateSize)
    {
        if (updateSize == 0)
            throw new Http2ConnectionException("Window Update Size must not be 0.");

        lock (_connectionWindowLock)
        {
            var updatedValue = _connectionWindowSize + updateSize;
            if (updatedValue > Http2Connection.MaxWindowUpdateSize)
                throw new Http2FlowControlException();

            _connectionWindowSize = updatedValue;
        }
    }

    internal void WriteGoAway(int lastStreamId, Http2ErrorCode errorCode)
    {
        _frame.SetGoAway(lastStreamId, errorCode);
    }

    internal void WriteSettings(Http2SettingsPayload payload)
    {
        uint size = 5 * (2 + 4);
        var totalSize = (int)(size + FrameHeaderSize);
        _frame.SetSettings(size);
        var buffer = _destination.GetSpan(totalSize);
        WriteFrameHeader(buffer);
        buffer = buffer[FrameHeaderSize..];

        buffer = WriteSetting(buffer, 1, payload.HeaderTableSize);
        buffer = WriteSetting(buffer, 2, payload.EnablePush);
        buffer = WriteSetting(buffer, 3, payload.MaxConcurrentStream);
        buffer = WriteSetting(buffer, 4, payload.InitialWindowSize);
        buffer = WriteSetting(buffer, 5, payload.MaxFrameSize);

        static Span<byte> WriteSetting(Span<byte> buffer, ushort id, uint value)
        {
            IntegerSerializer.WriteUInt16BigEndian(buffer, id);
            buffer = buffer[2..];
            IntegerSerializer.WriteUInt32BigEndian(buffer, value);
            buffer = buffer[4..];
            return buffer;
        }
        _destination.Advance(totalSize);
    }

    internal void WriteSettingAck()
    {
        _frame.SetSettingsAck();
        var buffer = _destination.GetSpan(FrameHeaderSize);
        WriteFrameHeader(buffer);
        _destination.Advance(FrameHeaderSize);
    }

    internal void WriteData(uint streamId, ReadOnlySequence<byte> data)
    {
        var dataLength = (int)data.Length; // Cast is safe as it fits in frame.
        int totalSize = dataLength + FrameHeaderSize;
        _frame.SetData(streamId, dataLength);
        var buffer = _destination.GetSpan(totalSize);
        WriteFrameHeader(buffer);
        buffer = buffer[FrameHeaderSize..];
        data.CopyTo(buffer);
        _destination.Advance(totalSize);
    }

    internal void WriteResponseHeader(uint streamId, Memory<byte> headers, bool endStream)
    {
        int totalSize = headers.Length + FrameHeaderSize;
        _frame.SetResponseHeaders(streamId, headers.Length);
        _frame.EndHeaders = true;
        _frame.EndStream = endStream;
        var buffer = _destination.GetSpan(totalSize);
        WriteFrameHeader(buffer);
        buffer = buffer[FrameHeaderSize..];
        headers.Span.CopyTo(buffer);
        _destination.Advance(totalSize);
    }

    internal void WriteEndStream(uint streamId)
    {
        int totalSize = FrameHeaderSize;
        _frame.SetData(streamId, 0);
        _frame.EndStream = true;
        var buffer = _destination.GetSpan(totalSize);
        WriteFrameHeader(buffer);
        buffer = buffer[FrameHeaderSize..];
        _destination.Advance(totalSize);
    }

    internal ValueTask<FlushResult> FlushAsync() => _destination.FlushAsync();

    private void WriteFrameHeader(Span<byte> destination)
    {
        IntegerSerializer.WriteUInt24BigEndian(destination, _frame.PayloadLength);
        destination[3] = (byte)_frame.Type;
        destination[4] = (byte)_frame.Flags;
        IntegerSerializer.WriteUInt32BigEndian(destination[5..], _frame.StreamId);
    }
}
