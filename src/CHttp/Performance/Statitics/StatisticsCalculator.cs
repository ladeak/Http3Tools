using System.Diagnostics.Metrics;
using System.Numerics;
using CHttp.Data;
using CHttp.Performance.Data;

namespace CHttp.Performance.Statitics;

internal static class StatisticsCalculator
{
    private static readonly Meter Meter = new("CHttp");
    private static readonly Histogram<double> Mean = Meter.CreateHistogram<double>(nameof(Mean));
    private static readonly Histogram<double> StdDev = Meter.CreateHistogram<double>(nameof(StdDev));
    private static readonly Histogram<double> Error = Meter.CreateHistogram<double>(nameof(Error));
    private static readonly Histogram<double> Median = Meter.CreateHistogram<double>(nameof(Median));
    private static readonly Histogram<double> Min = Meter.CreateHistogram<double>(nameof(Min));
    private static readonly Histogram<double> Max = Meter.CreateHistogram<double>(nameof(Max));
    private static readonly Histogram<double> Percentile95 = Meter.CreateHistogram<double>(nameof(Percentile95));
    private static readonly Histogram<double> Throughput = Meter.CreateHistogram<double>(nameof(Throughput));
    private static readonly Histogram<double> RequestSec = Meter.CreateHistogram<double>("Req/Sec");

    public static Stats GetStats(PerformanceMeasurementResults session)
    {
        var summaries = session.Summaries;
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
        double throughput = session.TotalBytesRead / (mean / TimeSpan.TicksPerSecond);
        var min = durations[0];
        var max = durations[^1];
        var median = durations[summaries.Count / 2];
        var percentile95 = durations[(int)((durations.Length - 1) * 0.95)];

        var stats = new Stats(mean, stdDev, error, requestSec, throughput, min, max, median, percentile95, durations, statusCodes);

        var url = new KeyValuePair<string, object?>("Url", summaries.First().Url);
        var requestCount = new KeyValuePair<string, object?>("RequestCount", session.Behavior.RequestCount);
        var clientCount = new KeyValuePair<string, object?>("ClientCount", session.Behavior.ClientsCount);
        Mean.Record(TimeSpan.FromTicks((int)stats.Mean).TotalMilliseconds, url, requestCount, clientCount);
        StdDev.Record(TimeSpan.FromTicks((int)stats.StdDev).TotalMilliseconds, url, requestCount, clientCount);
        Error.Record(TimeSpan.FromTicks((int)stats.Error).TotalMilliseconds, url, requestCount, clientCount);
        Median.Record(TimeSpan.FromTicks(stats.Median).TotalMilliseconds, url, requestCount, clientCount);
        Min.Record(TimeSpan.FromTicks(stats.Min).TotalMilliseconds, url, requestCount, clientCount);
        Max.Record(TimeSpan.FromTicks(stats.Max).TotalMilliseconds, url, requestCount, clientCount);
        Percentile95.Record(TimeSpan.FromTicks(stats.Percentile95th).TotalMilliseconds, url, requestCount, clientCount);
        Throughput.Record(TimeSpan.FromTicks((int)stats.Throughput).TotalMilliseconds, url, requestCount, clientCount);
        RequestSec.Record(TimeSpan.FromTicks((int)stats.RequestSec).TotalMilliseconds, url, requestCount, clientCount);

        return stats;
    }

    private static double CalcSquaredStdDev(long[] durations, double mean)
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

    public static (double BucketCount, double BucketSize) GetHistogramBuckets(Stats stats)
    {
        var error = stats.Error == 0 ? 1 : stats.Error;
        double bucketCount = Math.Max(Math.Min(10, (stats.Max - stats.Min) / error), 5);
        var bucketSize = (stats.Max - stats.Min) / bucketCount;
        return (bucketCount, bucketSize);
    }

    public static (double DisplayValue, string Qualifier) Display(double value)
    {
        double displayAverage;
        string qualifier;
        double absValue = Math.Abs(value);
        if (absValue >= TimeSpan.TicksPerMinute)
        {
            displayAverage = value / TimeSpan.TicksPerMinute;
            qualifier = "m ";
        }
        else if (absValue >= TimeSpan.TicksPerSecond)
        {
            displayAverage = value / TimeSpan.TicksPerSecond;
            qualifier = "s ";
        }
        else if (absValue >= TimeSpan.TicksPerMillisecond)
        {
            displayAverage = value / TimeSpan.TicksPerMillisecond;
            qualifier = "ms";
        }
        else if (absValue >= TimeSpan.TicksPerMicrosecond)
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
