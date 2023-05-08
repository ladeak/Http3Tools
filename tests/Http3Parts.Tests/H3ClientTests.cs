using CHttp.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Http3Parts.Tests;

[Collection(nameof(NonParallel))]
public class H3ClientTests
{
    [QuicSupported]
    public async Task Test1()
    {
        using var app = HttpServer.CreateHostBuilder(TestResponseAsync, HttpProtocols.Http3, port: 5001);
        await app.StartAsync();

        var client = new H3Client();
        await client.TestAsync();
    }

    private async Task TestResponseAsync(HttpContext context)
    {
        await context.Response.WriteAsync("hello world");
    }
}