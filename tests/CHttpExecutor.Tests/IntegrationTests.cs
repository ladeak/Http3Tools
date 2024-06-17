using System.CommandLine.IO;
using System.Text.Json;
using CHttp.Abstractions;
using CHttp.Tests;
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
        var executor = new Executor(plan, new NoOpConsole());
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
        var executor = new Executor(plan, new NoOpConsole());
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
        var executor = new Executor(plan, new NoOpConsole());
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
        var executor = new Executor(plan, new NoOpConsole());
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

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
        var executor = new Executor(plan, new NoOpConsole());
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private byte[] _postProcessingBodyRequest = @"###
# @no-cert-validation
# @name firstRequest
GET https://localhost:5020/ HTTP/2
###
@myvalue = {{firstRequest.response.body.data[1].stringValue}}
@mydate = {{firstRequest.response.body.$.data[1].dateValue}}
@mynumber = {{firstRequest.response.body.data[1].numberValue}}
###

# @no-cert-validation
GET https://localhost:5020/ HTTP/2
myvalue: {{myvalue}}
mydate: {{mydate}}
mynumber: {{mynumber}}"u8.ToArray();

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
        var executor = new Executor(plan, new NoOpConsole());
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private byte[] _postProcessingBodyArrayRequest = @"###
# @no-cert-validation
# @name firstRequest
GET https://localhost:5020/ HTTP/2
###
@myvalue = {{firstRequest.response.body.data[1]}}
###

# @no-cert-validation
GET https://localhost:5020/ HTTP/2
myvalue: {{myvalue}}"u8.ToArray();

    [Fact]
    public async Task PostProcessingBodyArrayVariables()
    {
        string testValue = "roundtripped header value";
        var testDate = new DateTime(2024, 06, 02);
        TaskCompletionSource requestReceived = new();
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            if (context.Request.Headers["myvalue"] == """{"stringValue":"roundtripped header value","dateValue":"2024-06-02T00:00:00","numberValue":2}""")
                requestReceived.TrySetResult();
            await context.Response.WriteAsJsonAsync(new Root([new(testValue, testDate, 1), new(testValue, testDate, 2)]), new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_postProcessingBodyArrayRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var executor = new Executor(plan, new NoOpConsole());
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private byte[] _postProcessingBodyArrayIntegersRequest = @"###
# @no-cert-validation
# @name firstRequest
GET https://localhost:5020/ HTTP/2
###
@myvalue = {{firstRequest.response.body.data}}
###

# @no-cert-validation
GET https://localhost:5020/ HTTP/2
myvalue: {{myvalue}}"u8.ToArray();

    [Fact]
    public async Task PostProcessingBodyArrayIntegersVariables()
    {
        TaskCompletionSource requestReceived = new();
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            if (context.Request.Headers["myvalue"] == """[1,2,3]""")
                requestReceived.TrySetResult();
            await context.Response.WriteAsJsonAsync(new RootInts([1, 2, 3]));
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_postProcessingBodyArrayIntegersRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var executor = new Executor(plan, new NoOpConsole());
        await executor.ExecuteAsync();

        await requestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private byte[] _assertionRequest = @"###
@host = https://localhost:5020/
@rcount = 5

###
# @no-cert-validation
# @requestCount 5
# @clientsCount 2
# @assert mean < 1
GET {{host}} HTTP/2
"u8.ToArray();

    [Fact]
    public async Task Assert_ReturnsFalse()
    {
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            await Task.Delay(2);
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_assertionRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var executor = new Executor(plan, new NoOpConsole());
        Assert.False(await executor.ExecuteAsync());
    }

    private byte[] _successfulAssertionRequest = @"###
@host = https://localhost:5020/
@rcount = 5

###
# @no-cert-validation
# @requestCount 5
# @clientsCount 2
# @assert mean >= 1.000 median > 0.653 min > -1 max < 1000000 throughput > 0 requestsec !=0 successstatus == 5 percentile95th <= 100000 stddev > 0 error >= 0
GET {{host}} HTTP/2
"u8.ToArray();

    [Fact]
    public async Task SuccessAssert_ReturnsTrue()
    {
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_successfulAssertionRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var executor = new Executor(plan, new NoOpConsole());
        Assert.True(await executor.ExecuteAsync());
    }

    private byte[] _failingAssertionRequest = @"###
@host = https://localhost:5020/
@rcount = 5

###
# @no-cert-validation
# @requestCount 5
# @clientsCount 2
# @assert mean <= 0s median < 0.001ms min <= 0.001 max < 0 throughput<=0.002 requestsec ==0 successstatus== 6 percentile95th < 0.001 stddev <= 0 error == 0
GET {{host}} HTTP/2
"u8.ToArray();

    [Fact]
    public async Task FailingAssert_ReturnsErrors()
    {
        using var host = HttpServer.CreateHostBuilder(async context =>
        {
            await Task.Delay(1);
            await context.Response.WriteAsync("test");
        }, HttpProtocols.Http2, port: Port);
        await host.StartAsync();

        var stream = new MemoryStream(_failingAssertionRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);
        var testConsole = new TestConsolePerWrite();
        var executor = new Executor(plan, testConsole);
        Assert.False(await executor.ExecuteAsync());
        var output = testConsole.Text;
        Assert.Contains("MeanAssertion", output);
        Assert.Contains("MedianAssertion", output);
        Assert.Contains("MinAssertion", output);
        Assert.Contains("MaxAssertion", output);
        Assert.Contains("ThroughputAssertion", output);
        Assert.Contains("StdDevAssertion", output);
        Assert.Contains("ErrorAssertion", output);
        Assert.Contains("Percentile95thAssertion", output);
        Assert.Contains("RequestSecAssertion", output);
        Assert.Contains("SuccessStatusCodesAssertion", output);

        Assert.Contains("<= 0.000ns", output);
        Assert.Contains("< 1.000us", output);
        Assert.Contains("<= 1.000ms", output);
        Assert.Contains("< 0.000ns", output);
        Assert.Contains("<= 0.002", output);
        Assert.Contains("== 0.000", output);
        Assert.Contains("== 6.000", output);
        Assert.Contains("< 1.000ms", output);
        Assert.Contains("<= 0.000ns", output);
        Assert.Contains("== 0.000ns", output);
    }

    private record class Root(IEnumerable<Data> Data);

    private record class Data(string StringValue, DateTime DateValue, int NumberValue);

    private record class RootInts(IEnumerable<int> Data);
}