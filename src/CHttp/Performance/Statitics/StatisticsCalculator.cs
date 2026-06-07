using System.Diagnostics;
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
        double stdDev = Math.Sqrt(CalculateVariance(durations, mean));
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

    private static double CalculateVariance(long[] durations, double mean)
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
                input = input[vSize..];
            }
        }

        // Remaining
        while (input.Length > 0)
        {
            var difference = mean - input[0];
            sum += difference * difference;
            input = input[1..];
        }

        return sum / durations.Length;
    }

    private static double CalculateVariance(double[] durations, double mean)
    {
        var avg = new Vector<double>(mean);
        var input = durations.AsSpan();
        double sum = 0;
        var vSize = Vector<double>.Count;
        while (input.Length >= vSize)
        {
            var vInput = new Vector<double>(input);
            var difference = Vector.Subtract(avg, vInput);
            var squared = Vector.Multiply(difference, difference);
            sum += Vector.Sum(squared);
            input = input[vSize..];
        }

        // Remaining
        for (int i = 0; i < input.Length; i++)
        {
            var difference = mean - input[i];
            sum += difference * difference;
        }

        return sum / durations.Length;
    }

    public static (double BucketCount, double BucketSize) GetHistogramBuckets(Stats stats)
    {
        var error = stats.Error == 0 ? 1 : stats.Error;
        long max = stats.Max;
        long min = stats.Min;
        if (stats.Durations.Length > 999)
        {
            var firstPercentile = stats.Durations.Length / 100; // p99
            max = stats.Durations[^firstPercentile];
        }
        double bucketCount = Math.Max(Math.Min(10, (max - min) / error), 5);
        var bucketSize = (max - min) / bucketCount;
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

    internal static double? CalculateBayesianProbability(
        PerformanceMeasurementResults session0,
        PerformanceMeasurementResults session1,
        Random? random = null)
    {
        if (session0.Summaries.Count < 100 || session0.Summaries.Count != session1.Summaries.Count)
            return null;

        // Step1: Calculate Mean and Variance based logarithmic durations
        var (s0Mean, s0Variance) = LogMeanVariance(session0.Summaries);
        var (s1Mean, s1Variance) = LogMeanVariance(session1.Summaries);

        // The above values are not the "true" values, as these are based on the approximation of the samples.
        // To calculate the "true" values use a Posterior Distributions (one for each session), in this case
        // Student's t distribution ref: https://en.wikipedia.org/wiki/Student%27s_t-distribution
        // and sample values of the this distribution. Compare the samples to derive the probability of one
        // session being faster to the other session.

        // For Student t distribution: number of requests, mean and variance needed.
        random = random ?? Random.Shared;
        var student0 = new StudentDistribution(session0.Summaries.Count - 1, s0Mean, Math.Sqrt(s0Variance / session0.Summaries.Count), random);
        var student1 = new StudentDistribution(session1.Summaries.Count - 1, s1Mean, Math.Sqrt(s1Variance / session1.Summaries.Count), random);

        // Simulate and compare
        long session0Faster = 0;
        double simulationCount = 8192;
        for (int i = 0; i < simulationCount; i++)
        {
            if (student0.Sample() < student1.Sample())
                session0Faster++;
        }
        return session0Faster / simulationCount;

        static (double Mean, double Variance) LogMeanVariance(IReadOnlyCollection<Summary> summaries)
        {
            var durations = new double[summaries.Count];
            double totalLogTicks = 0;

            int current = 0;
            foreach (var item in summaries)
            {
                var logTicks = Math.Log(item.Duration.Ticks);
                durations[current++] = logTicks;
                totalLogTicks += logTicks;
            }
            var mean = totalLogTicks / summaries.Count;
            var variance = CalculateVariance(durations, mean);
            return (mean, variance);
        }
    }
}

internal class StudentDistribution
{
    private readonly double _mean;
    private readonly double _scale;
    private readonly int _degreeOfFreedom;
    private readonly StandardNormalDistribution _standardNormal;
    private readonly ChiSquaredDistribution _chiSquared;

    public StudentDistribution(int degreeOfFreedom, double mean, double scale, Random random)
    {
        _mean = mean;
        _scale = scale;
        _degreeOfFreedom = degreeOfFreedom;
        _standardNormal = new(random);
        _chiSquared = new(degreeOfFreedom, _standardNormal, random);
    }

    public double Sample()
    {
        // Student's t-distribution with v (degreeOfFreedom) degree of freedom can be defined as the distribution
        // T = Z / Sqrt(V/degreeOfFreedom). 
        // Z is a standard normal with expected value 0 and variance 1
        // V is a chi-squared distribution
        // degreeOfFreedom is the degree of freedom (sample count - 1)
        var z = _standardNormal.Sample();
        var v = _chiSquared.Sample();

        var tStandard = z / Math.Sqrt(v / _degreeOfFreedom);

        // Apply 'location' (mean) and 'scale' (variance).
        return _mean + tStandard * _scale;
    }
}

internal class StandardNormalDistribution(Random random)
{
    private readonly Random _random = random;
    public double Sample()
    {
        // Ref: https://en.wikipedia.org/wiki/Normal_distribution
        // The Box–Muller method uses two independent random numbers u1 and u2 distributed uniformly on (0,1).
        // Then the two random variables X and Y
        double u1 = 1.0 - _random.NextDouble();
        double u2 = 1.0 - _random.NextDouble();
        var sample = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return sample;
    }
}

internal class ChiSquaredDistribution(
    int degreeOfFreedom,
    StandardNormalDistribution standardNormal,
    Random random)
{
    private readonly double _degreeOfFreedom = degreeOfFreedom;
    private readonly Random _random = random;
    public readonly StandardNormalDistribution _standardNormal = standardNormal;

    public double Sample()
    {
        // Ref: https://en.wikipedia.org/wiki/Chi-squared_distribution
        // Using the Gamma distribution method. x ~ Gamma(alpha = k/2, omega = 2)
        // k in the above is (the degree of freedom, degreeOfFreedom variable).
        return SampleGamma(_degreeOfFreedom / 2.0, 2);
    }

    private double SampleGamma(double alpha, double omega)
    {
        if (alpha < 1)
            throw new InvalidOperationException("Unsupported Student-t distribution calculation");
        // Ref: https://en.wikipedia.org/wiki/Gamma_distribution
        // Marsaglia & Tsang GS algorithm, because this is faster
        // to generating a sum of squared normal samples.
        var d = alpha - 1 / 3.0;
        var c = 1 / Math.Sqrt(9 * d);

        while (true)
        {
            var z = _standardNormal.Sample();
            var v = Math.Pow((1 + c * z), 3);
            if (v <= 0)
                continue;
            var u = _random.NextDouble(); // Uniform distribution;

            if (Math.Log(u) < 0.5 * z * z + d - d * v + d * Math.Log(v))
                return d * v * omega;
        }
    }
}