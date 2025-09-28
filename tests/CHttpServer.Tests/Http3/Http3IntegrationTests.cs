using System.Net;

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

    [Fact(Skip = "Work in Progress")]
    public async Task Get_NoContent()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{_port}/nocontent") { Version = HttpVersion.Version30, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);        
    }
}

