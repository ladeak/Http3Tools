using System.IO.Pipelines;

namespace CHttpServer.Http3;

internal static class Http3FrameWriter
{
    public static void WriteControlStreamHeader(PipeWriter destination)
    {
        var buffer = destination.GetSpan(1);
        buffer[0] = 0;
        destination.Advance(1);
    }

    /// <summary>
    /// Setting {
    ///   Identifier (i),
    ///   Value (i),
    /// }
    /// SETTINGS Frame {
    ///   Type (i) = 0x04,
    ///   Length (i),
    ///   Setting (..) ...,
    /// }
    /// </summary>
    public static void WriteSettings(PipeWriter destination, Http3Settings settings)
    {
        // Max length: Type 1 byte; Length 1 byte, Identifier 1 byte, Value 8 byte.
        Span<byte> buffer = destination.GetSpan(11);
        buffer[0] = 0x04; // FrameType

        byte id = settings.ServerMaxFieldSectionSize.HasValue ? (byte)6 : (byte)33;
        VariableLenghtIntegerDecoder.TryWrite(buffer[2..], id, out _);
        VariableLenghtIntegerDecoder.TryWrite(buffer[3..], settings.ServerMaxFieldSectionSize ?? 0, out var valueBytesWritten);
        byte length = (byte)(1 + valueBytesWritten);
        VariableLenghtIntegerDecoder.TryWrite(buffer[1..], length, out var _);
        destination.Advance(2 + length);
    }

    /// <summary>
    /// GOAWAY Frame {
    ///   Type (i) = 0x07,
    ///   Length (i),
    ///   Stream ID/Push ID (i),
    /// }
    /// </summary>
    public static void WriteGoAway(PipeWriter destination, long streamId)
    {
        // Max length: Type 1 byte; Length 1 byte, StreamId 8 byte.
        Span<byte> buffer = destination.GetSpan(10);
        buffer[0] = 0x07; // FrameType
        VariableLenghtIntegerDecoder.TryWrite(buffer[2..], (ulong)streamId, out var writtenBytes);
        VariableLenghtIntegerDecoder.TryWrite(buffer[1..], (ulong)writtenBytes, out var _);
        destination.Advance(2 + writtenBytes);
    }
}
