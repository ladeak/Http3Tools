using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

public class QPackIntegerDecoderTests
{
    [Fact]
    public void Decode10()
    {
        byte b = 0b00001010;
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b, 5, out int result);
        Assert.Equal(10, result);
    }

    [Fact]
    public void Decode1337()
    {
        byte[] b = [0b00011111, 0b10011010, 0b00001010];
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b[0], 5, out _);
        var index = 1;
        decoder.TryDecodeInteger(b, ref index, out int result);
        Assert.Equal(1337, result);
    }

    [Fact]
    public void Decode1337_62bits()
    {
        byte[] b = [0b00011111, 0b10011010, 0b00001010];
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b[0], 5, out _);
        var index = 1;
        decoder.TryDecode62Bits(b, ref index, out long result);
        Assert.Equal(1337, result);
    }

    [Fact]
    public void Decode0()
    {
        byte[] b = [0b00000000];
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b[0], 5, out int result);
        Assert.Equal(0, result);
    }

    [Fact]
    public void DecodeMax()
    {
        byte[] b = [0b01111111, 0b11111111, 0b11111111, 0b11111111, 0b11111111, 0b11111111, 0b11111111, 0b11111111, 0b01111111];
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b[0], 7, out _);
        var index = 1;
        decoder.TryDecode62Bits(b, ref index, out long result);
        Assert.Equal(72057594037928062, result);
    }

    [Fact]
    public void Zeros_62Bits()
    {
        byte[] b = [0b01111111, 0b10000000, 0b00000001];
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b[0], 7, out _);
        var index = 1;
        decoder.TryDecode62Bits(b, ref index, out long result);
        Assert.Equal(255, result);
    }

    [Fact]
    public void Zeros()
    {
        byte[] b = [0b01111111, 0b10000000, 0b00000001];
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b[0], 7, out _);
        var index = 1;
        decoder.TryDecodeInteger(b, ref index, out int result);
        Assert.Equal(255, result);
    }

    [Fact]
    public void DecodeLimits_Zeros_62Bits()
    {
        byte[] b = [0b01111111, 0b10000000, 0b10000000, 0b00000000];
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b[0], 7, out _);
        var index = 1;
        Assert.Throws<HeaderDecodingException>(() => decoder.TryDecode62Bits(b, ref index, out long result));
    }

    [Fact]
    public void DecodeLimits_Length_62Bits()
    {
        byte[] b = [0b01111111, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b00000001];
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b[0], 7, out _);
        var index = 1;
        Assert.Throws<HeaderDecodingException>(() => decoder.TryDecode62Bits(b, ref index, out long result));
    }

    [Fact]
    public void DecodeLimits_Zeros()
    {
        byte[] b = [0b01111111, 0b10000000, 0b10000000, 0b00000000];
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b[0], 7, out _);
        var index = 1;
        Assert.Throws<HeaderDecodingException>(() => decoder.TryDecodeInteger(b, ref index, out int result));
    }

    [Fact]
    public void DecodeLimits_Length()
    {
        byte[] b = [0b01111111, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b10000000, 0b00000001];
        QPackIntegerDecoder decoder = new();
        decoder.BeginTryDecode(b[0], 7, out _);
        var index = 1;
        Assert.Throws<HeaderDecodingException>(() => decoder.TryDecodeInteger(b, ref index, out int result));
    }
}
