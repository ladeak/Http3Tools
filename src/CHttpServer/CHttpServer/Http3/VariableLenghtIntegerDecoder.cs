using System.Buffers;
using System.Buffers.Binary;

namespace CHttpServer.Http3;

public class VariableLenghtIntegerDecoder
{
    public static bool TryRead(ReadOnlySpan<byte> buffer, out ulong value, out int bytesRead)
    {
        if (buffer.IsEmpty)
        {
            value = 0;
            bytesRead = 0;
            return false;
        }
        byte firstByte = buffer[0];
        bytesRead = 1 << (firstByte >> 6);
        switch (bytesRead)
        {
            case 1:
                value = (ulong)(firstByte & 0x3F);
                return true;
            case 2:
                var result = BinaryPrimitives.TryReadUInt16BigEndian(buffer, out var parsedShortValue);
                value = (ulong)parsedShortValue & 0x3FFF;
                return result;
            case 4:
                result = BinaryPrimitives.TryReadUInt32BigEndian(buffer, out var parsedIntValue);
                value = parsedIntValue & 0x3FFFFFFF;
                return result;
            case 8:
                result = BinaryPrimitives.TryReadUInt64BigEndian(buffer, out value);
                value = value & 0x3FFFFFFF_FFFFFFFF;
                return result;
        }
        value = 0;
        return false;
    }

    public static bool TryRead(ReadOnlySequence<byte> source, out ulong value, out int bytesRead)
    {
        if (source.IsEmpty)
        {
            value = 0;
            bytesRead = 0;
            return false;
        }
        byte firstByte = source.FirstSpan[0];
        bytesRead = 1 << (firstByte >> 6);
        if (source.Length < bytesRead)
        {
            value = 0;
            bytesRead = 0;
            return false;
        }
        switch (bytesRead)
        {
            case 1:
                value = (ulong)(firstByte & 0x3F);
                return true;
            case 2:
                var buffer = source.FirstSpan.Length >= 2 ? source.FirstSpan : FlattenBoundary(source, stackalloc byte[2], bytesRead);
                var result = BinaryPrimitives.TryReadUInt16BigEndian(buffer, out var parsedShortValue);
                value = (ulong)parsedShortValue & 0x3FFF;
                return result;
            case 4:
                buffer = source.FirstSpan.Length >= 4 ? source.FirstSpan : FlattenBoundary(source, stackalloc byte[4], bytesRead);
                result = BinaryPrimitives.TryReadUInt32BigEndian(buffer, out var parsedIntValue);
                value = parsedIntValue & 0x3FFFFFFF;
                return result;
            case 8:
                buffer = source.FirstSpan.Length >= 8 ? source.FirstSpan : FlattenBoundary(source, stackalloc byte[8], bytesRead);
                result = BinaryPrimitives.TryReadUInt64BigEndian(buffer, out value);
                value = value & 0x3FFFFFFF_FFFFFFFF;
                return result;
        }
        value = 0;
        return false;
    }

    private static Span<byte> FlattenBoundary(ReadOnlySequence<byte> source, Span<byte> destination, int bytesRead)
    {
        source.Slice(0, bytesRead).CopyTo(destination);
        return destination;
    }
}