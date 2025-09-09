using System.Buffers.Binary;

namespace CHttpServer.Http3;

public class VariableLenghtIntegerDecoder
{
    public static bool TryRead(ReadOnlySpan<byte> buffer, out ulong value, out int bytesRead)
    {
        byte firstByte = buffer[0];
        var length = firstByte & 0b11000000;
        switch(length)
        {
            case 0b00000000:
                bytesRead = 1;
                value = (ulong)(firstByte & 0x3F);
                return true;
            case 0b01000000:
                bytesRead = 2;
                var result = BinaryPrimitives.TryReadUInt16BigEndian(buffer, out var parsedShortValue);
                value = (ulong)parsedShortValue & 0x3FFF;
                return result;
            case 0b10000000:
                bytesRead = 4;
                result = BinaryPrimitives.TryReadUInt32BigEndian(buffer, out var parsedIntValue);
                value = parsedIntValue & 0x3FFFFFFF;
                return result;
            case 0b11000000:
                bytesRead = 8;
                result = BinaryPrimitives.TryReadUInt64BigEndian(buffer, out value);
                value = value & 0x3FFFFFFF_FFFFFFFF;
                return result;
        }
        value = 0;
        bytesRead = 0;
        return false;
    }
}