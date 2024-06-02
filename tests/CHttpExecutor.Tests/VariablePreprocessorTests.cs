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
        var variables = new Dictionary<string, string>
        {
            { "host", "localhost" }
        };
        var result = VariablePreprocessor.Evaluate(input, variables, new Dictionary<string, VariablePostProcessingWriterStrategy>());
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
        var variables = new Dictionary<string, string>
        {
            { "host", "localhost" }
        };
        var result = VariablePreprocessor.Evaluate(input, variables, new Dictionary<string, VariablePostProcessingWriterStrategy>());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://{{host}}:{{port}}/", "https://localhost:5000/")]
    [InlineData("https://{{host }}:{{port }}/", "https://localhost:5000/")]
    [InlineData("https://{{host  }}:{{ port}}/", "https://localhost:5000/")]
    [InlineData("https://{{ host }}{{ port  }}/", "https://localhost5000/")]
    public void MultiSubstitution(string input, string expected)
    {
        var variables = new Dictionary<string, string>
        {
            { "host", "localhost" },
            { "port", "5000" }
        };
        var result = VariablePreprocessor.Evaluate(input, variables, new Dictionary<string, VariablePostProcessingWriterStrategy>());
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("envVarHost", "localhost", EnvironmentVariableTarget.Process);
        var variables = new Dictionary<string, string>
        {
            { "host", "{{$envVarHost}}" }
        };
        var result = VariablePreprocessor.Evaluate("https://{{host}}/", variables, new Dictionary<string, VariablePostProcessingWriterStrategy>());
        Assert.Equal("https://localhost/", result);
    }

    [Fact]
    public void InlineReplacementFromValues()
    {
        var variables = new Dictionary<string, string>
        {
            { "something", "localhost" },
            { "host", "{{ something }}" }
        };
        var result = VariablePreprocessor.Evaluate("https://{{host}}/", variables, new Dictionary<string, VariablePostProcessingWriterStrategy>());
        Assert.Equal("https://localhost/", result);
    }

    [Fact]
    public void InlinedReplacementFromValues()
    {
        Environment.SetEnvironmentVariable("envVarHost", "{{someOtherVariable}}", EnvironmentVariableTarget.Process);
        var variables = new Dictionary<string, string>
        {
            { "someOtherVariable", "localhost" },
            { "host", "{{$ envVarHost  }}" }
        };
        var result = VariablePreprocessor.Evaluate("https://{{host}}/", variables, new Dictionary<string, VariablePostProcessingWriterStrategy>());
        Assert.Equal("https://localhost/", result);
    }
}
