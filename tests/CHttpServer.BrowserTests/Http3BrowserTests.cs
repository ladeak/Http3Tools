namespace TestCHttpServerApplication.Tests;

public class Http3BrowserTests : IClassFixture<TestFixture>
{
    private readonly TestFixture _fixture;

    public Http3BrowserTests(TestFixture testFixture)
    {
        _fixture = testFixture;
    }

    [Fact]
    public async Task Test1()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await page.GotoAsync("https://127.0.0.1:7297/protocol");
        Assert.Contains("HTTP/3", (await page.ContentAsync()));
    }
}
