using System.Diagnostics.Metrics;
using CHttp.Abstractions;
using CHttp.Performance.Data;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace CHttp.Performance.Statitics;

internal class OpenTelemtryPrinter(IConsole console, string? metricsConnectionString = null) : ISummaryPrinter
{
    private readonly IConsole _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly string? _metricsConnectionString = metricsConnectionString;

    public ValueTask SummarizeResultsAsync(PerformanceMeasurementResults session)
    {
        if (string.IsNullOrWhiteSpace(_metricsConnectionString))
        {
            return ValueTask.CompletedTask;
        }
        Uri endpoint;
        string header = string.Empty;
        var connectionStringSplitIndex = _metricsConnectionString.IndexOf(';');
        if (connectionStringSplitIndex > -1)
        {
            endpoint = new Uri(_metricsConnectionString.Substring(0, connectionStringSplitIndex));
            header = _metricsConnectionString.Substring(connectionStringSplitIndex + 1);
        }
        else
        {
            endpoint = new Uri(_metricsConnectionString);
        }

        Meter Meter = new("CHttp");
        Histogram<long> Requests = Meter.CreateHistogram<long>(nameof(Requests));
        MeterProvider? metricsProvider = Sdk.CreateMeterProviderBuilder()
              .ConfigureResource(b => b.AddService("CHttp"))
              .AddMeter("CHttp")
              .AddOtlpExporter(options =>
              {
                  options.Endpoint = endpoint;
                  options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                  if (!string.IsNullOrWhiteSpace(header))
                      options.Headers = $"x-otlp-api-key={header}";
              })
              .Build();

        var summaries = session.Summaries;
        if (metricsProvider is null || summaries.Count == 0)
            return ValueTask.CompletedTask;

        _console.WriteLine("Publishing metrics...");
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
