namespace CHttp.Data;

public record class Stats(double Mean, double StdDev, double Error, double RequestSec, double Throughput, long Min, long Max, long Median, long Percentile95th, long[] Durations, int[] StatusCodes)
{
    internal static Stats SumHistogram(Stats a, Stats b)
    {
        return new Stats(0, 0, double.Min(a.Error, b.Error), 0, 0, long.Min(a.Min, b.Min), long.Max(a.Max, b.Max), 0, 0, Array.Empty<long>(), Array.Empty<int>());
    }
}
