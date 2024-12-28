using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace CHttpServer.Tests;

[Collection(nameof(ChttpServerIntegrationTests))]
[CollectionDefinition(DisableParallelization = true)]
public class ChttpServerIntegrationTests : IClassFixture<TestServer>
{
    private readonly TestServer _server;

    public ChttpServerIntegrationTests(TestServer testServer)
    {
        _server = testServer;
        _server.RunAsync();
    }

    [Fact]
    public async Task Get_NoContent()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/nocontent") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Get_Content()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/content") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("\"some content\"", content);
    }

    [Fact]
    public async Task Get_NoStatusCode()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/nostatuscode") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task TestPost()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost:7222/post") { Version = HttpVersion.Version20, Content = JsonContent.Create(new WeatherForecast(new DateOnly(2025, 01, 01), 22, "sunny")) };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task HttpContext_WritesResponse()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/httpcontext") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("some content", content);
    }

    [Fact]
    public async Task HttpContext_StreamsResponse()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/stream") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        var buffer = new byte[12];
        await content.ReadExactlyAsync(buffer.AsMemory(), TestContext.Current.CancellationToken)
            .AsTask()
            .WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        Assert.Equal("some content", Encoding.UTF8.GetString(buffer));
    }

    [Fact]
    public async Task HttpContext_DoubleWrite()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/stream") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("some contentsome content2", content);
    }

    [Fact]
    public async Task IAsyncEnumerable()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/iasyncenumerable") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("""
                     ["some content","some content"]
                     """, content);
    }

    [Fact]
    public async Task Get_Content_TwiceSerial_SameConnection()
    {
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/content") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("\"some content\"", content);

        request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/content") { Version = HttpVersion.Version20 };
        response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("\"some content\"", content);
    }
}

public class TestServer : IAsyncDisposable, IDisposable
{
    private WebApplication? _app;

    public Task RunAsync()
    {
        if (_app != null)
            return Task.CompletedTask;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseCHttpServer(o => { o.Port = 7222; });
        _app = builder.Build();
        _app.MapGet("/nostatuscode", () =>
        {
            return "TypedResults.NoContent()";
        });
        _app.MapGet("/nocontent", () =>
        {
            return TypedResults.NoContent();
        });
        _app.MapGet("/content", () =>
        {
            return TypedResults.Ok("some content");
        });
        _app.MapPost("/post", ([FromBody] WeatherForecast body) =>
        {
            return TypedResults.NoContent();
        });
        _app.MapGet("/httpcontext", (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync("some content");
        });
        _app.MapGet("/stream", async (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("some content");
            await Task.Delay(1000);
            await ctx.Response.WriteAsync("some content2");
        });
        _app.MapGet("/iasyncenumerable", (HttpContext ctx) =>
        {
            async IAsyncEnumerable<string> GetStream()
            {
                foreach (var i in Enumerable.Range(0, 2))
                {
                    await Task.Delay(1000);
                    yield return "some content";
                }
            }
            return GetStream();
        });
        return _app.RunAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app == null)
            return;
        await _app.StopAsync();
        await _app.WaitForShutdownAsync();
    }

    public void Dispose()
    {
        DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }
}

internal record class WeatherForecast(DateOnly Date, int TemperatureC, string? Summary);