using CHttp.Data;

namespace CHttp.Performance.Data;

internal class PerformanceMeasurementResults
{
    public static PerformanceMeasurementResults Default { get; } = new PerformanceMeasurementResults() { Summaries = [], TotalBytesRead = 0, MaxConnections = 0, Behavior = new(0, 0, false) };

    public required IReadOnlyCollection<Summary> Summaries { get; init; }

    public required long TotalBytesRead { get; init; }

    public required long MaxConnections { get; init; }

    public required PerformanceBehavior Behavior { get; init; }
}
