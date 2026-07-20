using Microsoft.Playwright;

namespace CHttpServer.BrowserTests;

public class Http3BrowserTests(Http3TestFixture testFixture) : IClassFixture<Http3TestFixture>
{
    private readonly Http3TestFixture _fixture = testFixture;
    private readonly string _url = $"https://127.0.0.1:{testFixture.Port}";

    [Fact]
    public async Task Http3Protocol()
    {
        for (int i = 0; i < 10; i++)
        {
            var page = await _fixture.Browser.NewPageAsync();
            await page.GotoAsync($"{_url}/protocol");
            Assert.Contains("HTTP/3", await page.ContentAsync());
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task Sse()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await page.GotoAsync($"{_url}/html");
        await page.ClickAsync("body > button:nth-child(3)");
        var sse = await page.WaitForSelectorAsync("#sse", new PageWaitForSelectorOptions() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.NotNull(sse);
        var content = await sse.InnerHTMLAsync();
        Assert.Contains("sse 0", content);
        Assert.Contains("sse 1", content);
        await page.CloseAsync();
    }

    [Fact]
    public async Task FormPost()
    {
        for (int i = 0; i < 10; i++)
        {
            var page = await _fixture.Browser.NewPageAsync();
            await page.GotoAsync($"{_url}/html");
            await page.RunAndWaitForResponseAsync(async () =>
            {
                await page.ClickAsync("body > form > button");
            }, $"{_url}/protocol");
            Assert.Contains("HTTP/3", await page.ContentAsync());
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task GetFetchJs()
    {
        for (int i = 0; i < 10; i++)
        {
            var page = await _fixture.Browser.NewPageAsync();
            await page.GotoAsync($"{_url}/html");
            await page.ClickAsync("body > button:nth-child(2)");
            var output = await page.WaitForSelectorAsync("#output", new PageWaitForSelectorOptions() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            Assert.NotNull(output);
            var content = await output.InnerHTMLAsync();
            Assert.Contains("\"some content\"", content);
            await page.CloseAsync();
        }
    }
}

