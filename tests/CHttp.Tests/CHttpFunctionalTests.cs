using System.CommandLine;
using CHttp.Writers;
using Microsoft.AspNetCore.Http;

namespace CHttp.Tests;

public class CHttpFunctionalTests
{
    [Fact]
    public async Task NormalWriter_TestVanilaHttp3Request()
    {
        using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync("test"), Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3);
        await host.StartAsync();
        var console = new TestConsole();
        var writer = new NormalConsoleWriter(new BufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer).InvokeAsync("--method GET --no-certificate-validation --uri https://localhost:5011");

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal($"Status: OK{Environment.NewLine}Date:Server: Kestrel{Environment.NewLine}test", console.Text);
    }

    [Fact]
    public async Task QuietWriter_TestVanilaHttp3Request()
    {
        using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync("test"), Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3);
        await host.StartAsync();
        var console = new TestConsole();
        var writer = new QuietConsoleWriter(new BufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer).InvokeAsync("--method GET --no-certificate-validation --uri https://localhost:5011");

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("100%       4 B", console.Text);
    }
}
