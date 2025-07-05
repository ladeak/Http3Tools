using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace CHttpServer.Tests;


[Collection(nameof(VanilaCHttpServerIntegrationTests))]
[CollectionDefinition(DisableParallelization = true)]
public class VanilaCHttpServerIntegrationTests : CHttpServerIntegrationTests
{
    public VanilaCHttpServerIntegrationTests(TestServer testServer) : base(testServer)
    {
        _server.RunAsync(port: 7222, usePriority: false);
    }
}

[Collection(nameof(PriorityCHttpServerIntegrationTests))]
[CollectionDefinition(DisableParallelization = true)]
public class PriorityCHttpServerIntegrationTests : CHttpServerIntegrationTests
{
    public PriorityCHttpServerIntegrationTests(TestServer testServer) : base(testServer)
    {
        _server.RunAsync(port: 7223, usePriority: true);
    }
}

public abstract class CHttpServerIntegrationTests : IClassFixture<TestServer>
{
    protected readonly TestServer _server;

    public CHttpServerIntegrationTests(TestServer testServer)
    {
        _server = testServer;
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

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(11)]
    [InlineData(15)]
    [InlineData(24)]
    [InlineData(30)]
    public async Task LargerInput_Than_FlowControl(int multiplier)
    {
        int requestLength = 32768 * multiplier;
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost:7222/readallrequest") { Version = HttpVersion.Version20 };
        request.Content = new ByteArrayContent(new byte[requestLength]);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(requestLength, int.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task LargerOutput_Than_FlowControl()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/getlargeresponse") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        var content = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10_000_000, content.Length);
    }

    [Fact]
    public async Task LargerStreamedOutput()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/getlargestreamresponse") { Version = HttpVersion.Version20 };
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
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
        var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:7222/headersecho") { Version = HttpVersion.Version20 };
        request.Headers.Add(headerName, headerValue);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.TryGetValues(headerName, out var values) && values.Single() == headerValue);
    }
}
