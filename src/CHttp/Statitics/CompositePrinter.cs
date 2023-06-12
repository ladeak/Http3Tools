namespace CHttp.Statitics;

internal class CompositePrinter : ISummaryPrinter
{
    private readonly ISummaryPrinter _printer0;
    private readonly ISummaryPrinter _printer1;

    public CompositePrinter(ISummaryPrinter printer0, ISummaryPrinter printer1)
    {
        _printer0 = printer0 ?? throw new ArgumentNullException(nameof(printer0));
        _printer1 = printer1 ?? throw new ArgumentNullException(nameof(printer1));
    }

    public async ValueTask SummarizeResultsAsync(PerformanceMeasurementResults session)
    {
        await _printer0.SummarizeResultsAsync(session);
        await _printer1.SummarizeResultsAsync(session);
    }
}