using CHttp.Data;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;

namespace CHttpExecutor;

internal class StatsAssertionHandler(FrozenExecutionStep step) : IStatsHandler
{
    private Stats? _stats;

    public ValueTask HandleStats(PerformanceMeasurementResults session, Stats stats)
    {
        var summaries = session.Summaries;
        if (summaries.Count == 0)
            throw new InvalidOperationException("No measurements available");
        _stats = StatisticsCalculator.GetStats(session);
        return ValueTask.CompletedTask;
    }

    internal IReadOnlyCollection<string> GetViolations()
    {
        if (_stats == null)
            return [];
        List<string> violations = [];
        foreach (var assertion in step.Assertions)
            if (!assertion.Assert(_stats, out var error))
                violations.Add(error);
        return violations;
    }
}