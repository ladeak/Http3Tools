using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace CHttpServer.Http3;

internal struct QPackIntegerDecoder
{
    private const string BadInteger = "Bad Integer";
    private long _i;
    private byte _m;

    /// <summary>
    /// Decodes the first byte of the integer.
    /// </summary>
    public bool BeginTryDecode(byte b, int prefixLength, out int result)
    {
        Debug.Assert(prefixLength >= 1 && prefixLength <= 8);
        Debug.Assert((b & ~((1 << prefixLength) - 1)) == 0, "bits other than prefix data must be set to 0.");

        if (b < ((1 << prefixLength) - 1))
        {
            result = b;
            return true;
        }

        _i = b;
        _m = 0;
        result = 0;
        return false;
    }

    /// <summary>
    /// Decodes the first byte of the integer.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="currentIndex">The already parsed section.</param>
    /// <param name="result">The result number.</param>
    /// <returns>Returns true when a number is successfully decoded, returns false if the input data is incomplete.</returns>
    public bool TryDecodeInteger(ReadOnlySpan<byte> data, ref int currentIndex, out int result)
    {
        for (; currentIndex < data.Length; currentIndex++)
        {
            if (TryDecodeInteger(data[currentIndex], out result))
            {
                currentIndex++;
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Decodes the first byte of the integer.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="currentIndex">The already parsed section.</param>
    /// <param name="result">The result number.</param>
    /// <returns>Returns true when a number is successfully decoded, returns false if the input data is incomplete.</returns>
    public bool TryDecodeInteger(ReadOnlySequence<byte> data, ref int currentIndex, out int result)
    {
        // Fast path, process the first span.
        var buffer = data.FirstSpan;
        for (; currentIndex < buffer.Length; currentIndex++)
        {
            if (TryDecodeInteger(buffer[currentIndex], out result))
            {
                currentIndex++;
                return true;
            }
        }

        // Slow path, the first span is fully consumed.
        // Foreach iterates over the remaining segments.
        foreach (var segment in data.Slice(currentIndex))
        {
            var span = segment.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (TryDecodeInteger(span[i], out result))
                {
                    currentIndex++;
                    return true;
                }
            }
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Decodes the first byte of the integer.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="currentIndex">The already parsed section.</param>
    /// <param name="result">The result number.</param>
    /// <returns>Returns true when a number is successfully decoded, returns false if the input data is incomplete.</returns>
    public bool TryDecodeIntegerSimd(ReadOnlySpan<byte> buffer, ref int currentIndex, out int result)
    {
        if (Avx2.IsSupported && buffer.Length >= 32 + currentIndex)
            return TryDecodeSimd(buffer, ref currentIndex, out result);

        return TryDecodeInteger(buffer, ref currentIndex, out result);
    }

    /// <summary>
    /// Decodes the first byte of the integer.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="currentIndex">The already parsed section.</param>
    /// <param name="result">The result number.</param>
    /// <returns>Returns true when a number is successfully decoded, returns false if the input data is incomplete.</returns>
    public bool TryDecodeIntegerSimd(ReadOnlySequence<byte> data, ref int currentIndex, out int result)
    {
        // Fast path, process the first span.
        var buffer = data.FirstSpan;
        if (Avx2.IsSupported && buffer.Length >= 32 + currentIndex && buffer[currentIndex+1] >= 128) // The last condition makes sure that at least there are 3 total bytes.
            return TryDecodeSimd(buffer, ref currentIndex, out result);
        return TryDecodeInteger(buffer, ref currentIndex, out result);
    }

    /// <summary>
    /// Decodes the first byte of the integer.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="currentIndex">The already parsed section.</param>
    /// <param name="result">The result number.</param>
    /// <returns>Returns true when a number is successfully decoded, returns false if the input data is incomplete.</returns>
    public bool TryDecode62Bits(ReadOnlySpan<byte> data, ref int currentIndex, out long result)
    {
        for (; currentIndex < data.Length; currentIndex++)
        {
            if (TryDecode62Bits(data[currentIndex], out result))
            {
                currentIndex++;
                return true;
            }
        }

        result = default;
        return false;
    }

    private bool TryDecodeInteger(byte b, out int result)
    {
        // decode I from the next N bits
        // if I < 2^N - 1, return I
        // else
        // M = 0
        // repeat
        //   B = next octet
        //   I = I + (B & 127) * 2^M
        //   M = M + 7
        // while B & 128 == 128
        // return I

        if (BitOperations.LeadingZeroCount((uint)b) <= _m)
        {
            throw new HeaderDecodingException(BadInteger);
        }

        _i += ((long)(b & 0x7f) << _m);
        _m += 7;

        if ((b & 128) == 0)
        {
            if (b == 0 && _m / 7 > 1)
            {
                throw new HeaderDecodingException(BadInteger);
            }

            result = checked((int)_i);
            return true;
        }

        result = 0;
        return false;
    }

    private bool TryDecode62Bits(byte b, out long result)
    {
        // decode I from the next N bits
        // if I < 2^N - 1, return I
        // else
        // M = 0
        // repeat
        //   B = next octet
        //   I = I + (B & 127) * 2^M
        //   M = M + 7
        // while B & 128 == 128
        // return I

        int additionalBitsCount = 32 - BitOperations.LeadingZeroCount((uint)b);
        if (_m + additionalBitsCount > 62)
        {
            throw new HeaderDecodingException(BadInteger);
        }

        _i += ((long)(b & 0x7f) << _m);
        _m += 7;

        if ((b & 128) == 0)
        {
            if (b == 0 && _m / 7 > 1)
            {
                // Unneeded zeros not permitted.
                throw new HeaderDecodingException(BadInteger);
            }

            result = _i;
            return true;
        }

        result = 0;
        return false;
    }

    private bool TryDecodeSimd(ReadOnlySpan<byte> buffer, ref int currentIndex, out int result)
    {
        Debug.Assert(buffer.Length >= 32 + currentIndex);
        var vInput = Vector256.LoadUnsafe(in buffer[currentIndex]);
        var initialBits = Avx2.MoveMask(vInput);
        var byteCount = 32 - Lzcnt.LeadingZeroCount((uint)initialBits);
        vInput = Avx2.And(vInput, Mask);
        var vInputUShort = Vector256.WidenLower(vInput); // 32 -> 16
        var vCalcUInt = Vector256.WidenLower(vInputUShort); // 16 -> 8
        int bCount = (int)byteCount;
        if (byteCount > 5 || vCalcUInt[bCount] == 0)
            ThrowDecodingException();
        vCalcUInt = Avx2.ShiftLeftLogicalVariable(vCalcUInt, Multiplier); // Multiply by 2^M
        _i += Vector256.Sum(vCalcUInt);
        result = checked((int)_i);
        currentIndex += bCount + 1;
        return byteCount <= 4;
    }

    private void ThrowDecodingException() => throw new HeaderDecodingException(BadInteger);

    // Filters the most significant bit for the first 5 bytes, the rest is ignored.
    private static Vector256<byte> Mask = Vector256.Create([(byte)0x7F, (byte)0x7F, (byte)0x7F, (byte)0x7F, (byte)0x7F, (byte)0, (byte)0, (byte)0,
        (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0,
        (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0,
        (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0]);

    private static Vector256<uint> Multiplier = Vector256.Create([0u, 7u, 14u, 21u, 28u, 0u, 0u, 0u]);
}