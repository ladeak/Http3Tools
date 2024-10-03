using System;
using System.Drawing;
using System.IO.Pipelines;

namespace CHttpServer;

internal sealed class FrameWriter
{
    private readonly CHttpConnectionContext _context;
    private readonly Http2Frame _frame;
    private readonly PipeWriter _destination;
    private const int FrameHeaderSize = 9;

    public FrameWriter(CHttpConnectionContext context)
    {
        _context = context;
        _destination = context.TransportPipe!.Output;
        _frame = new Http2Frame();
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
        buffer = buffer[9..];

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

    private void WriteFrameHeader(Span<byte> destination)
    {
        IntegerSerializer.WriteUInt24BigEndian(destination, _frame.PayloadLength);
        destination[3] = (byte)_frame.Type;
        destination[4] = (byte)_frame.Flags;
        IntegerSerializer.WriteUInt32BigEndian(destination[5..], _frame.StreamId);
    }
}
