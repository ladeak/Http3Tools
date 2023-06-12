using System.Diagnostics;
using System.Net;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.EventListeners;
using CHttp.Statitics;
using CHttp.Writers;

namespace CHttp;

internal class PerformanceMeasureOrchestrator
{
    private readonly ISummaryPrinter _summaryPrinter;
    private readonly int _requestCount;
    private readonly int _clientsCount;
    private readonly ProgressBar<Ratio<int>> _progressBar;
    private readonly CancellationTokenSource _cts;
    private Task? _progressBarTask;
    private int _requestCompleted;
    private int _requestStarting;
    private long _startTimestamp;

    public PerformanceMeasureOrchestrator(ISummaryPrinter summaryPrinter, IConsole console, IAwaiter awaiter, PerformanceBehavior behavior)
    {
        _summaryPrinter = summaryPrinter ?? throw new ArgumentNullException(nameof(summaryPrinter));
        _requestCount = behavior.RequestCount;
        _clientsCount = behavior.ClientsCount;
        _progressBar = new ProgressBar<Ratio<int>>(console, awaiter);
        _cts = new();
    }

    public async Task RunAsync(HttpRequestDetails requestDetails, HttpBehavior httpBehavior)
    {
        _startTimestamp = Stopwatch.GetTimestamp();
        _progressBarTask = _progressBar.RunAsync<RatioFormatter<int>>(_cts.Token);
        var clientTasks = new Task<IEnumerable<Summary>>[_clientsCount];
        INetEventListener readListener = requestDetails.Version == HttpVersion.Version30 ? new QuicEventListener() : new SocketEventListener();
        for (int i = 0; i < _clientsCount; i++)
            clientTasks[i] = Task.Run(() => RunClient(requestDetails, httpBehavior));
        await Task.WhenAll(clientTasks);
        await readListener.WaitUpdateAndStopAsync();
        await CompleteProgressBarAsync();

        await _summaryPrinter.SummarizeResultsAsync(new PerformanceMeasurementResults()
        {
            Summaries = new KnowSizeEnumerableCollection<Summary>(clientTasks.SelectMany(x => x.Result), _requestCompleted),
            TotalBytesRead = readListener.GetBytesRead(),
            Behavior = new(_requestCount, _clientsCount)
        });
    }

    private async Task CompleteProgressBarAsync()
    {
        _progressBar.Set(new Ratio<int>(_requestCompleted, _requestCount, TimeSpan.Zero));
        _cts.Cancel();
        if (_progressBarTask != null)
            await _progressBarTask;
    }

    private async Task<IEnumerable<Summary>> RunClient(HttpRequestDetails requestDetails, HttpBehavior httpBehavior)
    {
        var writer = new SummaryWriter();
        var client = new HttpMessageSender(writer, httpBehavior);

        // Warm up
        await client.SendRequestAsync(requestDetails);

        // Measured requests
        while (Interlocked.Increment(ref _requestStarting) <= _requestCount)
        {
            await client.SendRequestAsync(requestDetails);
            var completed = Interlocked.Increment(ref _requestCompleted);
            var currentTimestamp = Stopwatch.GetTimestamp();
            var reaminingTime = TimeSpan.FromTicks((long)((currentTimestamp - _startTimestamp) / (double)completed * (_requestCount - completed)));
            _progressBar.Set(new Ratio<int>(completed, _requestCount, reaminingTime));
        }
        await writer.CompleteAsync(CancellationToken.None);

        // Skip the first request as that is warm up.
        return writer.Summaries.Skip(1);
    }
}
