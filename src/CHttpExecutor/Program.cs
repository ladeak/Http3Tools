using System.Net;
using System.Text;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Http;
using CHttp.Writers;

var input = args[0];

var fileSytem = new FileSystem();
if (!fileSytem.Exists(input))
{
    Console.WriteLine($"{input} file does not exist");
    return;
}

var fileStream = fileSytem.Open(input, FileMode.Open, FileAccess.Read);
var reader = new InputReader(new ExecutionPlanBuilder());
var plan = await reader.ReadStreamAsync(fileStream);
var executor = new Executor(plan);
await executor.ExecuteAsync();

public class InputReader(IExecutionPlanBuilder builder)
{
    public async Task<ExecutionPlan> ReadStreamAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line = await reader.ReadLineAsync();
        while (line != null)
        {
            ProcessLine(line);
            line = await reader.ReadLineAsync();
        }
        return builder.Build();
    }

    private void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (line.StartsWith("###"))
        {
            builder.AddStep();
        }

        if (line.StartsWith("GET "))
        {
            var endIndex = line.LastIndexOf(' ');
            builder.AddUri(line[4..(endIndex - 1)]);
        }
    }
}

public interface IExecutionPlanBuilder
{
    ExecutionPlan Build();

    void AddStep();

    void AddUri(string uri);
}

public class ExecutionPlanBuilder : IExecutionPlanBuilder
{
    private List<ExecutionStep> _steps = new();

    private ExecutionStep _currentStep = new();

    public void AddStep()
    {
        _steps.Add(_currentStep);
        _currentStep = new();
    }

    public void AddUri(string uri)
    {
        _currentStep.Uri = uri;
    }

    public ExecutionPlan Build()
    {
        _steps.Add(_currentStep);
        return new ExecutionPlan(_steps);
    }
}

public record ExecutionPlan(IEnumerable<ExecutionStep> Steps);

// TODO abstract?
public record ExecutionStep
{
    public string? Uri { get; set; }
}

public class Executor(ExecutionPlan plan)
{
    private static MemoryFileSystem _fileSystem = new MemoryFileSystem();

    public async Task ExecuteAsync()
    {
        foreach (var step in plan.Steps.Where(x => x != new ExecutionStep()))
        {
            await SendRequestImplAsync(false, false, false, 5, "GET", step.Uri,
                HttpVersion.Version20, [], null);
        }
    }

    private static async Task SendRequestImplAsync(
        bool enableRedirects,
        bool enableCertificateValidation,
        bool useKerberosAuth,
        double timeout,
        string method,
        string uri,
        Version version,
        IEnumerable<string> headers,
        string body
    )
    {
        var httpBehavior = new HttpBehavior(timeout, false, string.Empty, new SocketBehavior(enableRedirects, enableCertificateValidation, useKerberosAuth, 1));
        var parsedHeaders = new List<KeyValueDescriptor>();
        foreach (string header in headers ?? Enumerable.Empty<string>())
        {
            parsedHeaders.Add(new KeyValueDescriptor(header));
        }
        var requestDetails = new HttpRequestDetails(
            new HttpMethod(method),
            new Uri(uri, UriKind.Absolute),
            version,
            parsedHeaders);
        if (!string.IsNullOrWhiteSpace(body))
            requestDetails = requestDetails with { Content = new StringContent(body) };
        var outputBehavior = new OutputBehavior(LogLevel.Verbose, string.Empty);
        var console = new NoOpConsole();
        var cookieContainer = new MemoryCookieContainer();
        var writer = new WriterStrategy(outputBehavior, console: console);
        var client = new HttpMessageSender(writer, cookieContainer, new SingleSocketsHandlerProvider(), httpBehavior);
        await client.SendRequestAsync(requestDetails);
        await writer.CompleteAsync(CancellationToken.None);
        await cookieContainer.SaveAsync();
    }
}