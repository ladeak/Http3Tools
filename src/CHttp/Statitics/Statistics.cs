using System.Numerics;

namespace CHttp.Statitics;

internal static class Statistics
{
    public record class Stats(double Mean, double StdDev, double Error, double RequestSec, double Throughput, long Min, long Max, long Median, long[] Durations, int[] StatusCodes)
    {
        public static Stats SumHistogram(Stats a, Stats b)
        {
            return new Stats(0, 0, Math.Min(a.Error, b.Error), 0, 0, Math.Min(a.Min, b.Min), Math.Max(a.Max, b.Max), 0, Array.Empty<long>(), Array.Empty<int>());
        }
    }

    public static Stats GetStats(IReadOnlyCollection<Summary> summaries, long bytesRead)
    {
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
        var min = durations[0];
        var max = durations[^1];
        var median = durations[summaries.Count / 2];
        return new Stats(mean, stdDev, error, requestSec, throughput, min, max, median, durations, statusCodes);
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
