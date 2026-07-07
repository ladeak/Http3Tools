using System.Net;
using System.Net.Http.Json;
using System.Text;

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
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (message, certificate, chain, sslPolicyErrors) => certificate?.Issuer == "CN=localhost";
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

    [Fact]
    public async Task TestPost()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://127.0.0.1:{_port}/post") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact, Content = JsonContent.Create(new WeatherForecast(new DateOnly(2026, 03, 30), 22, "sunny")) };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task HttpContext_StreamsResponse()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/stream") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
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
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/stream") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("some contentsome content2", content);
    }

    [Fact]
    public async Task IAsyncEnumerable()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/iasyncenumerable") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
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
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/content") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("\"some content\"", content);

        request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/content") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("\"some content\"", content);
    }

    [Fact]
    public async Task Get_Content_TwoParallelRequests_SameConnection()
    {
        var client = CreateClient();
        var request0 = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/content") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var request1 = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/content") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };

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
        for (int i = 0; i < 100; i++)
        {
            var client = CreateClient();
            var request0 = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/content") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            var request1 = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/content") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };

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

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(11)]
    [InlineData(15)]
    [InlineData(24)]
    [InlineData(30)]
    public async Task LargeInput(int multiplier)
    {
        int requestLength = 32768 * multiplier;
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://127.0.0.1:{_port}/readallrequest") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Content = new ByteArrayContent(new byte[requestLength]);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(requestLength, int.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task LargeOutput()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/getlargeresponse") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10_000_000, content.Length);
    }

    [Fact]
    public async Task LargerStreamedOutput()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/getlargestreamresponse") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10_000_000, content.Length);
    }

    [Fact]
    public async Task TestPut()
    {
        var client = CreateClient();
        var input = new WeatherForecast(new DateOnly(2026, 03, 30), 22, "sunny");
        var request = new HttpRequestMessage(HttpMethod.Put, $"https://127.0.0.1:{_port}/put") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact, Content = JsonContent.Create(input) };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadFromJsonAsync<WeatherForecast>(TestContext.Current.CancellationToken);
        Assert.Equal(input, content);
    }

    [Fact]
    public async Task TestDelete()
    {
        var client = CreateClient();
        var input = 305;
        var request = new HttpRequestMessage(HttpMethod.Delete, $"https://127.0.0.1:{_port}/delete?i={input}") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(input.ToString(), content);
    }

    [Fact]
    public async Task TestOptions()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, $"https://127.0.0.1:{_port}/cors") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.Add("access-control-request-method", "get");
        request.Headers.Add("origin", "https://localhost");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);

        request = new HttpRequestMessage(HttpMethod.Options, $"https://127.0.0.1:{_port}/cors") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.Add("access-control-request-method", "get");
        request.Headers.Add("origin", "https://localhost");
        response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task TestTimeout()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/timeout") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var completed = await Task.WhenAny(response, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(response, completed);
        Assert.True(response.IsCanceled);
    }

    [Fact]
    public async Task TestServerSideEvents()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/sse") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("data: some content\n\ndata: some content\n\n", content);
    }
}

