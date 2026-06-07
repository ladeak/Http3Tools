using CHttp.Performance.Statitics;

namespace CHttp.Tests.Performance.Statistics;

public class DiffPrinterTests
{
    [Fact]
    public void PrintBayesian_Higher()
    {
        var console = new TestConsoleAsOuput();
        var sut = new DiffPrinter(console);
        var (session0, session1) = StatisticsCalculatorTests.GetSessions(100, 90, 0, 0);
        sut.Compare(session0, session1);
        Assert.Contains("probability, the base session's true mean latency is higher than compared session's", console.Text);
    }

    [Fact]
    public void PrintBayesian_Lower()
    {
        var console = new TestConsoleAsOuput();
        var sut = new DiffPrinter(console);
        var (session0, session1) = StatisticsCalculatorTests.GetSessions(90, 100, 0, 0);
        sut.Compare(session0, session1);
        Assert.Contains("probability, the base session's true mean latency is lower than compared session's", console.Text);
    }
}
