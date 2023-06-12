using System.Diagnostics.Metrics;
using Azure.Monitor.OpenTelemetry.Exporter;
using CHttp.Abstractions;
using CHttp.Statitics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace CHttp;

internal class AppInsightsPrinter : ISummaryPrinter
{
    private static readonly Meter Meter = new("CHttp");
    private static readonly Histogram<long> Requests = Meter.CreateHistogram<long>(nameof(Requests));

    private readonly IConsole _console;
    private MeterProvider? _metricsProvider;

    public AppInsightsPrinter(IConsole console, string metricsConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(metricsConnectionString))
        {
            _metricsProvider = Sdk.CreateMeterProviderBuilder()
              .ConfigureResource(b => b.AddService("CHttp"))
              .AddMeter("CHttp")
              .AddAzureMonitorMetricExporter(options => { options.ConnectionString = metricsConnectionString; }).Build();
        }
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public ValueTask SummarizeResultsAsync(PerformanceMeasurementResults session)
    {
        var summaries = session.Summaries;
        if (_metricsProvider is null || summaries.Count == 0)
            return ValueTask.CompletedTask;
        _console.WriteLine("Sending metrics to AppInsights...");
        foreach (var summary in summaries)
            Requests.Record((long)summary.Duration.TotalMilliseconds, 
                new("Url", summary.Url),
                new("Error", summary.ErrorCode),
                new("Length", summary.Length), 
                new("HttpStatusCode", summary.HttpStatusCode),
                new("RequestCount", session.Behavior.RequestCount),
                new("ClientCount", session.Behavior.ClientsCount));

        _metricsProvider.ForceFlush();
        _metricsProvider.Dispose();
        return ValueTask.CompletedTask;
    }
}
