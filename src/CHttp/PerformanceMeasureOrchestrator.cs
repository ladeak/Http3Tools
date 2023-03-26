using CHttp.Writers;

namespace CHttp;

internal class PerformanceMeasureOrchestrator
{
    private readonly IConsole _console;
    private readonly int _requestCount;
    private readonly int _clientsCount;
    private readonly ProgressBar<int> _progressBar;
    private readonly CancellationTokenSource _cts;
    private Task? _progressBarTask;

    public PerformanceMeasureOrchestrator(IConsole console, IAwaiter awaiter, int requestCount, int clientsCount)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _requestCount = requestCount;
        _clientsCount = clientsCount;
        _progressBar = new ProgressBar<int>(console, awaiter);
        _cts = new();
    }

    public async Task RunAsync(HttpRequestDetails requestDetails, HttpBehavior httpBehavior)
    {
        _progressBarTask = _progressBar.RunAsync<CountFormatter<int>>(_cts.Token);
        var clientTasks = new Task<IEnumerable<Summary>>[_clientsCount];
        for (int i = 0; i < _clientsCount; i++)
            clientTasks[i] = Task.Run(() => RunClient(requestDetails, httpBehavior));
        await Task.WhenAll(clientTasks);
        await SummarizeResults(clientTasks.SelectMany(x=>x.Result));
    }

    private async Task<IEnumerable<Summary>> RunClient(HttpRequestDetails requestDetails, HttpBehavior httpBehavior)
    {
        var writer = new StatisticsWriter();
        var client = new HttpMessageSender(writer);
        for (int j = 0; j < _requestCount / _clientsCount; j++)
        {
            await client.SendRequestAsync(requestDetails, httpBehavior);
        }
        await writer.CompleteAsync(CancellationToken.None);
        return writer.Summaries;
    }

    private async Task SummarizeResults(IEnumerable<Summary> summaries)
    {
        _cts.Cancel();
        if (_progressBarTask != null)
            await _progressBarTask;


    }
}
