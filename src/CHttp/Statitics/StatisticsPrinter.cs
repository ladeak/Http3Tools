using System.Numerics;
using CHttp.Writers;

namespace CHttp.Statitics;

internal class StatisticsPrinter : ISummaryPrinter
{
    private readonly IConsole _console;

    internal StatisticsPrinter(IConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public ValueTask SummarizeResultsAsync(IReadOnlyCollection<Summary> summaries, long bytesRead)
    {
        if (summaries.Count == 0)
        {
            _console.WriteLine("No measurements available");
            return ValueTask.CompletedTask;
        }

        var durations = new long[summaries.Count];

        long totalTicks = 0;
        int[] statusCodes = new int[6];
        int current = 0;
        long earliestStart = long.MaxValue;
        long latestEnd = long.MinValue;
        foreach (var item in summaries)
        {
            durations[current++] = item.Duration.Ticks;
            totalTicks += item.Duration.Ticks;
            var statusCode = item.HttpStatusCode;
            if (statusCode.HasValue && statusCode.Value < 600)
                statusCodes[statusCode.Value / 100 - 1]++;
            if (item.ErrorCode != ErrorType.None)
                statusCodes[5]++;
            if (item.StartTime < earliestStart)
                earliestStart = item.StartTime;
            if (item.EndTime > latestEnd)
                latestEnd = item.EndTime;
        }
        Array.Sort(durations);

        var mean = totalTicks / (double)summaries.Count;
        double stdDev = Math.Sqrt(CalcSquaredStdDev(durations, mean));
        double error = stdDev / Math.Sqrt(summaries.Count);
        double requestSec = (double)summaries.Count * TimeSpan.TicksPerSecond / (latestEnd - earliestStart);
        double throughput = bytesRead / (mean / TimeSpan.TicksPerSecond);

        (var displayMean, var meanQualifier) = Display(mean);
        (var displayStdDev, var stdDevQualifier) = Display(stdDev);
        (var displayError, var errorQualifier) = Display(error);
        (var displayMinResponseTime, var minResponseTimeQualifier) = Display(durations[0]);
        (var displayMaxResponseTime, var maxResponseTimeQualifier) = Display(durations[^1]);
        (var displayMedian, var medianQualifier) = Display(durations[summaries.Count / 2]);
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
            PrintHistogram(durations, error, scaleNormalize);
        }
        _console.WriteLine(separator);
        PrintStatusCodes(statusCodes);
        _console.WriteLine(separator);
        return ValueTask.CompletedTask;
    }

    private double CalcSquaredStdDev(long[] durations, double mean)
    {
        var avg = new Vector<double>(mean);
        var input = durations.AsSpan();
        double sum = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var vSize = Vector<long>.Count;
            while (input.Length >= vSize)
            {
                var vInput = Vector.ConvertToDouble(new Vector<long>(input));
                var difference = Vector.Subtract(avg, vInput);
                var squared = Vector.Multiply(difference, difference);
                sum += Vector.Sum(squared);
                input = input.Slice(vSize);
            }
        }

        // Remaining
        while (input.Length > 0)
        {
            var difference = mean - input[0];
            sum += difference * difference;
            input = input.Slice(1);
        }

        return sum / durations.Length;
    }

    private void PrintStatusCodes(int[] statusCodes)
    {
        _console.WriteLine("HTTP status codes:");
        _console.WriteLine($"1xx - {statusCodes[0]}, 2xx - {statusCodes[1]}, 3xx - {statusCodes[2]}, 4xx - {statusCodes[3]}, 5xx - {statusCodes[4]}, Other - {statusCodes[5]}");
    }

    private void PrintHistogram(long[] durations, double error, double scaleNormalize)
    {
        var min = durations[0];
        var max = durations[^1];
        error = error == 0 ? 1 : error;
        double bucketCount = Math.Max(Math.Min(10, (max - min) / error), 5);
        var bucketSize = new Vector<double>((max - min) / bucketCount);

        var bucketLimit = new Vector<double>(min);
        var input = durations.AsSpan();
        for (int i = 0; i < bucketCount; i++)
        {
            bucketLimit += bucketSize;
            long currentCounter = 0;
            var vSize = Vector<long>.Count;
            int oneCnt = vSize;
            while (oneCnt == vSize && input.Length >= vSize)
            {
                var vInput = Vector.ConvertToDouble(new Vector<long>(input));
                oneCnt = (int)Vector.Sum(Vector.LessThanOrEqual(vInput, bucketLimit)) * -1;
                currentCounter += oneCnt;
                input = input.Slice(oneCnt);
            }
            if (input.Length < vSize && input.Length > 0)
                currentCounter += input.Length;

            (var limit, var limitQualifier) = Display(bucketLimit[0]);
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
