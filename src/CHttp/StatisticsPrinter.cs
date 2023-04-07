using CHttp.Writers;

namespace CHttp;

internal class StatisticsPrinter : IStatisticsPrinter
{
    private readonly IConsole _console;

    internal StatisticsPrinter(IConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public void SummarizeResults(IReadOnlyCollection<Summary> summaries, long bytesRead)
    {
        if (!summaries.Any())
        {
            _console.WriteLine("No measurements available");
            return;
        }

        var durations = new long[summaries.Count];

        long totalTicks = 0;
        int[] statusCodes = new int[6];
        int current = 0;
        foreach (var item in summaries)
        {
            durations[current++] = item.RequestActivity.Duration.Ticks;
            totalTicks += item.RequestActivity.Duration.Ticks;
            var statusCode = item.HttpStatusCode;
            if (statusCode.HasValue && statusCode.Value < 600)
                statusCodes[(statusCode.Value / 100) - 1]++;
            if (item.ErrorCode != ErrorType.None)
                statusCodes[5]++;
        }
        Array.Sort(durations);

        long min = durations[0];
        long max = durations[^1];
        var mean = totalTicks / (double)summaries.Count;
        double squaredStdDev = CalcSquaredStdDev(durations, mean);
        double stdDev = Math.Sqrt(squaredStdDev);
        double error = stdDev / Math.Sqrt(summaries.Count);

        double requestSec = (double)summaries.Count * TimeSpan.TicksPerSecond / totalTicks;

        (var displayMean, var meanQualifier) = Display(mean);
        (var displayStdDev, var stdDevQualifier) = Display(stdDev);
        (var displayError, var errorQualifier) = Display(error);
        (var displayMinResponseTime, var minResponseTimeQualifier) = Display(min);
        (var displayMaxResponseTime, var maxResponseTimeQualifier) = Display(max);
        (var displayMedian, var medianQualifier) = Display(durations[summaries.Count / 2]);
        var throughput = bytesRead / (mean / TimeSpan.TicksPerSecond);
        (var throughputFormatted, var throughputQualifier) = SizeFormatter<double>.FormatSizeWithQualifier(throughput);

        _console.WriteLine($"| Mean:       {displayMean,10:F3} {meanQualifier}   |");
        _console.WriteLine($"| StdDev:     {displayStdDev,10:F3} {stdDevQualifier}   |");
        _console.WriteLine($"| Error:      {displayError,10:F3} {errorQualifier}   |");
        _console.WriteLine($"| Median:     {displayMedian,10:F3} {medianQualifier}   |");
        _console.WriteLine($"| Min:        {displayMinResponseTime,10:F3} {minResponseTimeQualifier}   |");
        _console.WriteLine($"| Max:        {displayMaxResponseTime,10:F3} {maxResponseTimeQualifier}   |");
        _console.WriteLine($"| Throughput: {throughputFormatted,10} {throughputQualifier}B/s |");
        _console.WriteLine($"| Req/Sec:    {requestSec,10:G3}      |");

        int lineLength = _console.WindowWidth;
        var scaleNormalize = (double)lineLength / summaries.Count;
        string separator = new string('-', lineLength);
        // Histogram
        if (summaries.Count >= 100)
        {
            _console.WriteLine(separator);
            PrintHistogram(durations, min, max, error, scaleNormalize);
        }
        _console.WriteLine(separator);
        PrintStatusCodes(statusCodes);
        _console.WriteLine(separator);
    }

    private double CalcSquaredStdDev(long[] durations, double mean)
    {
        return durations.Sum(x => Math.Pow(mean - x, 2)) / durations.Length;
    }

    private void PrintStatusCodes(int[] statusCodes)
    {
        _console.WriteLine("HTTP status codes:");
        _console.WriteLine($"1xx - {statusCodes[0]}, 2xx - {statusCodes[1]}, 3xx - {statusCodes[2]}, 4xx - {statusCodes[3]}, 5xx - {statusCodes[4]}, Other - {statusCodes[5]}");
    }

    private void PrintHistogram(long[] durations, long min, long max, double error, double scaleNormalize)
    {
        double bucketCount = Math.Max(Math.Min(10, (max - min) / error), 5);
        var bucketSize = (max - min) / bucketCount;

        int j = 0;
        double bucketLimit = min;
        for (int i = 0; i < bucketCount; i++)
        {
            bucketLimit += bucketSize;
            int currentCounter = 0;
            while (j < durations.Length && bucketLimit >= durations[j++])
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
        if (value >= TimeSpan.TicksPerMinute)
        {
            displayAverage = value / TimeSpan.TicksPerMinute;
            qualifier = "m ";
        }
        else if (value >= TimeSpan.TicksPerSecond)
        {
            displayAverage = value / TimeSpan.TicksPerSecond;
            qualifier = "s ";
        }
        else if (value >= TimeSpan.TicksPerMillisecond)
        {
            displayAverage = value / TimeSpan.TicksPerMillisecond;
            qualifier = "ms";
        }
        else if (value >= TimeSpan.TicksPerMicrosecond)
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
