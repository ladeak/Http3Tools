using CHttp.Performance.Data;
using CHttp.Performance.Statitics;

namespace CHttpExecutor;

internal class StatsAssertionPrinter : ISummaryPrinter
{
    public ValueTask SummarizeResultsAsync(PerformanceMeasurementResults session)
    {
        var summaries = session.Summaries;
        if (summaries.Count == 0)
            throw new InvalidOperationException("No measurements available");

        var stats = StatisticsCalculator.GetStats(session);



        return ValueTask.CompletedTask;
    }
}