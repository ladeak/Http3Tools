using System.Net;
using CHttp.EventListeners;
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
    private int _requestStarting;

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
        INetEventListener readListner = requestDetails.Version == HttpVersion.Version30 ? new QuicEventListener() : new SocketEventListener();
        for (int i = 0; i < _clientsCount; i++)
            clientTasks[i] = Task.Run(() => RunClient(requestDetails, httpBehavior));
        await Task.WhenAll(clientTasks);
        await readListner.WaitUpdateAndStopAsync();
        await CompleteProgressBarAsync();
        await SummarizeResults(clientTasks.SelectMany(x => x.Result).ToList(), readListner.GetBytesRead());
    }

    private async Task CompleteProgressBarAsync()
    {
        _progressBar.Set(_requestCompleted);
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
            _progressBar.Set(Interlocked.Increment(ref _requestCompleted));
        }
        await writer.CompleteAsync(CancellationToken.None);

        // Skip the first request as that is warm up.
        return writer.Summaries.Skip(1);
    }

    private async Task SummarizeResults(ICollection<Summary> summaries, long bytesRead)
    {

        
        double minResponseTime = summaries.Min(x => x.RequestActivity.Duration.Ticks);
        double maxResponseTime = summaries.Max(x => x.RequestActivity.Duration.Ticks);

        long totalTicks = 0;
        long min = long.MaxValue;
        long max = long.MinValue;
        foreach (var item in summaries)
        {
            var ticks = item.RequestActivity.Duration.Ticks;
            totalTicks += ticks;
            if (ticks > max)
                max = ticks;
            if (ticks < min)
                min = ticks;
        }
        var mean = totalTicks / (double)summaries.Count;
        double squaredStdDev = summaries
            .Sum(x => Math.Pow(mean - x.RequestActivity.Duration.Ticks, 2)) / summaries.Count;
        double stdDev = Math.Sqrt(squaredStdDev);
        double error = stdDev / Math.Sqrt(summaries.Count);

        double requestSec = (double)summaries.Count * TimeSpan.TicksPerSecond / totalTicks;


        (var displayMean, var meanQualifier) = Display(mean);
        (var displayStdDev, var stdDevQualifier) = Display(stdDev);
        (var displayError, var errorQualifier) = Display(error);
        (var displayMinResponseTime, var minResponseTimeQualifier) = Display(minResponseTime);
        (var displayMaxResponseTime, var maxResponseTimeQualifier) = Display(maxResponseTime);
        var throughput = bytesRead / (mean / TimeSpan.TicksPerSecond);
        (var throughputFormatted, var throughputQualifier) = SizeFormatter<double>.FormatSizeWithQualifier(throughput);

        _console.WriteLine($"| Mean:       {displayMean,10:F3} {meanQualifier}   |");
        _console.WriteLine($"| StdDev:     {displayStdDev,10:F3} {stdDevQualifier}   |");
        _console.WriteLine($"| Error:      {displayError,10:F3} {errorQualifier}   |");
        _console.WriteLine($"| Min:        {displayMinResponseTime,10:F3} {minResponseTimeQualifier}   |");
        _console.WriteLine($"| Max:        {displayMaxResponseTime,10:F3} {maxResponseTimeQualifier}   |");
        _console.WriteLine($"| Throughput: {throughputFormatted,10} {throughputQualifier}B/s |");
        _console.WriteLine($"| Req/Sec:    {requestSec,10:F3}      |");

        //Histogram

    }

    private (double DisplayAverage, string Qualifier) Display(double mean)
    {
        double displayAverage;
        string qualifier;
        if (mean > TimeSpan.TicksPerMinute)
        {
            displayAverage = mean / TimeSpan.TicksPerMinute;
            qualifier = "m ";
        }
        else if (mean > TimeSpan.TicksPerSecond)
        {
            displayAverage = mean / TimeSpan.TicksPerSecond;
            qualifier = "s";
        }
        else if (mean > TimeSpan.TicksPerMillisecond)
        {
            displayAverage = mean / TimeSpan.TicksPerMillisecond;
            qualifier = "ms";
        }
        else if (mean > TimeSpan.TicksPerMicrosecond)
        {
            displayAverage = mean / TimeSpan.TicksPerMicrosecond;
            qualifier = "us";
        }
        else
        {
            displayAverage = mean * TimeSpan.NanosecondsPerTick;
            qualifier = "ns";
        }
        return (displayAverage, qualifier);
    }
}
