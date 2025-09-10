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
}