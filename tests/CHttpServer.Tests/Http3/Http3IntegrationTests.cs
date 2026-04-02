using System.Net;
using System.Net.Http.Json;
using Microsoft.Net.Http.Headers;

namespace CHttpServer.Tests.Http3;

public class Http3IntegrationTests : IClassFixture<TestServer>
{
    private readonly int _port = 7224;
    private readonly TestServer _server;

    public Http3IntegrationTests(TestServer testServer)
    {
        _server = testServer;
        _server.RunAsync(_port, usePriority: false, useHttp3: true);
    }

    protected HttpClient CreateClient()
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
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/nocontent") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Get_NoStatusCode()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/nostatuscode") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("TypedResults.NoContent()", content);
    }

    [Fact]
    public async Task Get_Content()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/content") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("\"some content\"", content);
    }

    [Fact]
    public async Task Get_LargeResponse()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/getlargeresponse") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10_000_000, content.Length);
    }

    [Fact]
    public async Task LargeHeaderRequestResponse()
    {
        var headerName = "x-custom-header";
        var headerValue = new string('a', 40_000);
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/headersecho") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.Add(headerName, headerValue);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.TryGetValues(headerName, out var values) && values.Single() == headerValue);
    }

    [Fact]
    public async Task SmallHeaderRequestResponse()
    {
        var headerName0 = "x-custom-header0";
        var headerValue0 = "abc";
        var headerName1 = "x-custom-header1";
        var headerValue1 = "def";
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/headersecho") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.Add(headerName0, headerValue0);
        request.Headers.Add(headerName1, headerValue1);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.TryGetValues(headerName0, out var values0) && values0.Single() == headerValue0);
        Assert.True(response.Headers.TryGetValues(headerName1, out var values1) && values1.Single() == headerValue1);
    }

    [Fact]
    public async Task ManyLargeHeaderRequestResponse()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/headersack") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        for (int i = 0; i < 10; i++)
            request.Headers.Add($"x-custom-header-{i}", new string('a', i * 10000 + 1000));
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        for (int i = 0; i < 10; i++)
            Assert.True(response.Headers.TryGetValues($"x-custom-header-{i}", out var values));
    }

    [Fact]
    public async Task Header_And_Trailers()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/headerstrailers") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.Add("x-custom", "custom-header-value");
        request.Headers.Accept.Add(new("application/json"));
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.TryGetValues("x-custom-response", out var values) && values.First() == "custom-header-value");
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.TrailingHeaders.TryGetValues("x-trailer", out values) && values.First() == "mytrailer");
    }

    [Fact]
    public async Task HttpContext_WritesResponse()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/httpcontext") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("some content", content);
    }

    [Fact(Skip = "WIP")]
    public async Task TestPost()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://127.0.0.1:{_port}/post") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact, Content = JsonContent.Create(new WeatherForecast(new DateOnly(2026, 03, 30), 22, "sunny")) };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }
}

