using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;

namespace CHttpServer.Tests;


[Collection(nameof(ChttpServerIntegrationTests))]
[CollectionDefinition(DisableParallelization = true)]
public class ChttpServerIntegrationTests : IClassFixture<TestServer>
{
    private readonly CHttpServer.Tests.TestServer _server;

    public ChttpServerIntegrationTests(CHttpServer.Tests.TestServer testServer)
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