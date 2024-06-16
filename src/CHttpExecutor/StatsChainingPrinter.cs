using CHttp.Performance.Data;
using CHttp.Performance.Statitics;

namespace CHttpExecutor;

internal class StatsChainingPrinter(params IEnumerable<IStatsHandler> handlers) : ISummaryPrinter
{
    private readonly IEnumerable<IStatsHandler> _handlers = handlers;

    public ValueTask SummarizeResultsAsync(PerformanceMeasurementResults session)
    {
        var summaries = session.Summaries;
        if (summaries.Count == 0)
            throw new InvalidOperationException("No measurements available");
        var stats = StatisticsCalculator.GetStats(session);

        foreach (var handler in _handlers)
            handler.HandleStats(session, stats);

        return ValueTask.CompletedTask;
    }
}
