using System.CommandLine;
using CHttp.Abstractions;
using CHttp.Statitics;

namespace CHttp.Tests;

public class CHttpDiffFunctional
{
    [Fact]
    public async Task DisplayingSingleFile()
    {
        var console = new TestConsoleAsOuput();
        var fileSystem = new TestFileSystem();
        PerformanceMeasurementResults session = new()
        {
            TotalBytesRead = 100,
            Summaries = new[] { new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 } }
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem).InvokeAsync($"diff --files session0.json")
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("| Mean:            1.000 s    |", console.Text);
        Assert.Contains("| StdDev:          0.000 ns   |", console.Text);
        Assert.Contains("| Error:           0.000 ns   |", console.Text);
        Assert.Contains("| Median:          1.000 s    |", console.Text);
        Assert.Contains("| Min:             1.000 s    |", console.Text);
        Assert.Contains("| Max:             1.000 s    |", console.Text);
        Assert.Contains("| Throughput:    100.000  B/s |", console.Text);
        Assert.Contains("| Req/Sec:             1      |", console.Text);
        Assert.Contains("1xx: 0, 2xx: 1, 3xx: 0, 4xx: 0, 5xx: 0, Other: 0", console.Text);

        /* Expect something like this in the output, but actual details depend on the test run.
        | Mean:            1.000 s    |
        | StdDev:          0.000 ns   |
        | Error:           0.000 ns   |
        | Median:          1.000 s    |
        | Min:             1.000 s    |
        | Max:             1.000 s    |
        | Throughput:    100.000  B/s |
        | Req/Sec:             1      |
        --------
        HTTP status codes:
        1xx: 0, 2xx: 1, 3xx: 0, 4xx: 0, 5xx: 0, Other: 0
        --------
        */
    }

    [Fact]
    public async Task DisplayingNoFile()
    {
        var console = new TestConsoleAsOuput();
        var fileSystem = new TestFileSystem();

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem).InvokeAsync($"diff")
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(console.Text);
    }

    [Fact]
    public async Task DisplayingMultipleEqualFile()
    {
        var console = new TestConsoleAsOuput();
        var fileSystem = new TestFileSystem();
        PerformanceMeasurementResults session = new()
        {
            TotalBytesRead = 100,
            Summaries = new[] { new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 } }
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem).InvokeAsync($"diff --files session0.json --files session0.json")
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("| Mean:            1.000 s             0 ns   |", console.Text);
        Assert.Contains("| StdDev:          0.000 ns            0 ns   |", console.Text);
        Assert.Contains("| Error:           0.000 ns            0 ns   |", console.Text);
        Assert.Contains("| Median:          1.000 s             0 ns   |", console.Text);
        Assert.Contains("| Min:             1.000 s             0 ns   |", console.Text);
        Assert.Contains("| Max:             1.000 s             0 ns   |", console.Text);
        Assert.Contains("| Throughput:    100.000  B/s          0  B/s |", console.Text);
        Assert.Contains("| Req/Sec:             1               0      |", console.Text);
        Assert.Contains("1xx: 0 +0, 2xx: 1 +0, 3xx: 0 +0, 4xx: 0 +0, 5xx: 0 +0, Other: 0 +0", console.Text);
    }

    [Fact]
    public async Task DisplayingMultipleDifferentFile()
    {
        var console = new TestConsoleAsOuput();
        var fileSystem = new TestFileSystem();
        PerformanceMeasurementResults session0 = new()
        {
            TotalBytesRead = 100,
            Summaries = new[] { new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 } }
        };
        PerformanceMeasurementResults session1 = new()
        {
            TotalBytesRead = 400,
            Summaries = new[] { new Summary("url", new DateTime(2023, 06, 08, 0, 0, 1, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 400 } }
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session0);
        await PerformanceFileHandler.SaveAsync(fileSystem, "session1.json", session1);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem).InvokeAsync($"diff --files session0.json --files session1.json")
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("| Mean:            1.000 s        +1.000 s    |", console.Text);
        Assert.Contains("| StdDev:          0.000 ns            0 ns   |", console.Text);
        Assert.Contains("| Error:           0.000 ns            0 ns   |", console.Text);
        Assert.Contains("| Median:          1.000 s        +1.000 s    |", console.Text);
        Assert.Contains("| Min:             1.000 s        +1.000 s    |", console.Text);
        Assert.Contains("| Max:             1.000 s        +1.000 s    |", console.Text);
        Assert.Contains("| Throughput:    100.000  B/s   +100.000  B/s |", console.Text);
        Assert.Contains("| Req/Sec:             1            -0.5      |", console.Text);
        Assert.Contains("1xx: 0 +0, 2xx: 1 -1, 3xx: 0 +0, 4xx: 0 +1, 5xx: 0 +0, Other: 0 +0", console.Text);
    }

    [Fact]
    public async Task DisplayingMultipleHistogram()
    {
        var console = new TestConsoleAsOuput(windowWidth: 40);
        var fileSystem = new TestFileSystem();
        var summaries0 = new List<Summary>();
        for (int i = 0; i < 100; i++)
            summaries0.Add(new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMilliseconds(i)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 });
        PerformanceMeasurementResults session0 = new()
        {
            TotalBytesRead = 100,
            Summaries = summaries0
        };
        var summaries1 = new List<Summary>();
        for (int i = 0; i < 100; i++)
            summaries1.Add(new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMilliseconds(50)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 });
        PerformanceMeasurementResults session1 = new()
        {
            TotalBytesRead = 400,
            Summaries = summaries1
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session0);
        await PerformanceFileHandler.SaveAsync(fileSystem, "session1.json", session1);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem).InvokeAsync($"diff --files session0.json --files session1.json")
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("     9.900 ms ##", console.Text);
        Assert.Contains("    19.800 ms ##", console.Text);
        Assert.Contains("    29.700 ms ##", console.Text);
        Assert.Contains("    39.600 ms ##", console.Text);
        Assert.Contains("    49.500 ms ##", console.Text);
        Assert.Contains("    59.400 ms ==++++++++++++++++++", console.Text);
        Assert.Contains("    69.300 ms ##", console.Text);
        Assert.Contains("    79.200 ms ##", console.Text);
        Assert.Contains("    89.100 ms ##", console.Text);
        Assert.Contains("    99.000 ms ##", console.Text);
    }
}
