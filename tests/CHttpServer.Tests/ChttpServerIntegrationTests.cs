using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CHttpServer.Tests;

public class ChttpServerIntegrationTests
{
    [Fact]
    [global::System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1051:Calls to methods which accept CancellationToken should use TestContext.Current.CancellationToken", Justification = "<Pending>")]
    public async Task TestGet()
    {
        await using var server = new TestServer();
        var serverRun = server.RunAsync();
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/path") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        //var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }
}

public class TestServer : IAsyncDisposable
{
    private WebApplication? _app;

    public Task RunAsync()
    {
        if (_app != null)
            throw new InvalidOperationException("Server is already running");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseCHttpServer(o => { o.Port = 7222; });
        _app = builder.Build();
        _app.UseHttpsRedirection();
        _app.MapGet("/path", () =>
        {
            return TypedResults.NoContent();
        });
        return _app.RunAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app == null)
            return;
        await _app.StopAsync();
    }
}