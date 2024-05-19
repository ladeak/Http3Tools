using System.Net;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Http;
using CHttp.Writers;

namespace CHttpExecutor;

public class Executor(ExecutionPlan plan)
{
    private static MemoryFileSystem _fileSystem = new MemoryFileSystem();
    private static MemoryCookieContainer CookieContainer = new MemoryCookieContainer();

    public async Task ExecuteAsync()
    {
        foreach (var step in plan.Steps.Where(x => x != FrozenExecutionStep.Default))
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
        var client = new HttpMessageSender(writer, CookieContainer, new SingleSocketsHandlerProvider(), httpBehavior);
        await client.SendRequestAsync(requestDetails);
        await writer.CompleteAsync(CancellationToken.None);
        await CookieContainer.SaveAsync();
    }
}