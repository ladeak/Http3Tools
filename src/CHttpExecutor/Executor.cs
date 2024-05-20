using System.Net;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Http;
using CHttp.Writers;

namespace CHttpExecutor;

internal class ExecutionContext
{
    public IFileSystem FileSystem { get; } = new MemoryFileSystem();
    
    public ICookieContainer CookieContainer { get; } = new MemoryCookieContainer();
    
    public Dictionary<string, string> VariableValues { get; } = new();
}

public class Executor(ExecutionPlan plan)
{
    private ExecutionContext _ctx = new ExecutionContext();

    public async Task ExecuteAsync()
    {
        foreach (var step in plan.Steps)
        {
            // TODO: Variable preprocessing
            await SendRequestImplAsync(false, false, false, 5, "GET", new Uri(step.Uri),
                HttpVersion.Version20, [], null);
            // TODO: Variable postprocessing

            // TODO: assertion
        }
    }

    private async Task SendRequestImplAsync(
        bool enableRedirects,
        bool enableCertificateValidation,
        bool useKerberosAuth,
        double timeout,
        string method,
        Uri uri,
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
            uri,
            version,
            parsedHeaders);
        if (!string.IsNullOrWhiteSpace(body))
            requestDetails = requestDetails with { Content = new StringContent(body) };
        var outputBehavior = new OutputBehavior(LogLevel.Verbose, string.Empty);
        var console = new NoOpConsole();
        var writer = new WriterStrategy(outputBehavior, console: console);
        var client = new HttpMessageSender(writer, _ctx.CookieContainer, new SingleSocketsHandlerProvider(), httpBehavior);
        await client.SendRequestAsync(requestDetails);
        await writer.CompleteAsync(CancellationToken.None);
        await _ctx.CookieContainer.SaveAsync();
    }
}