﻿using System.Diagnostics.Metrics;

namespace CHttp.Tests.Statistics;

public class StatisticsTests
{
    [Fact]
    public void BaseicStatistics()
    {
        var summaries = new[] {
            new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
            new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
            new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(3)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
        };

        var result = CHttp.Statitics.Statistics.GetStats(summaries, 100);

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
    [InlineData("Req/Sec", 0.0001)]
    [InlineData("Throughput", 0.005)]
    public async void TestMetrics(string instrumentName, double expected)
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

        var summaries = new[] {
            new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
            new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
            new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(3)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 },
        };

        CHttp.Statitics.Statistics.GetStats(summaries, 100);
        var result = await tcs.Task;
        Assert.Equal(expected, result, 4);
    }
}