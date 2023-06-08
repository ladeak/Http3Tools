using CHttp.Statitics;
using NSubstitute;

namespace CHttp.Tests.Statistics;

public class CompositePrinterTests
{
    public async Task CompositePrinter_Invokes_BothDepepndencies()
    {
        var printer0 = Substitute.For<ISummaryPrinter>();
        var printer1 = Substitute.For<ISummaryPrinter>();
        var sut = new CompositePrinter(printer0, printer1);
        var summaries = new[] { new Summary() };
        await sut.SummarizeResultsAsync(summaries,1);
        await printer0.Received().SummarizeResultsAsync(summaries, 1);
        await printer1.Received().SummarizeResultsAsync(summaries, 1);
    }
}
