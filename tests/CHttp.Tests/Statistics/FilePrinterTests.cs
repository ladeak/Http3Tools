using System.Text.Json;
using CHttp.Statitics;

namespace CHttp.Tests.Statistics;

public class FilePrinterTests
{
    [Fact]
    public async Task SummarizeResultsAsync_Writes_File()
    {
        var fileSystem = new TestFileSystem();
        var sut = new FilePrinter("somefile", fileSystem);
        var summary = new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.Timeout };
        summary.Length = 100;

        await sut.SummarizeResultsAsync(new[] { summary }, 100);

        var file = fileSystem.GetFile("somefile");
        var results = JsonSerializer.Deserialize<PerformanceMeasurementResults>(file)!;
        Assert.Equal(100, results.TotalBytesRead);
        var resultSummary = results.Summaries.First();
        Assert.Equal(summary, resultSummary);
    }
}
