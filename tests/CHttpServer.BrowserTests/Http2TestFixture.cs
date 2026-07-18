using System.Runtime.Versioning;
using CHttpServer.Tests;
using Microsoft.Playwright;

namespace CHttpServer.BrowserTests;

public sealed class Http2TestFixture : IAsyncDisposable
{
    internal int Port => 7294;

    public TestServer? Server { get; private set; }

    public IPlaywright PlaywrightHost { get; private set; }

    public IBrowserContext Browser { get; private set; }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    public Http2TestFixture()
    {
        Server = new TestServer();
        Server.RunAsync(Port, false, useHttp3: false).GetAwaiter().GetResult();
        PlaywrightHost = Playwright.CreateAsync().GetAwaiter().GetResult();
        Browser = PlaywrightHost.Chromium.LaunchPersistentContextAsync("/tmp/chrome-profile-integrationtest", new()
        {
            Headless = true,
            Args = ["--ignore-certificate-errors-spki-list=5QveYGg8xaCnnZWvkC9Y6v9lQVmF2BCozvds6Cn6F6k="]
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
