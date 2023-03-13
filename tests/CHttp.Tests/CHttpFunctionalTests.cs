using System.CommandLine;
using System.Text;
using CHttp.Writers;
using Microsoft.AspNetCore.Http;

namespace CHttp.Tests;

public class CHttpFunctionalTests
{
    [Fact]
    public async Task VerboseWriter_TestVanilaHttp3Request()
    {
        using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync("test"), Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3);
        await host.StartAsync();
        var console = new TestConsole();
        var writer = new VerboseConsoleWriter(new TextBufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer).InvokeAsync("--method GET --no-certificate-validation --uri https://localhost:5011");

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains($"Status: OK Version: 3.0 Encoding: utf-8{Environment.NewLine}Date:Server: Kestrel{Environment.NewLine}test{Environment.NewLine}https://localhost:5011/ 4 B 00:00:00.", console.Text);
    }

    [Fact]
    public async Task ProgressingWriter_TestVanilaHttp3Request()
    {
        using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync("test"), Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3);
        await host.StartAsync();
        var console = new TestConsole();
        var writer = new ProgressingConsoleWriter(new TextBufferedProcessor(), console);

        var client = await CommandFactory.CreateRootCommand(writer).InvokeAsync("--method GET --no-certificate-validation --uri https://localhost:5011");

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("100%       4 B", console.Text);
        Assert.Contains($"https://localhost:5011/ 4 B 00:00:00.", console.Text);
    }

    [Theory]
    [InlineData("test message")]
    public async Task StreamWriter_TestVanilaHttp3Request(string response)
    {
        using var output = new MemoryStream();
        using var host = HttpServer.CreateHostBuilder(context => context.Response.WriteAsync(response), Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http3);
        await host.StartAsync();
        var console = new TestConsole();
        var writer = new ProgressingConsoleWriter(new StreamBufferedProcessor(output), console);

        var client = await CommandFactory.CreateRootCommand(writer).InvokeAsync("--method GET --no-certificate-validation --uri https://localhost:5011");

        await writer.CompleteAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(response, Encoding.UTF8.GetString(output.ToArray()));
    }
}
