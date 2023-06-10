using System.Globalization;
using CHttp.Abstractions;
using CHttp.Statitics;

namespace CHttp.Tests.Statistics;

public class StatisticsPrinterTests
{
    public StatisticsPrinterTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public async Task SingleMeasurement()
    {
        var summary = new Summary("url", new DateTime(2023, 04, 05, 21, 32, 00, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
        summary.RequestCompleted(System.Net.HttpStatusCode.OK);
        var console = new TestConsolePerWrite(59);
        var sut = new StatisticsPrinter(console);
        await sut.SummarizeResultsAsync(new List<Summary>() { summary }, 1);

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
1xx: 0, 2xx: 1, 3xx: 0, 4xx: 0, 5xx: 0, Other: 0
-----------------------------------------------------------
", console.Text);
    }

    [Fact]
    public async Task NoMeasurement()
    {
        var console = new TestConsolePerWrite();
        var sut = new StatisticsPrinter(console);
        await sut.SummarizeResultsAsync(new KnowSizeEnumerableCollection<Summary>(Enumerable.Empty<Summary>(), 0), 1);

        Assert.Equal($"No measurements available{Environment.NewLine}", console.Text);
    }

    [Fact]
    public async Task NoMeasuredTime()
    {
        var summary = new Summary("url", DateTime.MinValue.ToUniversalTime(), TimeSpan.Zero);
        summary.RequestCompleted(System.Net.HttpStatusCode.OK);
        var console = new TestConsolePerWrite(59);
        var sut = new StatisticsPrinter(console);
        await sut.SummarizeResultsAsync(new List<Summary>() { summary }, 1);

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
1xx: 0, 2xx: 1, 3xx: 0, 4xx: 0, 5xx: 0, Other: 0
-----------------------------------------------------------
", console.Text);
    }

    [Fact]
    public async Task ThreeMeasurements()
    {
        var summary0 = new Summary("url", new DateTime(2023, 04, 05, 21, 32, 00, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
        summary0.RequestCompleted(System.Net.HttpStatusCode.OK);

        var summary1 = new Summary("url", new DateTime(2023, 04, 05, 21, 32, 00, DateTimeKind.Utc), TimeSpan.FromSeconds(2));
        summary1.RequestCompleted(System.Net.HttpStatusCode.OK);

        var summary2 = new Summary("url", new DateTime(2023, 04, 05, 21, 32, 00, DateTimeKind.Utc), TimeSpan.FromSeconds(3));
        summary2.RequestCompleted(System.Net.HttpStatusCode.OK);
        var console = new TestConsolePerWrite(59);
        var sut = new StatisticsPrinter(console);
        await sut.SummarizeResultsAsync(new KnowSizeEnumerableCollection<Summary>(new[] { summary0, summary1, summary2 }, 3), 1);

        Assert.Equal(
@"| Mean:            2.000 s    |
| StdDev:        816.497 ms   |
| Error:         471.405 ms   |
| Median:          2.000 s    |
| Min:             1.000 s    |
| Max:             3.000 s    |
| Throughput:      0.500  B/s |
| Req/Sec:             1      |
-----------------------------------------------------------
HTTP status codes:
1xx: 0, 2xx: 3, 3xx: 0, 4xx: 0, 5xx: 0, Other: 0
-----------------------------------------------------------
", console.Text);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(99)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task NMeasurements(int n)
    {
        var input = new List<Summary>();
        for (int i = 0; i < n; i++)
        {
            var summary = new Summary("url", new DateTime(2023, 04, 05, 21, 32, 00, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
            summary.RequestCompleted(System.Net.HttpStatusCode.OK);
            input.Add(summary);
        }
        var console = new TestConsolePerWrite(59);
        var sut = new StatisticsPrinter(console);
        await sut.SummarizeResultsAsync(input, 1);

        Assert.Equal(
@$"| Mean:            1.000 s    |
| StdDev:          0.000 ns   |
| Error:           0.000 ns   |
| Median:          1.000 s    |
| Min:             1.000 s    |
| Max:             1.000 s    |
| Throughput:      1.000  B/s |
| Req/Sec:           {n,3}      |
-----------------------------------------------------------
HTTP status codes:
1xx: 0, 2xx: {n}, 3xx: 0, 4xx: 0, 5xx: 0, Other: 0
-----------------------------------------------------------
", console.Text);
    }

    [Fact]
    public async Task Histogram()
    {
        var input = new List<Summary>();
        for (int i = 0; i < 100; i++)
        {
            var summary = new Summary("url", new DateTime(2023, 04, 05, 21, 32, 00, DateTimeKind.Utc), TimeSpan.FromSeconds(i / 25 + 1));
            summary.RequestCompleted(System.Net.HttpStatusCode.OK);
            input.Add(summary);
        }
        var console = new TestConsoleAsOuput(200);
        var sut = new StatisticsPrinter(console);
        await sut.SummarizeResultsAsync(input, 1);

        Assert.Contains("     1.300 s  ##################################################", console.Text);
        Assert.Contains("     2.200 s  ##################################################", console.Text);
        Assert.Contains("     3.100 s  ##################################################", console.Text);
        Assert.Contains("     4.000 s  ##################################################", console.Text);
        Assert.Equal(200, console.Text.Count(x => x == '#'));
    }
}