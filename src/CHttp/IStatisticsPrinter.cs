namespace CHttp;

internal interface IStatisticsPrinter
{
    void SummarizeResults(IReadOnlyCollection<Summary> summaries, long bytesRead);
}