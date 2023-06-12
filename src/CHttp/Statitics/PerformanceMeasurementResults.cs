namespace CHttp.Statitics;

internal class PerformanceMeasurementResults
{
    public static PerformanceMeasurementResults Default { get; } = new PerformanceMeasurementResults() { Summaries = Array.Empty<Summary>(), TotalBytesRead = 0, Behavior = new(0,0) };

#if NET7_0
    public IReadOnlyCollection<Summary> Summaries { get; set; } = new List<Summary>();

    public long TotalBytesRead { get; set; }

    public PerformanceBehavior Behavior { get; set; } = new(0, 0);
#endif
#if NET8_0
    public required IReadOnlyCollection<Summary> Summaries { get; init; }

    public required long TotalBytesRead { get; init; }

    public required PerformanceBehavior Behavior { get; init; }
#endif

}
