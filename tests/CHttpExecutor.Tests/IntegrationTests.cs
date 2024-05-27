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

    private byte[] _multiRequest = @"###
@host = https://localhost:5020/
@rcount = 5
###
# @no-cert-validation
# @name firstRequest
GET {{host}} HTTP/2

###
# @no-cert-validation
# @requestCount {{rcount}}
# @clientsCount 2
GET {{host}} HTTP/2

###
# @no-cert-validation
GET {{host}} HTTP/2
"u8.ToArray();

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

    [Fact]
    public async Task MultiRequestInvokesEndpoint()
    {
        int requestCount = 0;
        TaskCompletionSource requestReceived = new();
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            var decremented = Interlocked.Increment(ref requestCount);
            if (decremented == 9) // Note that perf runs an additional warmup query per client: 1 + (2 + 5) + 1
                requestReceived.TrySetResult();
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_multiRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var executor = new Executor(plan);
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}