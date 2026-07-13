using CHttpServer.Http3;

namespace CHttpServer.Tests.Http3;

public class Http3ResponseHeaderCollectionTests
{
    [Theory]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('\u0000')] // NUL
    [InlineData('\u0001')] // SOStart of HeadingH
    [InlineData('ú')]
    [InlineData('ö')]
    [InlineData('\u0008')] // Backspace
    [InlineData('\u0010')] // New Line
    [InlineData(']')]
    [InlineData('<')]
    [InlineData(';')]
    public void InvalidKeyChars_Rejected(char invalidChar)
    {
        var sut = new Http3ResponseHeaderCollection();
        Assert.Throws<ArgumentException>(() => sut.Add($"abc{invalidChar}", "ab"));
        Assert.Throws<ArgumentException>(() => sut[$"abc{invalidChar}"] = "ab");
    }

    [Theory]
    [InlineData('\u0000')] // NUL
    [InlineData('\u0001')] // Start of Heading
    [InlineData('\u0008')] // Backspace
    [InlineData('\u000A')] // New Line
    [InlineData('\u001B')] // ESC
    public void InvalidValueChars_Rejected(char invalidChar)
    {
        var sut = new Http3ResponseHeaderCollection();
        Assert.Throws<ArgumentException>(() => sut.Add("abc", $"ab{invalidChar}"));
        Assert.Throws<ArgumentException>(() => sut["abc"] = $"ab{invalidChar}");
    }
}
