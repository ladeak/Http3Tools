﻿using System.Net;
using CHttp.EventListeners;
using CHttp.Writers;

namespace CHttp;

internal class PerformanceMeasureOrchestrator
{
    private readonly IStatisticsPrinter _summaryPrinter;
    private readonly int _requestCount;
    private readonly int _clientsCount;
    private readonly ProgressBar<Ratio<int>> _progressBar;
    private readonly CancellationTokenSource _cts;
    private Task? _progressBarTask;
    private int _requestCompleted;
    private int _requestStarting;

    public PerformanceMeasureOrchestrator(IStatisticsPrinter summaryPrinter, IConsole console, IAwaiter awaiter, int requestCount, int clientsCount)
    {
        _summaryPrinter = summaryPrinter ?? throw new ArgumentNullException(nameof(summaryPrinter));
        _requestCount = requestCount;
        _clientsCount = clientsCount;
        _progressBar = new ProgressBar<Ratio<int>>(console, awaiter);
        _cts = new();
    }

    public async Task RunAsync(HttpRequestDetails requestDetails, HttpBehavior httpBehavior)
    {
        _progressBarTask = _progressBar.RunAsync<RatioFormatter<int>>(_cts.Token);
        var clientTasks = new Task<IEnumerable<Summary>>[_clientsCount];
        INetEventListener readListner = requestDetails.Version == HttpVersion.Version30 ? new QuicEventListener() : new SocketEventListener();
        for (int i = 0; i < _clientsCount; i++)
            clientTasks[i] = Task.Run(() => RunClient(requestDetails, httpBehavior));
        await Task.WhenAll(clientTasks);
        await readListner.WaitUpdateAndStopAsync();
        await CompleteProgressBarAsync();
        _summaryPrinter.SummarizeResults(clientTasks.SelectMany(x => x.Result), readListner.GetBytesRead());
    }

    private async Task CompleteProgressBarAsync()
    {
        _progressBar.Set(new Ratio<int>(_requestCompleted, _requestCount));
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
            _progressBar.Set(new Ratio<int>(completed, _requestCount));
        }
        await writer.CompleteAsync(CancellationToken.None);

        // Skip the first request as that is warm up.
        return writer.Summaries.Skip(1);
    }
}