using System.Diagnostics;
using System.Globalization;

namespace CHttp.Tests;

public class StatisticsPrinterTests
{
    public StatisticsPrinterTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void SingleMeasurement()
    {
        var summary = new Summary("url", new Activity("test")
            .SetStartTime(new DateTime(2023, 04, 05, 21, 32, 00, DateTimeKind.Utc))
            .SetEndTime(new DateTime(2023, 04, 05, 21, 32, 01, DateTimeKind.Utc)));
        summary.RequestCompleted(System.Net.HttpStatusCode.OK);
        var console = new TestConsole(59);
        var sut = new StatisticsPrinter(console);
        sut.SummarizeResults(new List<Summary>() { summary }, 1);

        Assert.Equal(
@"| Mean:            1.000 s    |
| StdDev:          0.000 ns   |
| Error:           0.000 ns   |
| Median:          1.000 s    |
| Min:             1.000 s    |
| Max:             1.000 s    |
| Throughput:      1.000  B/s |
| Req/Sec:             1      |
-----------------------------------------------------------
HTTP status codes:
1xx - 0, 2xx - 1, 3xx - 0, 4xx - 0, 5xx - 0, Other - 0
-----------------------------------------------------------
", console.Text);
    }

    [Fact]
    public void NoMeasurement()
    {
        var console = new TestConsole();
        var sut = new StatisticsPrinter(console);
        sut.SummarizeResults(new KnowSizeEnumerableCollection<Summary>(Enumerable.Empty<Summary>(), 0), 1);

        Assert.Equal($"No measurements available{Environment.NewLine}", console.Text);
    }

    [Fact]
    public void NoMeasuredTime()
    {
        var summary = new Summary("url", new Activity("test"));
        summary.RequestCompleted(System.Net.HttpStatusCode.OK);
        var console = new TestConsole(59);
        var sut = new StatisticsPrinter(console);
        sut.SummarizeResults(new List<Summary>() { summary }, 1);

        Assert.Equal(
@"| Mean:            0.000 ns   |
| StdDev:          0.000 ns   |
| Error:           0.000 ns   |
| Median:          0.000 ns   |
| Min:             0.000 ns   |
| Max:             0.000 ns   |
| Throughput:   Infinity TB/s |
| Req/Sec:      Infinity      |
-----------------------------------------------------------
HTTP status codes:
1xx - 0, 2xx - 1, 3xx - 0, 4xx - 0, 5xx - 0, Other - 0
-----------------------------------------------------------
", console.Text);
    }

    [Fact]
    public void ThreeMeasurements()
    {
        var summary0 = new Summary("url", new Activity("test")
            .SetStartTime(new DateTime(2023, 04, 05, 21, 32, 00, DateTimeKind.Utc))
            .SetEndTime(new DateTime(2023, 04, 05, 21, 32, 01, DateTimeKind.Utc)));
        summary0.RequestCompleted(System.Net.HttpStatusCode.OK);

        var summary1 = new Summary("url", new Activity("test")
            .SetStartTime(new DateTime(2023, 04, 05, 21, 32, 00, DateTimeKind.Utc))
            .SetEndTime(new DateTime(2023, 04, 05, 21, 32, 02, DateTimeKind.Utc)));
        summary1.RequestCompleted(System.Net.HttpStatusCode.OK);

        var summary2 = new Summary("url", new Activity("test")
            .SetStartTime(new DateTime(2023, 04, 05, 21, 32, 00, DateTimeKind.Utc))
            .SetEndTime(new DateTime(2023, 04, 05, 21, 32, 03, DateTimeKind.Utc)));
        summary2.RequestCompleted(System.Net.HttpStatusCode.OK);
        var console = new TestConsole(59);
        var sut = new StatisticsPrinter(console);
        sut.SummarizeResults(new List<Summary>() { summary0, summary1, summary2 }, 1);

        Assert.Equal(
@"| Mean:            2.000 s    |
| StdDev:        816.497 ms   |
| Error:         471.405 ms   |
| Median:          2.000 s    |
| Min:             1.000 s    |
| Max:             3.000 s    |
| Throughput:      0.500  B/s |
| Req/Sec:           0.5      |
-----------------------------------------------------------
HTTP status codes:
1xx - 0, 2xx - 3, 3xx - 0, 4xx - 0, 5xx - 0, Other - 0
-----------------------------------------------------------
", console.Text);
    }
}
