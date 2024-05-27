using System.Text;
using System.Threading;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Http;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;
using CHttp.Performance;
using CHttp.Writers;
using System.IO.Pipelines;
using System.Net.Http.Headers;

namespace CHttpExecutor;

internal class ExecutionContext
{
    public IFileSystem FileSystem { get; } = new MemoryFileSystem();

    public ICookieContainer CookieContainer { get; } = new MemoryCookieContainer();

    public Dictionary<string, string> VariableValues { get; } = new();

    public FrozenExecutionStep? CurrentStep { get; set; }

    public VariablePostProcessingWriterStrategy Writer { get; set; }
}

public class Executor(ExecutionPlan plan)
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
            var uri = new Uri(VariablePreprocessor.Substitute(step.Uri, _ctx.VariableValues));
            List<KeyValueDescriptor> headers = [];
            foreach (var header in step.Headers)
                headers.Add(new(header.GetKey(), VariablePreprocessor.Substitute(header.GetValue(), _ctx.VariableValues)));
            var timeout = ProcessVariable(step.Timeout, _ctx, nameof(step.Timeout));
            var enableRedirects = ProcessVariable(step.EnableRedirects, _ctx, nameof(step.EnableRedirects));
            var enableCertificateValidation = !ProcessVariable(step.NoCertificateValidation, _ctx, nameof(step.NoCertificateValidation));
            var httpBehavior = new HttpBehavior(timeout, false, string.Empty, new SocketBehavior(enableRedirects, enableCertificateValidation, UseKerberosAuth: false, 1));
            var requestDetails = new HttpRequestDetails(new HttpMethod(step.Method), uri, step.Version, headers);
            HttpContent? body = step.Body.Count > 0 ? new StringLinesContent(step.Body.Select(x => VariablePreprocessor.Substitute(x, _ctx.VariableValues)).ToArray()) : null;
            if (!step.IsPerformanceRequest)
            {
                _ctx.Writer = new(!string.IsNullOrWhiteSpace(step.Name));
                await SendRequestAsync(httpBehavior, requestDetails, body, _ctx);

                // TODO: Variable postprocessing

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
        else
            if (T.TryParse(VariablePreprocessor.Substitute(value.VariableValue, ctx.VariableValues).AsSpan(), null, out T? result))
            return result;
        throw new ArgumentException($"Invalid value set for {name}");
    }

    private static async Task SendRequestAsync(HttpBehavior httpBehavior, HttpRequestDetails requestDetails, HttpContent? body, ExecutionContext ctx)
    {
        if (body is not null)
            requestDetails = requestDetails with { Content = body };
        var writer = ctx.Writer;
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

internal class VariablePostProcessingWriterStrategy : IWriter
{
    private bool _enabled;
    private Stream? _buffer;

    public VariablePostProcessingWriterStrategy(bool enabled)
    {
        _enabled = enabled;
        if (_enabled)
            _buffer = new MemoryStream();
        else
            _buffer = Stream.Null;
        Buffer = PipeWriter.Create(_buffer);
    }

    public PipeWriter Buffer { get; }

    public HttpResponseHeaders? Headers { get; private set; }

    public HttpContentHeaders? ContentHeaders { get; private set; }

    public HttpResponseHeaders? Trailers { get; private set; }

    public bool IsDisposed() => !_enabled;

    public async Task CompleteAsync(CancellationToken token)
    {
        await Buffer.FlushAsync();
        _enabled = false;
    }

    public ValueTask DisposeAsync()
    {
        _enabled = true;
        return ValueTask.CompletedTask;
    }

    public Task WriteSummaryAsync(HttpResponseHeaders? trailers, Summary summary)
    {
        if (!_enabled)
            return Task.CompletedTask;
        Trailers = trailers;
        return Task.CompletedTask;
    }

    public Task InitializeResponseAsync(HttpResponseInitials initials)
    {
        if (!_enabled)
            return Task.CompletedTask;
        Headers = initials.Headers;
        ContentHeaders = initials.ContentHeaders;
        return Task.CompletedTask;
    }
}
