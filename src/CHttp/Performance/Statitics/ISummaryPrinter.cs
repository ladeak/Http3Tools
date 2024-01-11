using CHttp.Performance.Data;

namespace CHttp.Performance.Statitics;

internal interface ISummaryPrinter
{
    ValueTask SummarizeResultsAsync(PerformanceMeasurementResults session);
}
