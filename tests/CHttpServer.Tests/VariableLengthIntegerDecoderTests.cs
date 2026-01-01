using CHttpServer.Http3;

namespace CHttpServer.Tests;

public class VariableLengthIntegerDecoderTests
{
    [Theory]
    [InlineData("c2197c5eff14e88c", 151288809941952652UL, 8)]
    [InlineData("9d7f3e7d", 494878333UL, 4)]
    [InlineData("7bbd", 15293UL, 2)]
    [InlineData("25", 37UL, 1)]
    [InlineData("4025", 37UL, 2)]
    [InlineData("3F", 63UL, 1)]
    [InlineData("3FFF", 63UL, 1)]
    [InlineData("00", 0UL, 1)]
    [InlineData("000000", 0UL, 1)]
    public void TryRead_Success(string hexInput, ulong expectedValue, int expectedBytesRead)
    {
        byte[] buffer = Convert.FromHexString(hexInput);
        bool result = VariableLenghtIntegerDecoder.TryRead(buffer, out ulong value, out int bytesRead);
        Assert.True(result);
        Assert.Equal(expectedValue, value);
        Assert.Equal(expectedBytesRead, bytesRead);
    }

    [Theory]
    [InlineData("C0")]
    [InlineData("C000")]
    [InlineData("C000FF")]
    [InlineData("C000FF00")]
    [InlineData("C000FF00EE")]
    [InlineData("C000FF00FF00")]
    [InlineData("C000FF00FF00DD")]
    [InlineData("8000")]
    [InlineData("80FF")]
    [InlineData("80FFFF")]
    [InlineData("80")]
    [InlineData("40")]
    public void TryRead_Invalid(string hexInput)
    {
        byte[] buffer = Convert.FromHexString(hexInput);
        Assert.False(VariableLenghtIntegerDecoder.TryRead(buffer, out _, out _));
    }

    [Theory]
    [InlineData("c2197c5eff14e88c", 151288809941952652UL, 8)]
    [InlineData("9d7f3e7d", 494878333UL, 4)]
    [InlineData("7bbd", 15293UL, 2)]
    [InlineData("25", 37UL, 1)]
    [InlineData("3F", 63UL, 1)]
    [InlineData("00", 0UL, 1)]
    public void TryWrite(string expectedValue, ulong input, int expectedBytesWritten)
    {
        Span<byte> destination = stackalloc byte[16];
        Assert.True(VariableLenghtIntegerDecoder.TryWrite(destination, input, out var bytesWritten));
        Assert.Equal(expectedBytesWritten, bytesWritten);
        Convert.FromHexString(expectedValue).SequenceEqual(destination[0..bytesWritten]);
    }

    [Theory]
    [InlineData(151288809941952652UL, 8)]
    [InlineData(494878333UL, 4)]
    [InlineData(15293UL, 2)]
    [InlineData(37UL, 1)]
    [InlineData(63UL, 1)]
    [InlineData(0UL, 1)]
    public void TryWrite_DestinationTooSmall(ulong input, int expectedBytesWritten)
    {
        Span<byte> destination = stackalloc byte[expectedBytesWritten - 1];
        Assert.False(VariableLenghtIntegerDecoder.TryWrite(destination, input, out var bytesWritten));
    }


    [Fact]
    public void TryWrite_Read()
    {
        Span<byte> destination = stackalloc byte[16];
        for (ulong i = 0; i < int.MaxValue + 63UL; i += 62)
        {
            Assert.True(VariableLenghtIntegerDecoder.TryWrite(destination, i, out var bytesWritten));
            Assert.True(VariableLenghtIntegerDecoder.TryRead(destination, out var value, out var bytesRead));
            Assert.Equal(i, value);
            Assert.Equal(bytesWritten, bytesRead);
        }
    }

    [Theory]
    [InlineData("7bbd", 15293, 2)]
    [InlineData("25", 37, 1)]
    [InlineData("3F", 63, 1)]
    [InlineData("00", 0, 1)]
    public void TryWrite_Generic_Short(string expectedValue, short input, int expectedBytesWritten)
    {
        Span<byte> destination = stackalloc byte[16];
        Assert.True(VariableLenghtIntegerDecoder.TryWrite(destination, input, out var bytesWritten));
        Assert.Equal(expectedBytesWritten, bytesWritten);
        Convert.FromHexString(expectedValue).SequenceEqual(destination[0..bytesWritten]);
    }


    [Theory]
    [InlineData("9d7f3e7d", 494878333, 4)]
    [InlineData("7bbd", 15293, 2)]
    [InlineData("25", 37, 1)]
    [InlineData("3F", 63, 1)]
    [InlineData("00", 0, 1)]
    public void TryWrite_Generic_Int(string expectedValue, int input, int expectedBytesWritten)
    {
        Span<byte> destination = stackalloc byte[16];
        Assert.True(VariableLenghtIntegerDecoder.TryWrite(destination, input, out var bytesWritten));
        Assert.Equal(expectedBytesWritten, bytesWritten);
        Convert.FromHexString(expectedValue).SequenceEqual(destination[0..bytesWritten]);
    }


    [Theory]
    [InlineData("c2197c5eff14e88c", 151288809941952652, 8)]
    [InlineData("9d7f3e7d", 494878333, 4)]
    [InlineData("7bbd", 15293, 2)]
    [InlineData("25", 37, 1)]
    [InlineData("3F", 63, 1)]
    [InlineData("00", 0, 1)]
    public void TryWrite_Generic_Long(string expectedValue, long input, int expectedBytesWritten)
    {
        Span<byte> destination = stackalloc byte[16];
        Assert.True(VariableLenghtIntegerDecoder.TryWrite(destination, input, out var bytesWritten));
        Assert.Equal(expectedBytesWritten, bytesWritten);
        Convert.FromHexString(expectedValue).SequenceEqual(destination[0..bytesWritten]);
    }

    [Theory]
    [InlineData(151288809941952652L, 8)]
    [InlineData(494878333L, 4)]
    [InlineData(15293L, 2)]
    [InlineData(37L, 1)]
    [InlineData(63L, 1)]
    [InlineData(0L, 1)]
    public void TryWrite_Generic_DestinationTooSmall(long input, int expectedBytesWritten)
    {
        Span<byte> destination = stackalloc byte[expectedBytesWritten - 1];
        Assert.False(VariableLenghtIntegerDecoder.TryWrite(destination, input, out var bytesWritten));
    }

    [Fact]
    public void TryWrite_Generic_Read()
    {
        Span<byte> destination = stackalloc byte[16];
        for (long i = 0; i < int.MaxValue + 63L; i += 62)
        {
            Assert.True(VariableLenghtIntegerDecoder.TryWrite(destination, i, out var bytesWritten));
            Assert.True(VariableLenghtIntegerDecoder.TryRead(destination, out var value, out var bytesRead));
            Assert.Equal(i, checked((long)value));
            Assert.Equal(bytesWritten, bytesRead);
        }
    }

    [Fact]
    public void TryWrite_Generic_NegativeValue_Throws()
    {
        Assert.Throws<OverflowException>(() => VariableLenghtIntegerDecoder.TryWrite(new byte[16], -1, out var bytesWritten));
        Assert.Throws<OverflowException>(() => VariableLenghtIntegerDecoder.TryWrite(new byte[16], (short)-1, out var bytesWritten));
        Assert.Throws<OverflowException>(() => VariableLenghtIntegerDecoder.TryWrite(new byte[16], (long)-1, out var bytesWritten));
        Assert.Throws<OverflowException>(() => VariableLenghtIntegerDecoder.TryWrite(new byte[16], (sbyte)-1, out var bytesWritten));
    }

}
