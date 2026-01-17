using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

public class QPackIntegerEncoderTests
{
    [Fact]
    public void Encode1337()
    {
        Span<byte> buffer = stackalloc byte[8];
        Assert.True(QPackIntegerEncoder.TryEncode(buffer, 1337, 5, out var length));
        byte[] b = [0b00011111, 0b10011010, 0b00001010];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void Encode2()
    {
        Span<byte> buffer = stackalloc byte[8];
        Assert.True(QPackIntegerEncoder.TryEncode(buffer, 2, 1, out var length));
        byte[] b = [0b00000001, 0b00000001];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void Encode167225()
    {
        Span<byte> buffer = stackalloc byte[8];
        Assert.True(QPackIntegerEncoder.TryEncode(buffer, 167225, 5, out var length));
        byte[] b = [0b00011111, 0b10011010, 0b10011010, 0b00001010];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void Encode0()
    {
        Span<byte> buffer = stackalloc byte[8];
        Assert.True(QPackIntegerEncoder.TryEncode(buffer, 0, 5, out var length));
        byte[] b = [0b00000000];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void Encode2147483647_7()
    {
        Span<byte> buffer = stackalloc byte[8];
        Assert.True(QPackIntegerEncoder.TryEncode(buffer, 2147483647, 7, out var length));
        byte[] b = [0b0111_1111, 0b10000000, 0b11111111, 0b11111111, 0b11111111, 0b00000111];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void Encode2147483647_1()
    {
        Span<byte> buffer = stackalloc byte[8];
        Assert.True(QPackIntegerEncoder.TryEncode(buffer, 2147483647, 1, out var length));
        byte[] b = [0b0000_0001, 0b11111110, 0b11111111, 0b11111111, 0b11111111, 0b00000111];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void Encode2147483647_7_Buffer32()
    {
        Span<byte> buffer = stackalloc byte[32];
        Assert.True(QPackIntegerEncoder.TryEncode(buffer, 2147483647, 7, out var length));
        byte[] b = [0b0111_1111, 0b10000000, 0b11111111, 0b11111111, 0b11111111, 0b00000111];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void Encode2147483647_1_Buffer32()
    {
        Span<byte> buffer = stackalloc byte[32];
        Assert.True(QPackIntegerEncoder.TryEncode(buffer, 2147483647, 1, out var length));
        byte[] b = [0b0000_0001, 0b11111110, 0b11111111, 0b11111111, 0b11111111, 0b00000111];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void EncodeSimd1337()
    {
        Span<byte> buffer = stackalloc byte[33];
        QPackIntegerEncoder.TryEncodeSimd(buffer, 1337, 5, out var length);
        byte[] b = [0b00011111, 0b10011010, 0b00001010];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }


    [Fact]
    public void EncodeSimd2()
    {
        Span<byte> buffer = stackalloc byte[33];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, 2, 1, out var length));
        byte[] b = [0b00000001, 0b00000001];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void EncodeSimd167225()
    {
        Span<byte> buffer = stackalloc byte[33];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, 167225, 5, out var length));
        byte[] b = [0b00011111, 0b10011010, 0b10011010, 0b00001010];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void EncodeSimd0()
    {
        Span<byte> buffer = stackalloc byte[33];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, 0, 5, out var length));
        byte[] b = [0b00000000];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void EncodeSimd2147483647_7()
    {
        Span<byte> buffer = stackalloc byte[33];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, 2147483647, 7, out var length));
        byte[] b = [0b0111_1111, 0b10000000, 0b11111111, 0b11111111, 0b11111111, 0b00000111];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void EncodeSimd2147483647_1()
    {
        Span<byte> buffer = stackalloc byte[33];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, 2147483647, 1, out var length));
        byte[] b = [0b0000_0001, 0b11111110, 0b11111111, 0b11111111, 0b11111111, 0b00000111];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }
    [Fact]
    public void Encode_Decode_Simd()
    {
        Span<byte> buffer = stackalloc byte[64];
        for (int i = 1; i < int.MaxValue - 127; i += 127)
        {
            buffer.Clear();
            Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, i, 5, out var length));
            var decoder = new QPackIntegerDecoder();
            if (decoder.BeginTryDecode(buffer[0], 5, out var result))
                Assert.Equal(i, result);
            else
            {
                int offset = 1;
                Assert.True(decoder.TryDecodeInteger(buffer, ref offset, out result));
                Assert.Equal(i, result);
            }
        }
    }

    [Fact]
    public void Encode_Decode()
    {
        Span<byte> buffer = stackalloc byte[64];
        for (int i = 1; i < int.MaxValue - 127; i += 127)
        {
            buffer.Clear();
            Assert.True(QPackIntegerEncoder.TryEncode(buffer, i, 5, out var length));
            var decoder = new QPackIntegerDecoder();
            if (decoder.BeginTryDecode(buffer[0], 5, out var result))
                Assert.Equal(i, result);
            else
            {
                int offset = 1;
                Assert.True(decoder.TryDecodeInteger(buffer, ref offset, out result));
                Assert.Equal(i, result);
            }
        }
    }

    [Fact]
    public void EncodeSimd1337_Long()
    {
        Span<byte> buffer = stackalloc byte[128];
        QPackIntegerEncoder.TryEncodeSimd(buffer, 1337L, 5, out var length);
        byte[] b = [0b00011111, 0b10011010, 0b00001010];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }


    [Fact]
    public void EncodeSimd2_Long()
    {
        Span<byte> buffer = stackalloc byte[128];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, 2L, 1, out var length));
        byte[] b = [0b00000001, 0b00000001];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void EncodeSimd167225_Long()
    {
        Span<byte> buffer = stackalloc byte[128];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, 167225L, 5, out var length));
        byte[] b = [0b00011111, 0b10011010, 0b10011010, 0b00001010];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void EncodeSimd0_Long()
    {
        Span<byte> buffer = stackalloc byte[128];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, 0L, 5, out var length));
        byte[] b = [0b00000000];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void EncodeSimd2147483647_7_Long()
    {
        Span<byte> buffer = stackalloc byte[128];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, 2147483647, 7, out var length));
        byte[] b = [0b0111_1111, 0b10000000, 0b11111111, 0b11111111, 0b11111111, 0b00000111];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void EncodeSimd2147483647_Prefix1_Long()
    {
        Span<byte> buffer = stackalloc byte[128];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, 2147483647L, 1, out var length));
        byte[] b = [0b0000_0001, 0b11111110, 0b11111111, 0b11111111, 0b11111111, 0b00000111];
        Assert.True(b.SequenceEqual(buffer[..length]));
    }

    [Fact]
    public void Encode_Decode_SimdLong()
    {
        Span<byte> buffer = stackalloc byte[128];
        for (long i = 1; i < uint.MaxValue; i += 127)
        {
            buffer.Clear();
            Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, i, 5, out var length));
            var decoder = new QPackIntegerDecoder();
            if (decoder.BeginTryDecode(buffer[0], 5, out var result))
                Assert.Equal(i, result);
            else
            {
                int offset = 1;
                Assert.True(decoder.TryDecode62Bits(buffer, ref offset, out long longResult));
                Assert.Equal(i, longResult);
            }
        }
    }

    [Fact]
    public void Encode_Decode_LongMax()
    {
        long input = long.MaxValue >> 1;
        Span<byte> buffer = stackalloc byte[128];
        Assert.True(QPackIntegerEncoder.TryEncodeSimd(buffer, input, 1, out var length));
        var decoder = new QPackIntegerDecoder();
        decoder.BeginTryDecode(buffer[0], 1, out var _);
        int offset = 1;
        Assert.True(decoder.TryDecode62Bits(buffer, ref offset, out long result));
        Assert.Equal(input, result);
    }
}