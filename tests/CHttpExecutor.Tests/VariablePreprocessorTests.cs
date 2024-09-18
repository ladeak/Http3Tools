using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CHttpExecutor.Tests;

public class VariablePreprocessorTests
{
    [Theory]
    [InlineData("https://{{host}}", "https://localhost")]
    [InlineData("https://{{host}}/", "https://localhost/")]
    [InlineData("https://{{host }}/", "https://localhost/")]
    [InlineData("https://{{host  }}/", "https://localhost/")]
    [InlineData("https://{{ host }}/", "https://localhost/")]
    public void SingleSubstitution(string input, string expected)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "host", "localhost" }
        };
        var result = VariablePreprocessor.Evaluate(input, variables.GetAlternateLookup<ReadOnlySpan<char>>(),
            new Dictionary<string, VariablePostProcessingWriterStrategy>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://{{hos", "https://{{hos")]
    [InlineData("https://{{hos{{", "https://{{hos{{")]
    [InlineData("https://{{hos}}", "https://{{hos}}")]
    [InlineData("https://{{hos{{}}", "https://{{hos{{}}")]
    [InlineData("https://{{host}/", "https://{{host}/")]
    [InlineData("https://{host}/", "https://{host}/")]
    [InlineData("https://host}}/", "https://host}}/")]
    [InlineData("https://hos{t{}}/", "https://hos{t{}}/")]
    [InlineData("https://hos{{}t}/", "https://hos{{}t}/")]
    public void NoSubstitution(string input, string expected)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "host", "localhost" }
        };
        var result = VariablePreprocessor.Evaluate(input, variables.GetAlternateLookup<ReadOnlySpan<char>>(),
            new Dictionary<string, VariablePostProcessingWriterStrategy>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://{{host}}:{{port}}/", "https://localhost:5000/")]
    [InlineData("https://{{host }}:{{port }}/", "https://localhost:5000/")]
    [InlineData("https://{{host  }}:{{ port}}/", "https://localhost:5000/")]
    [InlineData("https://{{ host }}{{ port  }}/", "https://localhost5000/")]
    public void MultiSubstitution(string input, string expected)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "host", "localhost" },
            { "port", "5000" }
        };
        var result = VariablePreprocessor.Evaluate(input, variables.GetAlternateLookup<ReadOnlySpan<char>>(),
            new Dictionary<string, VariablePostProcessingWriterStrategy>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>());
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("envVarHost", "localhost", EnvironmentVariableTarget.Process);
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "host", "{{$envVarHost}}" }
        };
        var result = VariablePreprocessor.Evaluate("https://{{host}}/", variables.GetAlternateLookup<ReadOnlySpan<char>>(),
            new Dictionary<string, VariablePostProcessingWriterStrategy>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>());
        Assert.Equal("https://localhost/", result);
    }

    [Fact]
    public void InlineReplacementFromValues()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "something", "localhost" },
            { "host", "{{ something }}" }
        };
        var result = VariablePreprocessor.Evaluate("https://{{host}}/", variables.GetAlternateLookup<ReadOnlySpan<char>>(),
            new Dictionary<string, VariablePostProcessingWriterStrategy>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>());
        Assert.Equal("https://localhost/", result);
    }

    [Fact]
    public void InlinedReplacementFromValues()
    {
        Environment.SetEnvironmentVariable("envVarHost", "{{someOtherVariable}}", EnvironmentVariableTarget.Process);
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "someOtherVariable", "localhost" },
            { "host", "{{$ envVarHost  }}" }
        };
        var result = VariablePreprocessor.Evaluate("https://{{host}}/", variables.GetAlternateLookup<ReadOnlySpan<char>>(),
            new Dictionary<string, VariablePostProcessingWriterStrategy>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>());
        Assert.Equal("https://localhost/", result);
    }

    [Fact]
    public async Task BodyParse()
    {
        var responseWriter = new VariablePostProcessingWriterStrategy(true);
        responseWriter.Buffer.Write("""{"Data":"hello"}"""u8);
        await responseWriter.Buffer.CompleteAsync();
        await responseWriter.CompleteAsync(CancellationToken.None);
        var responses = new Dictionary<string, VariablePostProcessingWriterStrategy>(StringComparer.Ordinal)
        {
            { "first",  responseWriter }
        };
        var result = VariablePreprocessor.Evaluate("https://{{first.response.body.$.Data}}/", new Dictionary<string, string>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>(),
            responses.GetAlternateLookup<ReadOnlySpan<char>>());
        Assert.Equal("https://hello/", result);
    }

    [Fact]
    public async Task BodyFilterParse()
    {
        var responseWriter = new VariablePostProcessingWriterStrategy(true);
        responseWriter.Buffer.Write("""{"Data":[{"Name":"test0","Val":0},{"Name":"test1","Val":1}]}"""u8);
        await responseWriter.Buffer.CompleteAsync();
        await responseWriter.CompleteAsync(CancellationToken.None);
        var responses = new Dictionary<string, VariablePostProcessingWriterStrategy>(StringComparer.Ordinal)
        {
            { "first",  responseWriter }
        };
        var result = VariablePreprocessor.Evaluate("https://{{first.response.body.$.Data[?@.Val<1].Name}}/",
            new Dictionary<string, string>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>(),
            responses.GetAlternateLookup<ReadOnlySpan<char>>());
        Assert.Equal("https://test0/", result);
    }

    [Fact]
    public async Task Header()
    {
        var responseWriter = new VariablePostProcessingWriterStrategy(true);
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.Add("my", "value-");
        response.Content = new StringContent("test", MediaTypeHeaderValue.Parse("application/json"));
        var initDetails = new CHttp.Writers.HttpResponseInitials(HttpStatusCode.OK, response.Headers, response.Content.Headers, HttpVersion.Version20, Encoding.UTF8);
        await responseWriter.InitializeResponseAsync(initDetails);
        await responseWriter.Buffer.CompleteAsync();
        await responseWriter.CompleteAsync(CancellationToken.None);
        var responses = new Dictionary<string, VariablePostProcessingWriterStrategy>(StringComparer.Ordinal)
        {
            { "first",  responseWriter }
        };
        var result = VariablePreprocessor.Evaluate("{{first.response.Headers.my}}{{first.response.headers.content-type}}",
            new Dictionary<string, string>(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>(),
            responses.GetAlternateLookup<ReadOnlySpan<char>>());
        Assert.Equal("value-application/json", result);
    }
}
