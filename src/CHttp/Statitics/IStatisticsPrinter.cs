namespace CHttp.Statitics;

internal interface ISummaryPrinter
{
    ValueTask SummarizeResultsAsync(IReadOnlyCollection<Summary> summaries, long bytesRead);
}
