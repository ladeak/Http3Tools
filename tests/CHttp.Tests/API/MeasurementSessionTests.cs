using System.Text.Json;
using CHttp.Abstractions;
using CHttp.API;
using CHttp.Performance.Data;

namespace CHttp.Tests.API;

public class MeasurementSessionTests
{
    [Fact]
    public void CanCreate()
    {
        var sut = new MeasurementsSession("uri", new NoOpConsole(), new MemoryFileSystem());
    }

    [Fact]
    public async Task AddSummary()
    {
        var fileSystem = new MemoryFileSystem();
        var testConsole = new TestConsoleAsOuput();
        var sut = new MeasurementsSession("uri", testConsole, fileSystem);
        var summary = sut.StartMeasurement();
        sut.EndMeasurement(summary);
        await sut.SaveAsync("testfile");

        var data = fileSystem.GetFile("testfile");
        var result = JsonSerializer.Deserialize<PerformanceMeasurementResults>(data);
        Assert.NotNull(result);
        Assert.Single(result.Summaries);
        Assert.Equal(1, result.MaxConnections);
        Assert.Equal(0, result.TotalBytesRead);
        Assert.Equal(1, result.Behavior.RequestCount);
        Assert.Equal(1, result.Behavior.ClientsCount);
    }

    [Fact]
    public void AddSummary_WhileInProgress_Throws()
    {
        var fileSystem = new MemoryFileSystem();
        var testConsole = new TestConsoleAsOuput();
        var sut = new MeasurementsSession("uri", testConsole, fileSystem);
        sut.StartMeasurement();
        Assert.Throws<InvalidOperationException>(() => sut.StartMeasurement());
    }
}
