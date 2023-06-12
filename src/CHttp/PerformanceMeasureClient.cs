using System.Net;
using CHttp.Abstractions;
using CHttp.EventListeners;
using CHttp.Statitics;
using CHttp.Writers;

namespace CHttp;

public class PerformanceMeasureClient
{
    public record PerformanceOptions(int RequestCount, int ClientsCount, string AppInsightsConnectionString);

    private readonly ISummaryPrinter _summaryPrinter;
    private readonly int _requestCount;
    private readonly int _clientsCount;
    private int _requestCompleted;
    private int _requestStarting;

    public PerformanceMeasureClient(PerformanceOptions options)
    {
        var noConsole = new NoOpConsole();
        _summaryPrinter = new CompositePrinter(new StatisticsPrinter(noConsole),
            new AppInsightsPrinter(noConsole, options.AppInsightsConnectionString));
        _requestCount = options.RequestCount;
        _clientsCount = options.ClientsCount;
    }

    public async Task RunAsync(HttpClient client, Func<HttpRequestMessage> requestFactory)
    {
        var clientTasks = new Task<IEnumerable<Summary>>[_clientsCount];
        INetEventListener readListener = new SocketEventListener();
        for (int i = 0; i < _clientsCount; i++)
            clientTasks[i] = Task.Run(() => RunClient(client, requestFactory));
        await Task.WhenAll(clientTasks);
        await readListener.WaitUpdateAndStopAsync();
        await _summaryPrinter.SummarizeResultsAsync(new KnowSizeEnumerableCollection<Summary>(clientTasks.SelectMany(x => x.Result), _requestCompleted), readListener.GetBytesRead());
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
        }
        await writer.CompleteAsync(CancellationToken.None);

        // Skip the first request as that is warm up.
        return writer.Summaries.Skip(1);
    }
}
