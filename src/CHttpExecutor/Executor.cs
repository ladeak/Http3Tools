using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Http;
using CHttp.Performance;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;

namespace CHttpExecutor;

internal class ExecutionContext
{
    public ExecutionContext()
    {
        // TODO: Drop StringComparer.Ordinal .NET 9 https://github.com/dotnet/runtime/issues/27229
        VariableValues = new(StringComparer.Ordinal);
        VariableValuesLookup = VariableValues.GetAlternateLookup<ReadOnlySpan<char>>();
        ExecutionResults = new(StringComparer.Ordinal);
        ExecutionResultsLookup = ExecutionResults.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public IFileSystem FileSystem { get; } = new MemoryFileSystem();

    public required IConsole Console { get; init; }

    public ICookieContainer CookieContainer { get; } = new MemoryCookieContainer();

    public Dictionary<string, string> VariableValues { get; }

    public Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> VariableValuesLookup { get; }

    public FrozenExecutionStep? CurrentStep { get; set; }

    public (string Value, bool IsGiven) CurrentCalculatedStepName { get; set; } = (string.Empty, false);

    public Dictionary<string, VariablePostProcessingWriterStrategy> ExecutionResults { get; }

    public Dictionary<string, VariablePostProcessingWriterStrategy>.AlternateLookup<ReadOnlySpan<char>> ExecutionResultsLookup { get; }

    public List<string> AssertionViolations { get; } = new();
}

internal class Executor(ExecutionPlan plan, IConsole console)
{
    public async Task<bool> ExecuteAsync()
    {
        ExecutionContext ctx = new ExecutionContext() { Console = console };
        foreach (var step in plan.Steps)
        {
            ctx.CurrentStep = step;
            // Add or update variable state in the context.
            foreach (var newVar in step.Variables)
                ctx.VariableValues[newVar.Name] = newVar.Value;

            // Variable preprocessing
            var uri = new Uri(VariablePreprocessor.Evaluate(step.Uri, ctx.VariableValuesLookup, ctx.ExecutionResultsLookup));
            ctx.CurrentCalculatedStepName = GetStepId(step, uri, ctx);
            ctx.Console.WriteLine($"Executing {ctx.CurrentCalculatedStepName.Value}");
            List<KeyValueDescriptor> headers = [];
            foreach (var header in step.Headers)
                headers.Add(new(header.GetKey(), VariablePreprocessor.Evaluate(header.GetValue(), ctx.VariableValuesLookup, ctx.ExecutionResultsLookup)));
            var timeout = ProcessVariable(step.Timeout, ctx, nameof(step.Timeout));
            var enableRedirects = ProcessVariable(step.EnableRedirects, ctx, nameof(step.EnableRedirects));
            var enableCertificateValidation = !ProcessVariable(step.NoCertificateValidation, ctx, nameof(step.NoCertificateValidation));
            var httpBehavior = new HttpBehavior(timeout, false, string.Empty, new SocketBehavior(enableRedirects, enableCertificateValidation, UseKerberosAuth: false, 1));
            var requestDetails = new HttpRequestDetails(new HttpMethod(step.Method), uri, step.Version, headers);
            HttpContent? body = step.Body.Count > 0 ? new StringLinesContent(step.Body.Select(x => VariablePreprocessor.Evaluate(x, ctx.VariableValuesLookup, ctx.ExecutionResultsLookup)).ToArray()) : null;
            if (!step.IsPerformanceRequest)
            {
                ctx.ExecutionResults.TryAdd(ctx.CurrentCalculatedStepName.Value, new VariablePostProcessingWriterStrategy(ctx.CurrentCalculatedStepName.IsGiven));
                await SendRequestAsync(httpBehavior, requestDetails, body, ctx);
            }
            else
            {
                // Variable preprocessing
                var clientsCount = ProcessVariable(step.ClientsCount, ctx, nameof(step.ClientsCount));
                var requestCount = ProcessVariable(step.RequestsCount, ctx, nameof(step.RequestsCount));
                var sharedsocket = ProcessVariable(step.SharedSocket, ctx, nameof(step.SharedSocket));
                var perfBehavior = new PerformanceBehavior(requestCount, clientsCount, sharedsocket);
                await PerfMeasureAsync(httpBehavior, requestDetails, perfBehavior, body, ctx);
            }
        }
        return ctx.AssertionViolations.Count == 0;
    }

    private static T ProcessVariable<T>(VarValue<T> value, ExecutionContext ctx, string name)
        where T : ISpanParsable<T>
    {
        if (value.HasValue)
            return value.Value;
        else if (T.TryParse(VariablePreprocessor.Evaluate(value.VariableValue, ctx.VariableValuesLookup, ctx.ExecutionResultsLookup).AsSpan(), null, out T? result))
            return result;
        throw new ArgumentException($"Invalid value set for {name}");
    }

    private static async Task SendRequestAsync(HttpBehavior httpBehavior, HttpRequestDetails requestDetails, HttpContent? body, ExecutionContext ctx)
    {
        if (body is not null)
            requestDetails = requestDetails with { Content = body };
        var writer = ctx.ExecutionResults[ctx.CurrentCalculatedStepName.Value];
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
        var cookieContainer = new MemoryCookieContainer();
        var assertionHandler = new StatsAssertionHandler(step);

        var console = ctx.Console;
        var printer = new StatsChainingPrinter(assertionHandler, new StatisticsPrinter(console));
        var orchestrator = new PerformanceMeasureOrchestrator(printer, new NoOpConsole(), new Awaiter(), cookieContainer, new SingleSocketsHandlerProvider(), performanceBehavior);
        await orchestrator.RunAsync(requestDetails, httpBehavior, CancellationToken.None);
        var viloations = assertionHandler.GetViolations();
        if (viloations.Count == 0)
            return;
        ctx.AssertionViolations.AddRange(viloations);
        ctx.Console.WriteLine($"ASSERTION VIOLATION");
        foreach (var violation in viloations)
            console.WriteLine(violation);
    }

    private static (string Value, bool IsGiven) GetStepId(FrozenExecutionStep step, Uri uri, ExecutionContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(step.Name))
            return (VariablePreprocessor.Evaluate(step.Name, ctx.VariableValuesLookup, ctx.ExecutionResultsLookup), true);
        else
            return ($"{step.Method} {uri} at L{step.LineNumber}", false);
    }
}
