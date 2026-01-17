using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace CHttpServer.Http3;

internal struct QPackIntegerEncoder
{
    public static bool TryEncode(Span<byte> destination, long number, byte prefixLength, out int writtenLength)
    {
        //if I < 2 ^ N - 1, encode I on N bits
        //else
        //    encode(2 ^ N - 1) on N bits
        //         I = I - (2 ^ N - 1)
        //    while I >= 128
        //         encode(I % 128 + 128) on 8 bits
        //         I = I / 128
        //    encode I on 8 bits

        Debug.Assert(1 <= prefixLength && prefixLength <= 8);
        Debug.Assert(number >= 0);
        writtenLength = 0;
        int prefixLimit = ((1 << prefixLength) - 1);
        if (destination.IsEmpty)
            return false;
        if (number < prefixLimit)
        {
            writtenLength = 1;
            destination[0] = (byte)(destination[0] | number);
            return true;
        }

        destination[0] = (byte)(destination[0] | prefixLimit);
        number -= prefixLimit;

        if (Avx2.IsSupported && number >= 2097183 && destination.Length >= Vector128<byte>.Count + 2) // Measured for AVX2
            return TryEncodeSimdLargeOnly(destination, number, prefixLength, out writtenLength);

        writtenLength = 1;
        while (number >= 128)
        {
            if (destination.Length < (uint)writtenLength)
                return false;
            destination[writtenLength] = (byte)((number % 128) | 0b1000_0000);
            number = number >> 7;
            writtenLength++;
        }
        if (destination.Length < (uint)writtenLength)
            return false;
        destination[writtenLength] = (byte)number;
        writtenLength++;
        return true;
    }

    private static Vector256<uint> Divisor = Vector256.Create([0u, 7u, 14u, 21u, 28u, 32u, 32u, 32u]);
    private static Vector256<int> Add128 = Vector256.Create(128);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryEncodeSimd(Span<byte> destination, int number, byte prefixLength, out int writtenCount)
    {
        //if I < 2 ^ N - 1, encode I on N bits
        //else
        //    encode(2 ^ N - 1) on N bits
        //         I = I - (2 ^ N - 1)
        //    while I >= 128
        //         encode(I % 128 + 128) on 8 bits
        //         I = I / 128
        //    encode I on 8 bits
        Debug.Assert(1 <= prefixLength && prefixLength <= 8);
        Debug.Assert(number >= 0);
        if (!Avx2.IsSupported)
            return TryEncode(destination, number, prefixLength, out writtenCount);
        writtenCount = 1;
        int prefixLimit = (1 << prefixLength) - 1;
        if (destination.Length < Vector128<byte>.Count + 1)
            return false;

        if (number < prefixLimit)
        {
            destination[0] = (byte)(destination[0] | number);
            return true;
        }

        destination[0] = (byte)(destination[0] | prefixLimit);
        number -= prefixLimit;

        var vNumber = Avx2.ShiftRightLogicalVariable(Vector256.Create(number), Divisor);
        var quotient128 = Avx2.ShiftRightLogical(vNumber, 7); // Div
        var vRemainder = Avx2.Or(vNumber - Avx2.ShiftLeftLogical(quotient128, 7), Add128);

        Vector128<short> packedShort = Avx2.PackSignedSaturate(vRemainder.GetLower(), vRemainder.GetUpper());
        Vector128<byte> narrowBytes = Avx2.PackUnsignedSaturate(packedShort, packedShort);
        narrowBytes.StoreUnsafe(ref destination[1]);

        var vLength = Avx2.CompareGreaterThan(Add128, vNumber).AsByte();
        writtenCount = 2 + (int.TrailingZeroCount(Avx2.MoveMask(vLength)) >> 2);
        destination[writtenCount - 1] -= 128;
        return true;
    }

    private static Vector256<uint> DivisorLong = Vector256.Create([0u, 7u, 14u, 21u, 0u, 7u, 14u, 21u]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryEncodeSimd(Span<byte> destination, long number, byte prefixLength, out int writtenCount)
    {
        //if I < 2 ^ N - 1, encode I on N bits
        //else
        //    encode(2 ^ N - 1) on N bits
        //         I = I - (2 ^ N - 1)
        //    while I >= 128
        //         encode(I % 128 + 128) on 8 bits
        //         I = I / 128
        //    encode I on 8 bits
        Debug.Assert(1 <= prefixLength && prefixLength <= 8);
        Debug.Assert(number >= 0);

        if (!Avx2.IsSupported)
            return TryEncode(destination, number, prefixLength, out writtenCount);

        writtenCount = 1;
        int prefixLimit = (1 << prefixLength) - 1;
        if (destination.Length < Vector128<byte>.Count + 2)
            return false;

        if (number < prefixLimit)
        {
            destination[0] = (byte)(destination[0] | number);
            return true;
        }

        destination[0] = (byte)(destination[0] | prefixLimit);
        number -= prefixLimit;

        int upperLane = (int)(number >> 28);
        int lowerLane = (int)(number & int.MaxValue);
        var vNumber = Avx2.ShiftRightLogicalVariable(Vector256.Create(Vector128.Create(lowerLane), Vector128.Create(upperLane)), DivisorLong);
        var quotient128 = Avx2.ShiftRightLogical(vNumber, 7); // Div
        var vRemainder = Avx2.Or(vNumber - Avx2.ShiftLeftLogical(quotient128, 7), Add128);

        Vector128<short> packedShort = Avx2.PackSignedSaturate(vRemainder.GetLower(), vRemainder.GetUpper());
        Vector128<byte> narrowBytes = Avx2.PackUnsignedSaturate(packedShort, packedShort);
        narrowBytes.StoreUnsafe(ref destination[1]);

        var vLength = Avx2.CompareGreaterThan(vNumber, Vector256<int>.Zero).AsByte();
        writtenCount = 9 - (int.LeadingZeroCount(Avx2.MoveMask(vLength)) >> 2);
        if (writtenCount == 9)
            destination[writtenCount++] = (byte)(number >> 56);
        else
            destination[writtenCount - 1] -= 128;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryEncodeSimdLargeOnly(Span<byte> destination, long number, byte prefixLength, out int writtenCount)
    {
        //if I < 2 ^ N - 1, encode I on N bits
        //else
        //    encode(2 ^ N - 1) on N bits
        //         I = I - (2 ^ N - 1)
        //    while I >= 128
        //         encode(I % 128 + 128) on 8 bits
        //         I = I / 128
        //    encode I on 8 bits
        Debug.Assert(1 <= prefixLength && prefixLength <= 8);
        Debug.Assert(number >= 2097183);
        Debug.Assert(destination.Length >= Vector128<byte>.Count + 2);

        int upperLane = (int)(number >> 28);
        int lowerLane = (int)(number & int.MaxValue);
        var vNumber = Avx2.ShiftRightLogicalVariable(Vector256.Create(Vector128.Create(lowerLane), Vector128.Create(upperLane)), DivisorLong);
        var quotient128 = Avx2.ShiftRightLogical(vNumber, 7); // Div
        var vRemainder = Avx2.Or(vNumber - Avx2.ShiftLeftLogical(quotient128, 7), Add128);

        Vector128<short> packedShort = Avx2.PackSignedSaturate(vRemainder.GetLower(), vRemainder.GetUpper());
        Vector128<byte> narrowBytes = Avx2.PackUnsignedSaturate(packedShort, packedShort);
        narrowBytes.StoreUnsafe(ref destination[1]);

        var vLength = Avx2.CompareGreaterThan(vNumber, Vector256<int>.Zero).AsByte();
        writtenCount = 9 - (int.LeadingZeroCount(Avx2.MoveMask(vLength)) >> 2);
        if (writtenCount == 9)
            destination[writtenCount++] = (byte)(number >> 56);
        else
            destination[writtenCount - 1] -= 128;
        return true;
    }
}