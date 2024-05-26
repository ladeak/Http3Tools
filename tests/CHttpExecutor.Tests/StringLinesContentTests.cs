using System.Text;

namespace CHttpExecutor.Tests;

public class StringLinesContentTests
{
    [Fact]
    public async Task SingleWrite()
    {
        List<string> input = ["test"];
        var sut = new StringLinesContent(input);
        using var ms = new MemoryStream();
        await sut.CopyToAsync(ms);
        ms.Seek(0, SeekOrigin.Begin);
        Assert.Equal(Encoding.UTF8.GetBytes("test"), ms.ToArray());
    }

    [Fact]
    public async Task TwoWrites()
    {
        List<string> input = ["test", "test"];
        var sut = new StringLinesContent(input);
        using var ms = new MemoryStream();
        await sut.CopyToAsync(ms);
        ms.Seek(0, SeekOrigin.Begin);
        Assert.Equal(Encoding.UTF8.GetBytes("testtest"), ms.ToArray());
    }

    [Fact]
    public async Task LargeContent()
    {
        List<string> input = ["test", new string('a', 8 * 1024), "test2"];
        var sut = new StringLinesContent(input);
        using var ms = new MemoryStream();
        await sut.CopyToAsync(ms);
        ms.Seek(0, SeekOrigin.Begin);
        StringBuilder sb = new();
        sb.Append(input[0]);
        sb.Append(input[1]);
        sb.Append(input[2]);
        Assert.Equal(Encoding.UTF8.GetBytes(sb.ToString()), ms.ToArray());
    }
}
