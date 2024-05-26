using System.Net;

namespace CHttpExecutor.Tests;

public class InputReaderTests
{
    private const int Port = 5020;

    private byte[] _singleRequest = @"###
GET https://localhost:5020/ HTTP/2"u8.ToArray();

    [Fact]
    public async Task UrlParsed_MethodParsed_VersionParsed()
    {
        var stream = new MemoryStream(_singleRequest);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        var step = plan.Steps.Single();
        Assert.Equal("https://localhost:5020/", step.Uri.ToString());
        Assert.Equal(HttpMethod.Get.ToString(), step.Method);
        Assert.Equal(HttpVersion.Version20, step.Version);
        Assert.False(step.IsPerformanceRequest);
    }

    [Fact]
    public async Task ExecutionInstructionsParsed()
    {
        byte[] request = @"###
# @name mytest
# @clientscount 10
# @requestcount 100
# @sharedsocket
# @timeout 5
# @no-cert-validation true
# @enableRedirect false
GET https://localhost:5020/ HTTP/2"u8.ToArray();
        var stream = new MemoryStream(request);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        var step = plan.Steps.Single();
        Assert.Equal(10, step.ClientsCount!.Value);
        Assert.Equal(100, step.RequestsCount!.Value);
        Assert.Equal("mytest", step.Name);
        Assert.True(step.IsPerformanceRequest);
        Assert.True(step.SharedSocket!.Value);
        Assert.True(step.NoCertificateValidation!.Value);
        Assert.True(step.EnableRedirects!.Value);
        Assert.Equal(5, step.Timeout!.Value);
    }

    [Fact]
    public async Task CommentedVariables()
    {
        byte[] request = @"###
@var = a
## @clientscount 10
# @sharedsocket true
# @requestCount {{ var}}
GET https://localhost:5020/"u8.ToArray();
        var stream = new MemoryStream(request);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        var step = plan.Steps.Single();
        Assert.Null(step.ClientsCount);
        Assert.Equal("{{ var}}", step.RequestsCount!.VariableValue);
    }

    [Fact]
    public async Task HeadersParsed()
    {
        byte[] request = @"###
GET https://localhost:5020/ HTTP/3
Authorization: Bearer mytoken"u8.ToArray();
        var stream = new MemoryStream(request);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        var step = plan.Steps.Single();
        Assert.Equal(HttpVersion.Version30, step.Version);
        var header = step.Headers.Single();
        Assert.Equal("Authorization", header.GetKey());
        Assert.Equal("Bearer mytoken", header.GetValue());
    }

    [Fact]
    public async Task Body()
    {
        byte[] request = @"###
GET https://localhost:5020/ HTTP/1.1
Authorization: Bearer mytoken

{""something"": ""value""}"u8.ToArray();
        var stream = new MemoryStream(request);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        var step = plan.Steps.Single();
        Assert.Equal(HttpVersion.Version11, step.Version);
        Assert.Equal("{\"something\": \"value\"}", step.Body.Single());
        var header = step.Headers.Single();
        Assert.Equal("Authorization", header.GetKey());
        Assert.Equal("Bearer mytoken", header.GetValue());
    }

    [Fact]
    public async Task Variables()
    {
        byte[] request = @"
@my =  some
###
@my2 = variable 
POST https://localhost:5020/ HTTP/1.1

{""something"": ""value""}"u8.ToArray();
        var stream = new MemoryStream(request);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        var step = plan.Steps.Single();
        Assert.Equal("POST", step.Method);
        Assert.Equal("{\"something\": \"value\"}", step.Body.Single());
        var variables = plan.Variables;
        Assert.Contains("my", variables);
        Assert.Contains("my2", variables);

        Assert.Contains(new("my", "some"), step.Variables);
        Assert.Contains(new("my2", "variable"), step.Variables);
    }

    [Fact]
    public async Task MultiStep()
    {
        byte[] request = @"
@my =  some

###
@my2 = variable 
GET https://localhost:5021/ HTTP/2

{""something"": ""value""}
###
# @name test
POST https://localhost:5020/ HTTP/1.1

{""something"": ""value""}

###

"u8.ToArray();
        var stream = new MemoryStream(request);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        var stepFirst = plan.Steps.First();
        var stepSecond = plan.Steps.Last();

        Assert.Equal("https://localhost:5021/", stepFirst.Uri.ToString());
        Assert.Equal(HttpMethod.Get.ToString(), stepFirst.Method);
        Assert.Equal(HttpVersion.Version20, stepFirst.Version);
        Assert.Null(stepFirst.Name);

        Assert.Equal("https://localhost:5020/", stepSecond.Uri.ToString());
        Assert.Equal(HttpMethod.Post.ToString(), stepSecond.Method);
        Assert.Equal(HttpVersion.Version11, stepSecond.Version);
        Assert.Equal("test", stepSecond.Name);

        Assert.Equal(stepSecond.Body, stepSecond.Body);

        Assert.Equal(2, plan.Variables.Count);
        Assert.Equal(2, stepFirst.Variables.Count);
        Assert.Empty(stepSecond.Variables);
    }

    [Fact]
    public async Task SampleMultiStep_IgnoresDiffCommand()
    {
        var stream = File.OpenRead("test.chttp");

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        Assert.Equal(6, plan.Steps.Count());
        Assert.Equal(4, plan.Variables.Count());
    }

    [Fact]
    public async Task VariableNotDefinedThrows()
    {
        byte[] request = @"
# @requestCount {{var}}
GET https://localhost:5020/"u8.ToArray();
        var stream = new MemoryStream(request);

        var reader = new InputReader(new ExecutionPlanBuilder());
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadStreamAsync(stream));
    }
}