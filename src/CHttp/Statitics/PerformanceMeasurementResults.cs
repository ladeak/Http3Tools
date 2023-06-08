namespace CHttp.Statitics;

public class PerformanceMeasurementResults
{
#if NET7_0
    public IReadOnlyCollection<Summary> Summaries { get; set; } = new List<Summary>();

    public long TotalBytesRead { get; set; }
#endif
#if NET8_0
    public required IReadOnlyCollection<Summary> Summaries { get; init; }

    public required long TotalBytesRead { get; init; }
#endif

}
