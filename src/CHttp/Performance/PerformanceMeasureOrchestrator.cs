using System.Diagnostics;
using System.Net;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.EventListeners;
using CHttp.Http;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;
using CHttp.Writers;

namespace CHttp.Performance;

internal class PerformanceMeasureOrchestrator
{
    private readonly ISummaryPrinter _summaryPrinter;
    private readonly ICookieContainer _cookieContainer;
    private readonly BaseSocketsHandlerProvider _socketsProvider;
    private readonly PerformanceBehavior _behavior;
    private readonly ProgressBar<Ratio<int>> _progressBar;
    private readonly CancellationTokenSource _cts;
    private Task? _progressBarTask;
    private int _requestCompleted;
    private int _requestStarting;
    private long _startTimestamp;

    public PerformanceMeasureOrchestrator(ISummaryPrinter summaryPrinter,
        IConsole console,
        IAwaiter awaiter,
        ICookieContainer cookieContainer,
        BaseSocketsHandlerProvider socketsProvider,
        PerformanceBehavior behavior)
    {
        _summaryPrinter = summaryPrinter ?? throw new ArgumentNullException(nameof(summaryPrinter));
        _cookieContainer = cookieContainer ?? throw new ArgumentNullException(nameof(cookieContainer));
        _socketsProvider = socketsProvider ?? throw new ArgumentNullException(nameof(socketsProvider));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
        _progressBar = new ProgressBar<Ratio<int>>(console, awaiter);
        _cts = new();
        ThreadPool.GetMinThreads(out var workerThreadCount, out var completionPortThreadsCount);
        if (workerThreadCount < _behavior.ClientsCount)
            ThreadPool.SetMinThreads(_behavior.ClientsCount, completionPortThreadsCount);
    }

    public async Task RunAsync(HttpRequestDetails requestDetails, HttpBehavior httpBehavior, CancellationToken token = default)
    {
        using var a = new HttpMericsListener();
        _progressBarTask = _progressBar.RunAsync<RatioFormatter<int>>(_cts.Token);
        var clientTasks = new Task<IEnumerable<Summary>>[_behavior.ClientsCount];
        INetEventListener readListener = requestDetails.Version == HttpVersion.Version30 ? new QuicEventListener() : new SocketEventListener();

        _startTimestamp = Stopwatch.GetTimestamp();
        for (int i = 0; i < _behavior.ClientsCount; i++)
            clientTasks[i] = Task.Run(() => RunClient(requestDetails, httpBehavior, token), token);
        await Task.WhenAll(clientTasks);
        await readListener.WaitUpdateAndStopAsync();
        await CompleteProgressBarAsync();

        await _summaryPrinter.SummarizeResultsAsync(new PerformanceMeasurementResults()
        {
            Summaries = new KnowSizeEnumerableCollection<Summary>(clientTasks.SelectMany(x => x.Result), _requestCompleted),
            TotalBytesRead = readListener.GetBytesRead(),
            Behavior = _behavior
        });
    }

    private async Task CompleteProgressBarAsync()
    {
        _progressBar.Set(new Ratio<int>(_requestCompleted, _behavior.RequestCount, TimeSpan.Zero));
        _cts.Cancel();
        if (_progressBarTask != null)
            await _progressBarTask;
    }

    private async Task<IEnumerable<Summary>> RunClient(HttpRequestDetails requestDetails, HttpBehavior httpBehavior, CancellationToken token = default)
    {
        var writer = new SummaryWriter();
        var client = new HttpMessageSender(writer, _cookieContainer, _socketsProvider, httpBehavior);

        // Warm up
        await client.SendRequestAsync(requestDetails);

        // Measured requests
        while (Interlocked.Increment(ref _requestStarting) <= _behavior.RequestCount && !token.IsCancellationRequested)
        {
            await client.SendRequestAsync(requestDetails);
            var completed = Interlocked.Increment(ref _requestCompleted);
            var currentTimestamp = Stopwatch.GetTimestamp();
            var reaminingTime = TimeSpan.FromTicks((long)((currentTimestamp - _startTimestamp) / (double)completed * (_behavior.RequestCount - completed)));
            _progressBar.Set(new Ratio<int>(completed, _behavior.RequestCount, reaminingTime));
        }
        await writer.CompleteAsync(CancellationToken.None);
        await _cookieContainer.SaveAsync();

        // Skip the first request as that is warm up.
        return writer.Summaries.Skip(1);
    }
}
