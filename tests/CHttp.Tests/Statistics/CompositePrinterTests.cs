using CHttp.Statitics;
using NSubstitute;

namespace CHttp.Tests.Statistics;

public class CompositePrinterTests
{
    public async Task CompositePrinter_Invokes_BothDependencies()
    {
        var printer0 = Substitute.For<ISummaryPrinter>();
        var printer1 = Substitute.For<ISummaryPrinter>();
        var sut = new CompositePrinter(printer0, printer1);
        var session = new PerformanceMeasurementResults() { Summaries = new[] { new Summary() }, TotalBytesRead = 1, Behavior = new(1, 1, false) };
        await sut.SummarizeResultsAsync(session);
        await printer0.Received().SummarizeResultsAsync(session);
        await printer1.Received().SummarizeResultsAsync(session);
    }
}
