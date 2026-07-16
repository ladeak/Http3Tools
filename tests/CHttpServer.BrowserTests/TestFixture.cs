using System.Runtime.Versioning;
using CHttpServer.Tests;
using Microsoft.Playwright;

namespace TestCHttpServerApplication.Tests;

public class TestFixture : IAsyncDisposable
{
    public TestServer? Server { get; private set; }

    public IPlaywright PlaywrightHost { get; private set; }

    public IBrowserContext Browser { get; private set; }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    public TestFixture()
    {
        Server = new TestServer();
        Server.RunAsync(7297, false, useHttp3: true).GetAwaiter().GetResult();
        PlaywrightHost = Playwright.CreateAsync().GetAwaiter().GetResult();
        Browser = PlaywrightHost.Chromium.LaunchPersistentContextAsync("/tmp/chrome-profile-integrationtest", new()
        {
            Headless = false,
            Args = ["--origin-to-force-quic-on=127.0.0.1:7297", "--ignore-certificate-errors-spki-list=5QveYGg8xaCnnZWvkC9Y6v9lQVmF2BCozvds6Cn6F6k="]
        }).GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Server == null)
            return;
        await Server.DisposeAsync();
        await Browser.DisposeAsync();
        PlaywrightHost.Dispose();
    }
}
