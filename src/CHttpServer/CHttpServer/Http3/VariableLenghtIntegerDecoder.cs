using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;

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

    /// <summary>
    /// Writes a variable length positive integer (or zero) to the destination.
    /// Returns <see langword="false" /> if destination is too small.
    /// Returns <see langword="true" /> if the number is written successfully.
    /// </summary>
    /// <param name="destination">Writable memory region.</param>
    /// <param name="value">Postitive integer or zero.</param>
    /// <param name="bytesWritten">Number of bytes written to the <paramref name="destination"/>.</param>
    /// <returns></returns>
    public static bool TryWrite(Span<byte> destination, ulong value, out int bytesWritten)
    {
        if (value <= 63ul)
        {
            if (destination.Length > 0)
            {
                destination[0] = (byte)value;
                bytesWritten = 1;
                return true;
            }
        }
        else if (value <= 16383ul)
        {
            if (destination.Length > 1)
            {
                BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)(value | 0x4000));
                bytesWritten = 2;
                return true;
            }
        }
        else if (value <= 1073741823ul)
        {
            if (destination.Length > 3)
            {
                BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)(value | 0x80000000));
                bytesWritten = 4;
                return true;
            }
        }
        else if (value <= 4611686018427387903ul)
        {
            if (destination.Length > 7)
            {
                BinaryPrimitives.WriteUInt64BigEndian(destination, (value | 0xC000_0000_0000_0000));
                bytesWritten = 8;
                return true;
            }
        }
        bytesWritten = 0;
        return false;

    }


    /// <summary>
    /// Writes a variable length positive integer (or zero) to the destination.
    /// Returns <see langword="false" /> if destination is too small.
    /// Returns <see langword="true" /> if the number is written successfully.
    /// </summary>
    /// <param name="destination">Writable memory region.</param>
    /// <param name="value">Postitive integer or zero.</param>
    /// <param name="bytesWritten">Number of bytes written to the <paramref name="destination"/>.</param>
    /// <returns></returns>
    public static bool TryWrite<T>(Span<byte> destination, T value, out int bytesWritten)
        where T : IBinaryInteger<T>
    {
        return TryWrite(destination, ulong.CreateChecked(value), out bytesWritten);
    }
}