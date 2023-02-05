using System.CommandLine;
using Microsoft.AspNetCore.Http;

namespace CHttp.Tests;

public class CHttpFunctionalTests
{
    [Fact]
    public async Task TestVanilaHttp3Request()
    {
        using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync("test"), Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3);
        await host.StartAsync();
        var writer = new TestContentResponseWriter(new BufferedProcessor());
        
        var client = await CommandFactory.CreateRootCommand(writer).InvokeAsync("--method GET --no-certificate-validation --uri https://localhost:5001");

        await writer.ReadCompleted;
        var result = writer.ToString();
        Assert.Equal("test", result);
    }
}
