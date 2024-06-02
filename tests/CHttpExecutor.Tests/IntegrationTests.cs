using System.Net.Http.Json;
using CHttp.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    private byte[] _postProcessingRequest = @"###
# @no-cert-validation
# @name firstRequest
GET https://localhost:5020/ HTTP/2
###
@myheader = {{firstRequest.response.headers.my}}
###

# @no-cert-validation
GET https://localhost:5020/ HTTP/2
my: {{myheader}}"u8.ToArray();

    private byte[] _postProcessingContentHeaderRequest = @"###
# @no-cert-validation
# @name firstRequest
GET https://localhost:5020/ HTTP/2
###
@myheader = {{firstRequest.response.headers.content-type}}
###

# @no-cert-validation
GET https://localhost:5020/ HTTP/2
my: {{myheader}}"u8.ToArray();

    private byte[] _postProcessingBodyRequest = @"###
# @no-cert-validation
# @name firstRequest
GET https://localhost:5020/ HTTP/2
###
@myvalue = {{firstRequest.response.body.data.1.stringValue}}
@mydate = {{firstRequest.response.body.data.1.dateValue}}
@mynumber = {{firstRequest.response.body.data.1.numberValue}}
###

# @no-cert-validation
GET https://localhost:5020/ HTTP/2
myvalue: {{myvalue}}
mydate: {{mydate}}
mynumber: {{mynumber}}"u8.ToArray();

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

    [Fact]
    public async Task PostProcessingHeadersVariables()
    {
        string testHeaderValue = "roundtripped header value";
        TaskCompletionSource requestReceived = new();
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            if (context.Request.Headers["my"] == testHeaderValue)
                requestReceived.TrySetResult();
            context.Response.Headers["my"] = testHeaderValue;
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_postProcessingRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var executor = new Executor(plan);
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PostProcessingTrailersVariables()
    {
        string testHeaderValue = "roundtripped header value";
        TaskCompletionSource requestReceived = new();
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            if (context.Request.Headers["my"] == testHeaderValue)
                requestReceived.TrySetResult();
            await context.Response.WriteAsync("test");
            context.Response.AppendTrailer("my", testHeaderValue);
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_postProcessingRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var executor = new Executor(plan);
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PostProcessingContentHeadersVariables()
    {
        string testHeaderValue = "roundtripped header value";
        TaskCompletionSource requestReceived = new();
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            if (context.Request.Headers["my"] == testHeaderValue)
                requestReceived.TrySetResult();
            context.Response.ContentType = testHeaderValue;
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_postProcessingContentHeaderRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var executor = new Executor(plan);
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PostProcessingBodyVariables()
    {
        string testValue = "roundtripped header value";
        var testDate = new DateTime(2024, 06, 02);
        TaskCompletionSource requestReceived = new();
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            if (context.Request.Headers["myvalue"] == testValue && context.Request.Headers["mydate"] == testDate.ToString("s") && context.Request.Headers["mynumber"] == "2")
                requestReceived.TrySetResult();
            await context.Response.WriteAsJsonAsync(new Root([new(testValue, testDate, 1), new(testValue, testDate, 2)]));
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_postProcessingBodyRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var executor = new Executor(plan);
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private record class Root(IEnumerable<Data> Data);

    private record class Data(string StringValue, DateTime DateValue, int NumberValue);
}