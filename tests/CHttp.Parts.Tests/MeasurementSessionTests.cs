using System.Net;
using System.Text.Json;
using CHttp.Abstractions;
using CHttp.Performance.Data;
using CHttp.Tests;
using Xunit;

namespace CHttp.Parts.Tests;

public class MeasurementSessionTests
{
    [Fact]
    public void CanCreate()
    {
        new MeasurementsSession("uri", new NoOpConsole(), new MemoryFileSystem());
        new MeasurementsSession("uri");
    }

    [Fact]
    public async Task AddSummary()
    {
        var fileSystem = new MemoryFileSystem();
        var testConsole = new TestConsoleAsOuput();
        var sut = new MeasurementsSession("uri", testConsole, fileSystem);
        sut.StartMeasurement();
        sut.EndMeasurement();
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
    public async Task AddSummaries_SaveAsync()
    {
        var fileSystem = new MemoryFileSystem();
        var testConsole = new TestConsoleAsOuput();
        var sut = new MeasurementsSession("uri", testConsole, fileSystem);

        for (int i = 0; i < 10; i++)
        {
            sut.StartMeasurement();
            sut.EndMeasurement();
        }
        await sut.SaveAsync("testfile");

        var data = fileSystem.GetFile("testfile");
        var result = JsonSerializer.Deserialize<PerformanceMeasurementResults>(data);
        Assert.NotNull(result);
        Assert.Equal(10, result.Summaries.Count);
        Assert.Equal(1, result.MaxConnections);
        Assert.Equal(0, result.TotalBytesRead);
        Assert.Equal(10, result.Behavior.RequestCount);
        Assert.Equal(1, result.Behavior.ClientsCount);
    }

    [Fact]
    public async Task AddSummaries_PrintAsync()
    {
        var fileSystem = new MemoryFileSystem();
        var testConsole = new TestConsoleAsOuput();
        var sut = new MeasurementsSession("uri", testConsole, fileSystem);

        for (int i = 0; i < 10; i++)
        {
            sut.StartMeasurement();
            sut.EndMeasurement(HttpStatusCode.BadRequest);
        }
        await sut.PrintStatsAsync();
        Assert.NotNull(testConsole.Text);
        Assert.Contains("RequestCount: 10, Clients: 1, Connections: 1", testConsole.Text);
        Assert.Contains("1xx: 0, 2xx: 0, 3xx: 0, 4xx: 10, 5xx: 0, Other: 0", testConsole.Text);
        Assert.Contains("| Mean:", testConsole.Text);
    }

    [Fact]
    public void GetSession()
    {
        var fileSystem = new MemoryFileSystem();
        var testConsole = new TestConsoleAsOuput();
        var sut = new MeasurementsSession("uri", testConsole, fileSystem);

        for (int i = 0; i < 10; i++)
        {
            sut.StartMeasurement();
            sut.EndMeasurement();
        }
        var session = sut.GetSession();
        Assert.Equal(10, session.Count);
        Assert.True(session.All(x => x.HttpStatusCode is not null));
    }

    [Fact]
    public async Task DiffAsync()
    {
        var fileSystem = new MemoryFileSystem();
        var testConsole = new TestConsoleAsOuput();
        var sut = new MeasurementsSession("uri", testConsole, fileSystem);

        for (int i = 0; i < 10; i++)
        {
            sut.StartMeasurement();
            sut.EndMeasurement();
        }
        await sut.SaveAsync("s0");
        await sut.SaveAsync("s1");

        await sut.DiffAsync("s0", "s1");
        Assert.NotNull(testConsole.Text);
        Assert.Contains("RequestCount: 10, Clients: 1", testConsole.Text);
        Assert.Contains("1xx: 0 +0, 2xx: 10 +0, 3xx: 0 +0, 4xx: 0 +0, 5xx: 0 +0, Other: 0 +0", testConsole.Text);
        Assert.Contains("| Mean:", testConsole.Text);
    }

    [Fact]
    public void Diff()
    {
        var fileSystem = new MemoryFileSystem();
        var testConsole = new TestConsoleAsOuput();
        var sut = new MeasurementsSession("uri", testConsole, fileSystem);

        for (int i = 0; i < 10; i++)
        {
            sut.StartMeasurement();
            sut.EndMeasurement();
        }
        var s0 = sut.GetSession();
        var s1 = sut.GetSession();
        Assert.NotSame(s0, s1);

        sut.Diff(s1, s1);
        Assert.NotNull(testConsole.Text);
        Assert.Contains("RequestCount: 10, Clients: 1", testConsole.Text);
        Assert.Contains("1xx: 0 +0, 2xx: 10 +0, 3xx: 0 +0, 4xx: 0 +0, 5xx: 0 +0, Other: 0 +0", testConsole.Text);
        Assert.Contains("| Mean:", testConsole.Text);
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
