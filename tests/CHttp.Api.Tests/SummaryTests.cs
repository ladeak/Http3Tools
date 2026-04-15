using CHttp.Data;
using Xunit;

namespace CHttp.Api.Tests;

public class SummaryTests
{
    [Fact]
    public void Empty_ToString()
    {
        var sut = new Summary("");
        sut.ToString();
    }

    [Theory]
    [InlineData("https://some-very-long-url.atsomedomain:1234/path/segment/controller/action")]
    public void Url_ToString(string url)
    {
        var sut = new Summary(url);
        var result = sut.ToString();
        Assert.Contains(url, result);
    }

    [Fact]
    public void UrlStartEndTime_ToString()
    {
        var sut = new Summary("https://some-very-long-url.atsomedomain:1234/path/segment/controller/action");
        sut.Length = int.MaxValue;
        sut.RequestCompleted(System.Net.HttpStatusCode.OK);
        var result = sut.ToString();
        Assert.Contains("https://some-very-long-url.atsomedomain:1234/path/segment/controller/action",
            result);
        Assert.Contains("2.00GB", result);
    }

    [Fact]
    public void SizeFormatted_ToString()
    {
        var sut = new Summary("https://localhost:1234/path/segment/controller/action");
        sut.Length = (int)(1024 * 1024 * 1.65);
        sut.RequestCompleted(System.Net.HttpStatusCode.OK);
        var result = sut.ToString();
        Assert.Contains("1.65MB", result);
    }
}
