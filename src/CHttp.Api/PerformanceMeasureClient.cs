using CHttp.Abstractions;
using CHttp.Data;
using CHttp.EventListeners;
using CHttp.Http;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;
using CHttp.Writers;

namespace CHttp;

public class PerformanceMeasureClient
{
    public record PerformanceOptions(int RequestCount, int ClientsCount, string? AppInsightsConnectionString = null);

    private readonly ISummaryPrinter _summaryPrinter;
    private readonly int _requestCount;
    private readonly int _clientsCount;
    private int _requestCompleted;
    private int _requestStarting;

    public PerformanceMeasureClient(PerformanceOptions options)
    {
        var noConsole = new NoOpConsole();
        _summaryPrinter = new CompositePrinter(new StatisticsPrinter(noConsole),
            new OpenTelemtryPrinter(noConsole, options.AppInsightsConnectionString));
        _requestCount = options.RequestCount;
        _clientsCount = options.ClientsCount;
    }

    public async Task<Stats?> RunAsync(HttpClient client, Func<HttpRequestMessage> requestFactory)
    {
        var clientTasks = new Task<IEnumerable<Summary>>[_clientsCount];
        INetEventListener readListener = new SocketEventListener();
        for (int i = 0; i < _clientsCount; i++)
            clientTasks[i] = Task.Run(() => RunClient(client, requestFactory));
        await Task.WhenAll(clientTasks);
        await readListener.WaitUpdateAndStopAsync();
        var session = new PerformanceMeasurementResults()
        {
            Summaries = new KnowSizeEnumerableCollection<Summary>(clientTasks.SelectMany(x => x.Result), _requestCompleted),
            TotalBytesRead = readListener.GetBytesRead(),
            MaxConnections = 0,
            Behavior = new(_requestCount, _clientsCount, false)
        };
        await _summaryPrinter.SummarizeResultsAsync(session);
        if (session.Summaries.Count == 0)
            return null;
        return StatisticsCalculator.GetStats(session);
    }

    private async Task<IEnumerable<Summary>> RunClient(HttpClient httpClient, Func<HttpRequestMessage> requestFactory)
    {
        var writer = new SummaryWriter();
        var client = new HttpMessageSender(writer, httpClient);

        // Warm up
        await client.SendRequestAsync(requestFactory());

        // Measured requests
        while (Interlocked.Increment(ref _requestStarting) <= _requestCount)
        {
            await client.SendRequestAsync(requestFactory());
            Interlocked.Increment(ref _requestCompleted);
        }
        await writer.CompleteAsync(CancellationToken.None);

        // Skip the first request as that is warm up.
        return writer.Summaries.Skip(1);
    }
}
