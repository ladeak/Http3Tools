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
}
