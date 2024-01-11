using System.Diagnostics.Metrics;
using Azure.Monitor.OpenTelemetry.Exporter;
using CHttp.Abstractions;
using CHttp.Statitics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace CHttp;

internal class AppInsightsPrinter(IConsole console, string? metricsConnectionString = null) : ISummaryPrinter
{
    private readonly IConsole _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly string? _metricsConnectionString = metricsConnectionString;

    public ValueTask SummarizeResultsAsync(PerformanceMeasurementResults session)
    {
        if (string.IsNullOrWhiteSpace(_metricsConnectionString))
        {
            return ValueTask.CompletedTask;
        }
        Meter Meter = new("CHttp");
        Histogram<long> Requests = Meter.CreateHistogram<long>(nameof(Requests));
        MeterProvider? metricsProvider = Sdk.CreateMeterProviderBuilder()
              .ConfigureResource(b => b.AddService("CHttp"))
              .AddMeter("CHttp")
              .AddAzureMonitorMetricExporter(options => { options.ConnectionString = _metricsConnectionString; }).Build();

        var summaries = session.Summaries;
        if (metricsProvider is null || summaries.Count == 0)
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

        metricsProvider.ForceFlush();
        metricsProvider.Dispose();
        return ValueTask.CompletedTask;
    }
}
