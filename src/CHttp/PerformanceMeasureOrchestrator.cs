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
    private int _requestCompleted;

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
        await SummarizeResults(clientTasks.SelectMany(x => x.Result));
    }

    private async Task<IEnumerable<Summary>> RunClient(HttpRequestDetails requestDetails, HttpBehavior httpBehavior)
    {
        var writer = new SummaryWriter();
        var client = new HttpMessageSender(writer, httpBehavior);

        //Warm up
        await client.SendRequestAsync(requestDetails);

        for (int j = 0; j < _requestCount / _clientsCount; j++)
        {
            await client.SendRequestAsync(requestDetails);
            _progressBar.Set(Interlocked.Increment(ref _requestCompleted));
        }
        await writer.CompleteAsync(CancellationToken.None);

        // Skip the first request as that is warm up.
        return writer.Summaries.Skip(1);
    }

    private async Task SummarizeResults(IEnumerable<Summary> summaries)
    {
        _cts.Cancel();
        if (_progressBarTask != null)
            await _progressBarTask;

        double average = summaries.Average(x => x.RequestActivity.Duration.Ticks);
        double displayAverage = 0;
        string qualifier;
        if (average > TimeSpan.TicksPerMinute)
        {
            displayAverage = average / TimeSpan.TicksPerMinute;
            qualifier = "m";
        }
        else if (average > TimeSpan.TicksPerSecond)
        {
            displayAverage = average / TimeSpan.TicksPerSecond;
            qualifier = "s";
        }
        else if (average > TimeSpan.TicksPerMillisecond)
        {
            displayAverage = average / TimeSpan.TicksPerMillisecond;
            qualifier = "ms";
        }
        else if (average > TimeSpan.TicksPerMicrosecond)
        {
            displayAverage = average / TimeSpan.TicksPerMicrosecond;
            qualifier = "us";
        }
        else
        {
            displayAverage = average * 100;
            qualifier = "ns";
        }
        _console.WriteLine($"{displayAverage:F3} {qualifier}");
    }
}
