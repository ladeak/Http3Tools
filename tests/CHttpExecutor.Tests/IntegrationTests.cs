using CHttp.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace CHttpExecutor.Tests;

public class IntegrationTests
{
    private const int Port = 5020;

    private byte[] _singleRequest = @"###
# @no-cert-validation
GET https://localhost:5020/ HTTP/2"u8.ToArray();

    [Fact]
    public async Task SingleRequestInvokesEndpoint()
    {
        TaskCompletionSource requestReceived = new();
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            requestReceived.TrySetResult();
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_singleRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var executor = new Executor(plan);
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}