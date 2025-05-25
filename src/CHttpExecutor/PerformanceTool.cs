using System.ComponentModel;
using System.Net;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Http;
using CHttp.Performance;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;
using CHttpExtension;
using ModelContextProtocol.Server;

namespace CHttpExecutor;

[McpServerToolType]
internal sealed class PerformanceTool()
{
    [McpServerTool, Description("Measures the performance of the given http endpoint.")]
    public static async Task<string> PerformanceMeasure(
        [Description("The URL of the endpoint")] string uri,
        [Description("The number of requests")] int requestCount = 100)
    {
        var console = new StringConsole();
        var httpBehavior = new HttpBehavior(10, false, string.Empty, new SocketBehavior(EnableRedirects: true, EnableCertificateValidation: false, UseKerberosAuth: false, MaxConnectionPerServer: 1));
        var parsedHeaders = new List<KeyValueDescriptor>();
        var requestDetails = new HttpRequestDetails(
            new HttpMethod("GET"),
            new Uri(uri, UriKind.Absolute),
            HttpVersion.Version20,
            parsedHeaders);

        var performanceBehavior = new PerformanceBehavior(requestCount, 10, false);
        var cookieContainer = new MemoryCookieContainer();
        ISummaryPrinter printer;
        printer = new StatisticsPrinter(console);
        var orchestrator = new PerformanceMeasureOrchestrator(printer, new NoOpConsole(), new Awaiter(), cookieContainer, new SingleSocketsHandlerProvider(), performanceBehavior);
        await orchestrator.RunAsync(requestDetails, httpBehavior, CancellationToken.None);
        return console.ToString() ?? string.Empty;
    }

    [McpServerTool, Description("Given a chttp file, it executes the steps defined in the http file.")]
    public async Task<string> ExecuteCHttp(
        [Description("File path for the CHttp file to execute.")] string filePath)
    {
        var fileSytem = new FileSystem();
        var console = new StringConsole();
        var fileStream = fileSytem.Open(filePath, FileMode.Open, FileAccess.Read);
        var reader = new InputReader(new ExecutionPlanBuilder());
        var plan = await reader.ReadStreamAsync(fileStream);
        var executor = new Executor(plan, console);
        await executor.ExecuteAsync();
        return console.ToString() ?? string.Empty;
    }
}