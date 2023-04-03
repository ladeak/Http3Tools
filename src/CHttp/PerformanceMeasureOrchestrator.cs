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

    private async Task SummarizeResults(IReadOnlyList<Summary> summaries, long bytesRead)
    {
        summaries = summaries.OrderBy(x => x.RequestActivity.Duration.Ticks).ToList();

        long totalTicks = 0;
        int[] statusCodes = new int[6];
        foreach (var item in summaries)
        {
            totalTicks += item.RequestActivity.Duration.Ticks;
            var statusCode = item.HttpStatusCode;
            if (statusCode.HasValue && statusCode.Value < 600)
                statusCodes[(statusCode.Value / 100) - 1]++;
            if (item.ErrorCode != ErrorType.None)
                statusCodes[5]++;
        }

        long min = summaries.First().RequestActivity.Duration.Ticks;
        long max = summaries.Last().RequestActivity.Duration.Ticks;
        var mean = totalTicks / (double)summaries.Count;
        double squaredStdDev = summaries
            .Sum(x => Math.Pow(mean - x.RequestActivity.Duration.Ticks, 2)) / summaries.Count;
        double stdDev = Math.Sqrt(squaredStdDev);
        double error = stdDev / Math.Sqrt(summaries.Count);

        double requestSec = (double)summaries.Count * TimeSpan.TicksPerSecond / totalTicks;


        (var displayMean, var meanQualifier) = Display(mean);
        (var displayStdDev, var stdDevQualifier) = Display(stdDev);
        (var displayError, var errorQualifier) = Display(error);
        (var displayMinResponseTime, var minResponseTimeQualifier) = Display(min);
        (var displayMaxResponseTime, var maxResponseTimeQualifier) = Display(max);
        (var displayMedian, var medianQualifier) = Display((summaries[summaries.Count / 2]).RequestActivity.Duration.Ticks);
        var throughput = bytesRead / (mean / TimeSpan.TicksPerSecond);
        (var throughputFormatted, var throughputQualifier) = SizeFormatter<double>.FormatSizeWithQualifier(throughput);

        _console.WriteLine($"| Mean:       {displayMean,10:F3} {meanQualifier}   |");
        _console.WriteLine($"| StdDev:     {displayStdDev,10:F3} {stdDevQualifier}   |");
        _console.WriteLine($"| Error:      {displayError,10:F3} {errorQualifier}   |");
        _console.WriteLine($"| Median:     {displayMedian,10:F3} {medianQualifier}   |");
        _console.WriteLine($"| Min:        {displayMinResponseTime,10:F3} {minResponseTimeQualifier}   |");
        _console.WriteLine($"| Max:        {displayMaxResponseTime,10:F3} {maxResponseTimeQualifier}   |");
        _console.WriteLine($"| Throughput: {throughputFormatted,10} {throughputQualifier}B/s |");
        _console.WriteLine($"| Req/Sec:    {requestSec,10:F3}      |");

        // Histogram
        if (summaries.Count < 100)
            return;

        int lineLength = 45;
        var scaleNormalize = (double)lineLength / summaries.Count;
        string separator = new string('-', lineLength + 14);
        _console.WriteLine(separator);
        PrintHistogram(summaries, min, max, error, scaleNormalize);
        _console.WriteLine(separator);
        PrintStatusCodes(statusCodes);
        _console.WriteLine(separator);
    }

    private void PrintStatusCodes(int[] statusCodes)
    {
        _console.WriteLine("HTTP status codes:");
        _console.WriteLine($"1xx - {statusCodes[0]}, 2xx - {statusCodes[1]}, 3xx - {statusCodes[2]}, 4xx - {statusCodes[3]}, 5xx - {statusCodes[4]}, Other - {statusCodes[5]}");
    }

    private void PrintHistogram(IReadOnlyList<Summary> summaries, long min, long max, double error, double scaleNormalize)
    {
        double bucketCount = Math.Max(Math.Min(10, (max - min) / error), 5);
        var bucketSize = (max - min) / bucketCount;

        int j = 0;
        double bucketLimit = min;
        for (int i = 0; i < bucketCount; i++)
        {
            bucketLimit += bucketSize;
            int currentCounter = 0;
            while (j < summaries.Count && bucketLimit >= summaries[j++].RequestActivity.Duration.Ticks)
                currentCounter++;

            (var limit, var limitQualifier) = Display(bucketLimit);
            _console.Write($"{limit,10:F3} {limitQualifier} ");
            _console.Write(new string('#', (int)Math.Round(scaleNormalize * currentCounter)));
            _console.WriteLine();
        }
    }

    private (double DisplayValue, string Qualifier) Display(double value)
    {
        double displayAverage;
        string qualifier;
        if (value > TimeSpan.TicksPerMinute)
        {
            displayAverage = value / TimeSpan.TicksPerMinute;
            qualifier = "m ";
        }
        else if (value > TimeSpan.TicksPerSecond)
        {
            displayAverage = value / TimeSpan.TicksPerSecond;
            qualifier = "s";
        }
        else if (value > TimeSpan.TicksPerMillisecond)
        {
            displayAverage = value / TimeSpan.TicksPerMillisecond;
            qualifier = "ms";
        }
        else if (value > TimeSpan.TicksPerMicrosecond)
        {
            displayAverage = value / TimeSpan.TicksPerMicrosecond;
            qualifier = "us";
        }
        else
        {
            displayAverage = value * TimeSpan.NanosecondsPerTick;
            qualifier = "ns";
        }
        return (displayAverage, qualifier);
    }
}
