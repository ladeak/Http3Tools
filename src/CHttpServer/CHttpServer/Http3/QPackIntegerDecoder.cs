using System.Diagnostics;
using System.Numerics;

namespace CHttpServer.Http3;

internal struct QPackIntegerDecoder
{
    private ulong _i;
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
    /// Decodes subsequent bytes of an integer.
    /// </summary>
    public bool TryDecode(byte b, out int result)
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
            throw new HeaderDecodingException("Bad Integer");
        }

        _i += (ulong)((b & 0x7f) << _m);

        if (_i < 0)
        {
            throw new HeaderDecodingException("Bad Integer");
        }

        _m += 7;

        if ((b & 128) == 0)
        {
            if (b == 0 && _m / 7 > 1)
            {
                throw new HeaderDecodingException("Bad Integer");
            }

            result = (int)_i;
            return true;
        }

        result = 0;
        return false;
    }

    /// <summary>
    /// Decodes subsequent bytes of an integer.
    /// </summary>
    public bool TryDecode62Bits(byte b, out ulong result)
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
            throw new HeaderDecodingException("Bad Integer");
        }

        _i += (ulong)((b & 0x7f) << _m);

        if (_i < 0)
        {
            throw new HeaderDecodingException("Bad Integer");
        }

        _m += 7;

        if ((b & 128) == 0)
        {
            if (b == 0 && _m / 7 > 1)
            {
                // Unneeded zeros not permitted.
                throw new HeaderDecodingException("Bad Integer");
            }

            result = _i;
            return true;
        }

        result = 0;
        return false;
    }

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
}
