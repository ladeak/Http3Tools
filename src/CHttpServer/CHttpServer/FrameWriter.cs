﻿using System.Buffers;
using System.IO.Pipelines;

namespace CHttpServer;

internal sealed class FrameWriter
{
    private const int FrameHeaderSize = 9;

    private readonly Http2Frame _frame;
    private readonly PipeWriter _destination;

    public FrameWriter(CHttpConnectionContext context)
    {
        _destination = context.TransportPipe!.Output;
        _frame = new Http2Frame();
    }

    internal void WritePingAck()
    {
        _frame.SetPingAck();
        var buffer = _destination.GetSpan(FrameHeaderSize + 8);
        WriteFrameHeader(buffer);
        _destination.Advance(FrameHeaderSize + 8);
        _destination.FlushAsync();
    }

    internal void WriteGoAway(int lastStreamId, Http2ErrorCode errorCode)
    {
        _frame.SetGoAway(lastStreamId, errorCode);
        var buffer = _destination.GetSpan(FrameHeaderSize);
        WriteFrameHeader(buffer);
        _destination.Advance(FrameHeaderSize);
        _destination.FlushAsync();
        _destination.Complete();
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
        buffer = WriteSetting(buffer, 5, payload.ReceiveMaxFrameSize);

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

    internal void WriteWindowUpdate(uint streamId, uint size)
    {
        _frame.SetWindowUpdate(streamId);
        int totalSize = FrameHeaderSize + 4;
        var buffer = _destination.GetSpan(totalSize);
        WriteFrameHeader(buffer);
        buffer = buffer[FrameHeaderSize..];
        IntegerSerializer.WriteUInt32BigEndian(buffer, size);
        _destination.Advance(totalSize);
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
