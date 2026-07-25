using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using CHttpServer.Tests;
using Microsoft.Playwright;

namespace CHttpServer.BrowserTests;

public abstract class Http2TestFixtureBase : IAsyncDisposable
{
    internal int Port { get; }

    public TestServer Server { get; private set; }

    public IPlaywright PlaywrightHost { get; private set; }

    public IBrowserContext Browser { get; private set; }

    public Http2TestFixtureBase(int port)
    {
        Port = port;
        Server = new TestServer();
        Server.RunAsync(Port, false, useHttp3: false).GetAwaiter().GetResult();
        PlaywrightHost = Playwright.CreateAsync().GetAwaiter().GetResult();
        Browser = CreateBrowserContextAsync().GetAwaiter().GetResult();
    }

    protected abstract Task<IBrowserContext> CreateBrowserContextAsync();

    public async virtual ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync();
        PlaywrightHost.Dispose();
        await Server.DisposeAsync();
    }
}

[method: SupportedOSPlatform("linux")]
[method: SupportedOSPlatform("windows")]
public sealed class Http2ChromeTestFixture() : Http2TestFixtureBase(7294)
{
    protected override Task<IBrowserContext> CreateBrowserContextAsync()
    {
        return PlaywrightHost.Chromium.LaunchPersistentContextAsync("/tmp/chrome-profile-integrationtest", new()
        {
            Headless = true,
            Args = ["--ignore-certificate-errors-spki-list=5QveYGg8xaCnnZWvkC9Y6v9lQVmF2BCozvds6Cn6F6k="]
        });
    }
}

[method: SupportedOSPlatform("linux")]
[method: SupportedOSPlatform("windows")]
public sealed class Http2FirefoxTestFixture() : Http2TestFixtureBase(7293)
{
    protected override async Task<IBrowserContext> CreateBrowserContextAsync()
    {
        var browser = await PlaywrightHost.Firefox.LaunchAsync(new()
        {
            Headless = true
        });
        return await browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true
        });
    }
}