namespace CHttp.Statitics;

internal interface ISummaryPrinter
{
    ValueTask SummarizeResultsAsync(PerformanceMeasurementResults session);
}
