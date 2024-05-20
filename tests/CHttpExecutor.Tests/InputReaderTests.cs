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
GET https://localhost:5020/ HTTP/2"u8.ToArray();
    var stream = new MemoryStream(request);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        var step = plan.Steps.Single();
        Assert.Equal(10, step.ClientsCount);
        Assert.Equal(100, step.RequestsCount);
        Assert.Equal("mytest", step.Name);
        Assert.True(step.IsPerformanceRequest);
        Assert.True(step.SharedSocket);
        Assert.Equal(TimeSpan.FromSeconds(5), step.Timeout);
    }

    [Fact]
    public async Task CommentedVariables()
    {
        byte[] request = @"###
## @clientscount 10
# @sharedsocket true
GET https://localhost:5020/"u8.ToArray();
        var stream = new MemoryStream(request);

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        var step = plan.Steps.Single();
        Assert.False(step.ClientsCount.HasValue);
        Assert.True(step.SharedSocket);
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
        Assert.Equal("my", variables.First().Name);
        Assert.Equal("some", variables.First().Value);
        Assert.Equal("my2", variables.Last().Name);
        Assert.Equal("variable", variables.Last().Value);
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
        var stepLast = plan.Steps.Last();

        Assert.Equal("https://localhost:5021/", stepFirst.Uri.ToString());
        Assert.Equal(HttpMethod.Get.ToString(), stepFirst.Method);
        Assert.Equal(HttpVersion.Version20, stepFirst.Version);
        Assert.Null(stepFirst.Name);

        Assert.Equal("https://localhost:5020/", stepLast.Uri.ToString());
        Assert.Equal(HttpMethod.Post.ToString(), stepLast.Method);
        Assert.Equal(HttpVersion.Version11, stepLast.Version);
        Assert.Equal("test", stepLast.Name);

        Assert.Equal(stepFirst.Body , stepLast.Body);
        var variables = plan.Variables;
        Assert.Equal("my", variables.First().Name);
        Assert.Equal("some", variables.First().Value);
        Assert.Equal("my2", variables.Last().Name);
        Assert.Equal("variable", variables.Last().Value);
    }

    [Fact]
    public async Task SampleMultiStep()
    {
        var stream = File.OpenRead("test.chttp");

        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(stream);

        Assert.Equal(6, plan.Steps.Count());
        Assert.Equal(4, plan.Variables.Count());
    }

    // todo multistep, errors
}