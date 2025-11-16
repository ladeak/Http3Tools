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
            if (TryDecode(data[currentIndex], out result))
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
            if (TryDecode(buffer[currentIndex], out result))
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
                if (TryDecode(span[i], out result))
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
    public bool TryDecodeIntegerSimd(Span<byte> buffer, ref int currentIndex, out int result)
    {
        if (Avx2.IsSupported && buffer.Length >= 32 + currentIndex)
            return TryDecodeSimd(buffer, ref currentIndex, out result);

        for (; currentIndex < buffer.Length; currentIndex++)
        {
            if (TryDecode(buffer[currentIndex], out result))
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

    private bool TryDecode(byte b, out int result)
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

    private bool TryDecodeSimd(Span<byte> buffer, ref int currentIndex, out int result)
    {
        Debug.Assert(buffer.Length >= 32 + currentIndex);
        var vInput = Vector256.LoadUnsafe(ref buffer[currentIndex]);
        var initialBits = Avx2.MoveMask(vInput);
        var byteCount = 32 - Lzcnt.LeadingZeroCount((uint)initialBits);
        vInput = Avx2.And(vInput, Mask);
        var vInputUShort = Vector256.WidenLower(vInput); // 32 -> 16
        var vCalcUInt = Vector256.WidenLower(vInputUShort); // 16 -> 8
        var activeLanes = byteCount switch
        {
            1 => Filter1,
            2 => Filter2,
            3 => Filter3,
            4 => FilterAll,
            _ => throw new HeaderDecodingException(BadInteger)
        };

        vCalcUInt = Avx2.And(vCalcUInt, activeLanes);
        vCalcUInt *= Multiplier;
        if (vCalcUInt[(int)byteCount] == 0)
            throw new HeaderDecodingException(BadInteger);
        _i += vCalcUInt[0] + vCalcUInt[1] + vCalcUInt[2] + vCalcUInt[3] + vCalcUInt[4];
        result = checked((int)_i);
        currentIndex += (int)byteCount + 1;
        return byteCount <= 4;
    }

    private static Vector256<byte> Mask = Vector256.Create((byte)0x7F);
    private static Vector256<uint> Multiplier = Vector256.Create([1u, 128u, 16384u, 2097152u, 268435456u, 0u, 0u, 0u]);
    private static Vector256<uint> Filter1 = Vector256.Create([uint.MaxValue, uint.MaxValue, 0u, 0u, 0u, 0u, 0u, 0u]);
    private static Vector256<uint> Filter2 = Vector256.Create([uint.MaxValue, uint.MaxValue, uint.MaxValue, 0u, 0u, 0u, 0u, 0u]);
    private static Vector256<uint> Filter3 = Vector256.Create([uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, 0u, 0u, 0u, 0u]);
    private static Vector256<uint> FilterAll = Vector256.Create([uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, 0u, 0u, 0u]);
}