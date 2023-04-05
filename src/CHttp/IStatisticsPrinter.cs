namespace CHttp;

internal interface IStatisticsPrinter
{
    void SummarizeResults(IEnumerable<Summary> summaries, long bytesRead);
}