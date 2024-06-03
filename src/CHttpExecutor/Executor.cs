using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Http;
using CHttp.Performance;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;

namespace CHttpExecutor;

internal class ExecutionContext
{
    public IFileSystem FileSystem { get; } = new MemoryFileSystem();

    public ICookieContainer CookieContainer { get; } = new MemoryCookieContainer();

    public Dictionary<string, string> VariableValues { get; } = new();

    public FrozenExecutionStep? CurrentStep { get; set; }

    public Dictionary<string, VariablePostProcessingWriterStrategy> ExecutionResults { get; set; } = new();
}

internal class Executor(ExecutionPlan plan)
{
    private ExecutionContext _ctx = new ExecutionContext();

    public async Task ExecuteAsync()
    {
        foreach (var step in plan.Steps)
        {
            _ctx.CurrentStep = step;
            // Add or update variable state in the context.
            foreach (var newVar in step.Variables)
                _ctx.VariableValues[newVar.Name] = newVar.Value;

            // Variable preprocessing
            var uri = new Uri(VariablePreprocessor.Evaluate(step.Uri, _ctx.VariableValues, _ctx.ExecutionResults));
            List<KeyValueDescriptor> headers = [];
            foreach (var header in step.Headers)
                headers.Add(new(header.GetKey(), VariablePreprocessor.Evaluate(header.GetValue(), _ctx.VariableValues, _ctx.ExecutionResults)));
            var timeout = ProcessVariable(step.Timeout, _ctx, nameof(step.Timeout));
            var enableRedirects = ProcessVariable(step.EnableRedirects, _ctx, nameof(step.EnableRedirects));
            var enableCertificateValidation = !ProcessVariable(step.NoCertificateValidation, _ctx, nameof(step.NoCertificateValidation));
            var httpBehavior = new HttpBehavior(timeout, false, string.Empty, new SocketBehavior(enableRedirects, enableCertificateValidation, UseKerberosAuth: false, 1));
            var requestDetails = new HttpRequestDetails(new HttpMethod(step.Method), uri, step.Version, headers);
            HttpContent? body = step.Body.Count > 0 ? new StringLinesContent(step.Body.Select(x => VariablePreprocessor.Evaluate(x, _ctx.VariableValues, _ctx.ExecutionResults)).ToArray()) : null;
            if (!step.IsPerformanceRequest)
            {
                _ctx.ExecutionResults.TryAdd(step.Name ?? string.Empty, new VariablePostProcessingWriterStrategy(!string.IsNullOrWhiteSpace(step.Name)));
                await SendRequestAsync(httpBehavior, requestDetails, body, _ctx);
            }
            else
            {
                // Variable preprocessing
                var clientsCount = ProcessVariable(step.ClientsCount, _ctx, nameof(step.ClientsCount));
                var requestCount = ProcessVariable(step.RequestsCount, _ctx, nameof(step.RequestsCount));
                var sharedsocket = ProcessVariable(step.SharedSocket, _ctx, nameof(step.SharedSocket));
                var perfBehavior = new PerformanceBehavior(requestCount, clientsCount, sharedsocket);
                await PerfMeasureAsync(httpBehavior, requestDetails, perfBehavior, body, _ctx);
            }

            // TODO: assertion
        }
    }

    private static T ProcessVariable<T>(VarValue<T> value, ExecutionContext ctx, string name)
        where T : ISpanParsable<T>
    {
        if (value.HasValue)
            return value.Value;
        else if (T.TryParse(VariablePreprocessor.Evaluate(value.VariableValue, ctx.VariableValues, ctx.ExecutionResults).AsSpan(), null, out T? result))
            return result;
        throw new ArgumentException($"Invalid value set for {name}");
    }

    private static async Task SendRequestAsync(HttpBehavior httpBehavior, HttpRequestDetails requestDetails, HttpContent? body, ExecutionContext ctx)
    {
        if (body is not null)
            requestDetails = requestDetails with { Content = body };
        var writer = ctx.ExecutionResults[ctx.CurrentStep!.Name ?? string.Empty];
        var client = new HttpMessageSender(writer, ctx.CookieContainer, new SingleSocketsHandlerProvider(), httpBehavior);
        await client.SendRequestAsync(requestDetails);
        await writer.CompleteAsync(CancellationToken.None);
        await ctx.CookieContainer.SaveAsync();
    }

    private static async Task PerfMeasureAsync(HttpBehavior httpBehavior, HttpRequestDetails requestDetails, PerformanceBehavior performanceBehavior, HttpContent? body, ExecutionContext ctx)
    {
        var step = ctx.CurrentStep!;
        if (body is not null)
            requestDetails = requestDetails with { Content = body };
        var console = new NoOpConsole();
        var cookieContainer = new MemoryCookieContainer();
        ISummaryPrinter printer;
        if (string.IsNullOrEmpty(step.Name))
            printer = new StatisticsPrinter(console);
        else
            printer = new CompositePrinter(new StatisticsPrinter(console), new FilePrinter(step.Name, ctx.FileSystem));
        var orchestrator = new PerformanceMeasureOrchestrator(printer, new NoOpConsole(), new Awaiter(), cookieContainer, new SingleSocketsHandlerProvider(), performanceBehavior);
        await orchestrator.RunAsync(requestDetails, httpBehavior, CancellationToken.None);
    }
}
