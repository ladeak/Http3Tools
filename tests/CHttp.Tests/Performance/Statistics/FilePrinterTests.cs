using System.Text.Json;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;

namespace CHttp.Tests.Performance.Statistics;

public class FilePrinterTests
{
    [Fact]
    public async Task SummarizeResultsAsync_Writes_File()
    {
        var fileSystem = new MemoryFileSystem();
        var sut = new FilePrinter("somefile", fileSystem);
        var summary = new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.Timeout };
        summary.Length = 100;

        await sut.SummarizeResultsAsync(new PerformanceMeasurementResults() { Summaries = new[] { summary }, TotalBytesRead = 100, MaxConnections = 1, Behavior = new(1000, 10, false) });

        var file = fileSystem.GetFile("somefile");
        var results = JsonSerializer.Deserialize<PerformanceMeasurementResults>(file)!;
        Assert.Equal(100, results.TotalBytesRead);
        Assert.Equal(1, results.MaxConnections);
        Assert.Equal(1000, results.Behavior.RequestCount);
        Assert.Equal(10, results.Behavior.ClientsCount);
        var resultSummary = results.Summaries.First();
        Assert.Equal(summary, resultSummary);
    }
}
