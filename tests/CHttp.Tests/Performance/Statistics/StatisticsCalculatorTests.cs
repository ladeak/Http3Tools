using System.Diagnostics.Metrics;
using CHttp.Data;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;

namespace CHttp.Tests.Performance.Statistics;

public class StatisticsCalculatorTests
{
    [Fact]
    public void BaseicStatistics()
    {
        var summaries = new[] {
            new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
            new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
            new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(3)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
        };

        var result = StatisticsCalculator.GetStats(new PerformanceMeasurementResults() { Summaries = summaries, TotalBytesRead = 100, MaxConnections = 1, Behavior = new(3, 1, false) });

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks, result.Mean);
        Assert.Equal(8164965.8092772597, result.StdDev);
        Assert.Equal(4714045.207910317, result.Error, 0.1);
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks, result.Median);
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks, result.Min);
        Assert.Equal(TimeSpan.FromSeconds(3).Ticks, result.Max);
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks, result.Percentile95th);
        Assert.Equal(1, result.RequestSec);
        Assert.Equal(50, result.Throughput);
    }

    [Theory]
    [InlineData("Mean", 2000)]
    [InlineData("StdDev", 816.4965)]
    [InlineData("Error", 471.4045)]
    [InlineData("Median", 2000)]
    [InlineData("Min", 1000)]
    [InlineData("Max", 3000)]
    [InlineData("Percentile95", 2000)]
    [InlineData("Throughput", 0.005)]
    public async Task TestMetrics(string instrumentName, double expected)
    {
        // Retry as parallel tests may be also running in this assembly invoking GetStat that produces the metrics.
        // Not using test parallelism, as the metrics is solely tested by this unit tests, and numerous other tests
        // would not to synced. Alternatively use a bool flag to indicate the metrics to be published.
        for (int i = 0; i < 3; i++)
        {
            using var listener = new MeterListener();
            TaskCompletionSource<double> tcs = new TaskCompletionSource<double>();
            listener.SetMeasurementEventCallback((Instrument instrument, double measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
            {
                tcs.TrySetResult(measurement);
            });
            listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Name == instrumentName)
                    listener.EnableMeasurementEvents(instrument);
            };
            listener.Start();

            var summaries = new[]
            {
                new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
                new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
                new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(3)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
            };

            StatisticsCalculator.GetStats(new PerformanceMeasurementResults() { Summaries = summaries, TotalBytesRead = 100, MaxConnections = 1, Behavior = new(3, 1, false) });
            var result = await tcs.Task;
            if (Equal(expected, result, 4))
                return;
        }
        Assert.Fail();
    }

    [Fact]
    public void GetHistogramBuckets100()
    {
        (var bucketCount, var bucketSize) = StatisticsCalculator.GetHistogramBuckets(new Stats(100, 0, 0, 0, 0, 0, 99, 0, 0, [.. Enumerable.Sequence<long>(0, 99, 1)], []));
        Assert.Equal(10, bucketCount);
        Assert.Equal(9.9, bucketSize, 0.01);
    }

    [Fact]
    public void GetHistogramBuckets_WithPercentile99()
    {
        (var bucketCount, var bucketSize) = StatisticsCalculator.GetHistogramBuckets(new Stats(100, 0, 0, 0, 0, 0, 100000, 0, 0, [.. Enumerable.Sequence<long>(0, 998, 1), 100000], []));
        Assert.Equal(10, bucketCount);
        Assert.Equal(99, bucketSize);
    }

    [Fact]
    public void GetHistogramBuckets_WithPercentile98()
    {
        (var bucketCount, var bucketSize) = StatisticsCalculator.GetHistogramBuckets(new Stats(100, 0, 0, 0, 0, 0, 100000, 0, 0, [.. Enumerable.Sequence<long>(0, 990, 1), 100000, 100000, 100000, 100000, 100000, 100000, 100000, 100000, 100000, 100000], []));
        Assert.Equal(10, bucketCount);
        Assert.Equal(10000, bucketSize);
    }

    public static bool Equal(double expected, double actual, int precision)
    {
        var expectedRounded = Math.Round(expected, precision);
        var actualRounded = Math.Round(actual, precision);
        return Equals(expectedRounded, actualRounded);
    }

    [Fact]
    public void CalculateBayesianProbability_NoVariance()
    {
        var (session0, session1) = GetSessions(100, 100, 0, 0);
        var result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0.45, result.Value, 2));

        (session0, session1) = GetSessions(101, 100, 0, 0);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.Equal(0, result.Value);

        (session0, session1) = GetSessions(100, 101, 0, 0);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void CalculateBayesianProbability_WithVariance()
    {
        var (session0, session1) = GetSessions(100, 100, 10, 10);
        var result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0.5, result.Value, 1));

        (session0, session1) = GetSessions(101, 100, 10, 10);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.Equal(0, result.Value);

        (session0, session1) = GetSessions(100, 101, 10, 10);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void CalculateBayesianProbability_WithLargeVariance()
    {
        var (session0, session1) = GetSessions(100, 100, 50, 50);
        var result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0.5, result.Value, 1));

        (session0, session1) = GetSessions(100, 100, 90, 90);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0.5, result.Value, 1));

        (session0, session1) = GetSessions(200, 200, 200, 200);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0.5, result.Value, 1));
    }

    [Fact]
    public void CalculateBayesianProbability_WithDiffVariance()
    {
        var (session0, session1) = GetSessions(100, 100, 0, 10);
        var result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0, result.Value, 3));

        (session0, session1) = GetSessions(100, 100, 10, 00);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(1, result.Value, 3));

        (session0, session1) = GetSessions(100, 100, 10, 8);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0.558, result.Value, 3));

        (session0, session1) = GetSessions(100, 100, 10, 50);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0.016, result.Value, 3));

        (session0, session1) = GetSessions(6, 6, 4, 2);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0.993, result.Value, 3));

        (session0, session1) = GetSessions(6, 6, 10, 2);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(1, result.Value, 3));
    }

    [Fact]
    public void CalculateBayesianProbability_OverlappingVariance()
    {
        var (session0, session1) = GetSessions(99, 100, 20, 10);
        var result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(1, result.Value, 3));

        (session0, session1) = GetSessions(100, 99, 10, 20);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0, result.Value, 3));

        (session0, session1) = GetSessions(100, 99, 20, 5);
        result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0.032, result.Value, 3));
    }

    [Fact]
    public void CalculateBayesianProbability_LargeValuesWithLargeVariance()
    {
        var (session0, session1) = GetSessions(1500, 1490, 900, 600);
        var result = StatisticsCalculator.CalculateBayesianProbability(session0, session1, new Random(0));
        Assert.NotNull(result);
        Assert.True(Equal(0.77, result.Value, 2));
    }

    private (PerformanceMeasurementResults, PerformanceMeasurementResults) GetSessions(int duration0, int duration1, int variance0, int variance1)
    {
        List<Summary> summaries0 = new List<Summary>();
        List<Summary> summaries1 = new List<Summary>();
        int halfVariance0 = variance0 / 2;
        int halfVariance1 = variance1 / 2;
        for (int i = 0; i < 1000; i++)
        {
            int totalDuration = variance0 == 0 ? duration0 : duration0 + (i % variance0) - halfVariance0;
            summaries0.Add(new Summary("url", new DateTime(2026, 06, 01, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMilliseconds(totalDuration)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 });
        }

        for (int i = 0; i < 1000; i++)
        {
            int totalDuration = variance1 == 0 ? duration1 : duration1 + (i % variance1) - halfVariance1;
            summaries1.Add(new Summary("url", new DateTime(2026, 06, 01, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMilliseconds(totalDuration)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 });
        }

        var session0 = new PerformanceMeasurementResults()
        {
            TotalBytesRead = 100,
            MaxConnections = 1,
            Summaries = summaries0,
            Behavior = new(1, 1, false)
        };
        var session1 = new PerformanceMeasurementResults()
        {
            TotalBytesRead = 100,
            MaxConnections = 1,
            Summaries = summaries1,
            Behavior = new(1, 1, false)
        };
        return (session0, session1);
    }

}
