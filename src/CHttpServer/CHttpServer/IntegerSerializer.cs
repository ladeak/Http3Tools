using System.Buffers.Binary;

namespace CHttpServer;

public static class IntegerSerializer
{
    public static uint ReadUInt24BigEndian(ReadOnlySpan<byte> source) => (uint)((source[0] << 16) | (source[1] << 8) | source[2]);

    public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadUInt32BigEndian(source);

    public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadUInt16BigEndian(source);

    public static void WriteUInt16BigEndian(Span<byte> destination, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(destination, value);

    public static void WriteUInt32BigEndian(Span<byte> destination, uint value) => BinaryPrimitives.WriteUInt32BigEndian(destination, value);

    public static bool WriteUInt24BigEndian(Span<byte> destination, uint value)
    {
        if (destination.Length < 3 || value > 0xFF_FF_FF)
            return false;

        destination[2] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[0] = (byte)(value >> 16);
        return true;
    }
}