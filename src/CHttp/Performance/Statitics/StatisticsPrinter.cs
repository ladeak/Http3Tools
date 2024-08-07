﻿using System.Numerics;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Performance.Data;

namespace CHttp.Performance.Statitics;

internal class StatisticsPrinter : ISummaryPrinter, IStatsHandler
{
    private readonly IConsole _console;

    internal StatisticsPrinter(IConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public ValueTask SummarizeResultsAsync(PerformanceMeasurementResults session)
    {
        var summaries = session.Summaries;
        if (summaries.Count == 0)
        {
            _console.WriteLine("No measurements available");
            return ValueTask.CompletedTask;
        }
        var stats = StatisticsCalculator.GetStats(session);
        return HandleStats(session, stats);
    }

    public ValueTask HandleStats(PerformanceMeasurementResults session, Stats stats)
    {
        (var displayMean, var meanQualifier) = StatisticsCalculator.Display(stats.Mean);
        (var displayStdDev, var stdDevQualifier) = StatisticsCalculator.Display(stats.StdDev);
        (var displayError, var errorQualifier) = StatisticsCalculator.Display(stats.Error);
        (var displayMinResponseTime, var minResponseTimeQualifier) = StatisticsCalculator.Display(stats.Min);
        (var displayMaxResponseTime, var maxResponseTimeQualifier) = StatisticsCalculator.Display(stats.Max);
        (var displayMedian, var medianQualifier) = StatisticsCalculator.Display(stats.Median);
        (var displayPercentile95, var displayPercentile95Qualifier) = StatisticsCalculator.Display(stats.Percentile95th);
        (var throughputFormatted, var throughputQualifier) = SizeFormatter<double>.FormatSizeWithQualifier(stats.Throughput);

        _console.WriteLine($"RequestCount: {session.Behavior.RequestCount}, Clients: {session.Behavior.ClientsCount}, Connections: {session.MaxConnections}");
        _console.WriteLine($"| Mean:       {displayMean,10:F3} {meanQualifier}   |");
        _console.WriteLine($"| StdDev:     {displayStdDev,10:F3} {stdDevQualifier}   |");
        _console.WriteLine($"| Error:      {displayError,10:F3} {errorQualifier}   |");
        _console.WriteLine($"| Median:     {displayMedian,10:F3} {medianQualifier}   |");
        _console.WriteLine($"| Min:        {displayMinResponseTime,10:F3} {minResponseTimeQualifier}   |");
        _console.WriteLine($"| Max:        {displayMaxResponseTime,10:F3} {maxResponseTimeQualifier}   |");
        _console.WriteLine($"| 95th:       {displayPercentile95,10:F3} {displayPercentile95Qualifier}   |");
        _console.WriteLine($"| Throughput: {throughputFormatted,10} {throughputQualifier}B/s |");
        _console.WriteLine($"| Req/Sec:    {stats.RequestSec,10:G3}      |");

        int lineLength = _console.WindowWidth;
        var scaleNormalize = (double)lineLength / session.Summaries.Count;
        string separator = new string('-', lineLength);
        // Histogram
        if (session.Summaries.Count >= 100)
        {
            _console.WriteLine(separator);
            PrintHistogram(stats, scaleNormalize);
        }
        _console.WriteLine(separator);
        PrintStatusCodes(stats.StatusCodes);
        _console.WriteLine(separator);
        return ValueTask.CompletedTask;
    }

    private void PrintStatusCodes(int[] statusCodes)
    {
        _console.WriteLine("HTTP status codes:");
        _console.WriteLine($"1xx: {statusCodes[0]}, 2xx: {statusCodes[1]}, 3xx: {statusCodes[2]}, 4xx: {statusCodes[3]}, 5xx: {statusCodes[4]}, Other: {statusCodes[5]}");
    }

    private void PrintHistogram(Stats stats, double scaleNormalize)
    {
        (var bucketCount, var bSize) = StatisticsCalculator.GetHistogramBuckets(stats);
        var bucketSize = new Vector<double>(bSize);

        var bucketLimit = new Vector<double>(stats.Min);
        var input = stats.Durations.AsSpan();
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

            (var limit, var limitQualifier) = StatisticsCalculator.Display(bucketLimit[0]);
            _console.Write($"{limit,10:F3} {limitQualifier} ");
            _console.Write(new string('#', (int)Math.Round(scaleNormalize * currentCounter)));
            _console.WriteLine();
        }
    }
}