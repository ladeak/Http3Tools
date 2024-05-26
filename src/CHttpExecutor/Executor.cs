using System.Text;
using System.Threading;
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
                await SendRequestImplAsync(httpBehavior, requestDetails, body);
            else
            {
                // Variable preprocessing
                var clientsCount = ProcessVariable(step.ClientsCount, _ctx, nameof(step.ClientsCount));
                var requestCount = ProcessVariable(step.RequestsCount, _ctx, nameof(step.RequestsCount));
                var sharedsocket = ProcessVariable(step.SharedSocket, _ctx, nameof(step.SharedSocket));
            }


            // TODO: Variable postprocessing

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

    private async Task SendRequestImplAsync(HttpBehavior httpBehavior, HttpRequestDetails requestDetails, HttpContent? body)
    {
        if (body is not null)
            requestDetails = requestDetails with { Content = body };
        var outputBehavior = new OutputBehavior(LogLevel.Verbose, string.Empty);
        var console = new NoOpConsole();
        var writer = new WriterStrategy(outputBehavior, console: console);
        var client = new HttpMessageSender(writer, _ctx.CookieContainer, new SingleSocketsHandlerProvider(), httpBehavior);
        await client.SendRequestAsync(requestDetails);
        await writer.CompleteAsync(CancellationToken.None);
        await _ctx.CookieContainer.SaveAsync();
    }
}
