using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace CHttpServer.Tests;

[Collection(nameof(CHttpServerIntegrationTests))]
[CollectionDefinition(DisableParallelization = true)]
public class CHttpServerIntegrationTests : IClassFixture<TestServer>
{
    private readonly TestServer _server;

    public CHttpServerIntegrationTests(TestServer testServer)
    {
        _server = testServer;
        _server.RunAsync();
    }

    private HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            // Matching testCert.pfx
            ServerCertificateCustomValidationCallback = (message, certificate, chain, sslPolicyErrors) => certificate?.Issuer == "CN=localhost"
        };
        return new HttpClient(handler);
    }

    [Fact]
    public async Task Get_NoContent()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/nocontent") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Get_Content()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/content") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("\"some content\"", content);
    }

    [Fact]
    public async Task Get_NoStatusCode()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/nostatuscode") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task TestPost()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost:7222/post") { Version = HttpVersion.Version20, Content = JsonContent.Create(new WeatherForecast(new DateOnly(2025, 01, 01), 22, "sunny")) };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task HttpContext_WritesResponse()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/httpcontext") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("some content", content);
    }

    [Fact]
    public async Task HttpContext_StreamsResponse()
    {
        var client = CreateClient();
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
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/stream") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("some contentsome content2", content);
    }

    [Fact]
    public async Task IAsyncEnumerable()
    {
        var client = CreateClient();
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
        var client = CreateClient();
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

    [Fact]
    public async Task Get_Content_TwoParallelRequests_SameConnection()
    {
        var client = CreateClient();
        var request0 = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/content") { Version = HttpVersion.Version20 };
        var request1 = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/content") { Version = HttpVersion.Version20 };

        var response0Task = client.SendAsync(request0, TestContext.Current.CancellationToken);
        var response1Task = client.SendAsync(request1, TestContext.Current.CancellationToken);
        await Task.WhenAll(response0Task, response1Task);
        var response0 = await response0Task;
        var response1 = await response1Task;

        var content0 = await response0.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response0.IsSuccessStatusCode);
        Assert.Equal("\"some content\"", content0);

        var content1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response1.IsSuccessStatusCode);
        Assert.Equal("\"some content\"", content1);
    }

    [Fact]
    public async Task Get_Content_ParallelRequests_NewConnection()
    {
        for (int i = 0; i < 10; i++)
        {
            var client = CreateClient();
            var request0 = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/content") { Version = HttpVersion.Version20 };
            var request1 = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/content") { Version = HttpVersion.Version20 };

            var response0Task = client.SendAsync(request0, TestContext.Current.CancellationToken);
            var response1Task = client.SendAsync(request1, TestContext.Current.CancellationToken);
            await Task.WhenAll(response0Task, response1Task);
            var response0 = await response0Task;
            var response1 = await response1Task;

            var content0 = await response0.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response0.IsSuccessStatusCode);
            Assert.Equal("\"some content\"", content0);

            var content1 = await response1.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response1.IsSuccessStatusCode);
            Assert.Equal("\"some content\"", content1);
        }
    }

    [Fact]
    public async Task Header_And_Trailers()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/headerstrailers") { Version = HttpVersion.Version20 };
        request.Headers.Add("x-custom", "custom-header-value");
        request.Headers.Accept.Add(new("application/json"));
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.TryGetValues("x-custom-response", out var values) && values.First() == "custom-header-value");
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.TrailingHeaders.TryGetValues("x-trailer", out values) && values.First() == "mytrailer");
    }

    [Fact]
    public async Task LargerInput_Than_FlowControl()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/readallinput") { Version = HttpVersion.Version20 };
        request.Content = new ByteArrayContent(new byte[5000]); //10_000_000
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
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
        builder.WebHost.UseCHttpServer(o => { o.Port = 7222; o.Certificate = X509CertificateLoader.LoadPkcs12FromFile("testCert.pfx", "testPassword"); });

        // Use Kestrel:
        //builder.WebHost.UseKestrel(o =>
        //{
        //    o.Listen(IPAddress.Loopback, 7222, lo =>
        //    {
        //        lo.UseHttps();
        //        lo.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
        //    });
        //});
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
        _app.MapGet("/headerstrailers", async (HttpContext ctx) =>
        {
            if (!ctx.Request.Headers.Accept.Contains("application/json") || ctx.Request.Headers["x-custom"] != "custom-header-value")
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            ctx.Response.Headers.TryAdd("x-custom-response", "custom-header-value");
            ctx.Response.Headers.ContentType = "application/json";
            ctx.Response.DeclareTrailer("x-trailer");
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("some content");
            ctx.Response.AppendTrailer("x-trailer", new Microsoft.Extensions.Primitives.StringValues("mytrailer"));

        });
        _app.MapGet("/readallinput", async (HttpContext ctx) =>
        {
            await ctx.Request.BodyReader.CopyToAsync(Stream.Null);
            ctx.Response.StatusCode = 204;
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