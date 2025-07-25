﻿using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;

namespace CHttp.Tests.Performance;

public class CHttpDiffFunctionalTests
{
    [Fact]
    public async Task DisplayingSingleFile()
    {
        var console = new TestConsoleAsOuput();
        var fileSystem = new MemoryFileSystem();
        PerformanceMeasurementResults session = new()
        {
            TotalBytesRead = 100,
            MaxConnections = 1,
            Summaries = new[] { new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 } },
            Behavior = new(1, 1, false)
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem).Parse($"diff --files session0.json")
            .InvokeAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Contains("| Mean:            1.000 s    |", console.Text);
        Assert.Contains("| StdDev:          0.000 ns   |", console.Text);
        Assert.Contains("| Error:           0.000 ns   |", console.Text);
        Assert.Contains("| Median:          1.000 s    |", console.Text);
        Assert.Contains("| Min:             1.000 s    |", console.Text);
        Assert.Contains("| Max:             1.000 s    |", console.Text);
        Assert.Contains("| 95th:            1.000 s    |", console.Text);
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
        var fileSystem = new MemoryFileSystem();

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem).Parse($"diff")
            .InvokeAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Empty(console.Text);
    }

    [Fact]
    public async Task DisplayingMultipleEqualFile()
    {
        var console = new TestConsoleAsOuput();
        var fileSystem = new MemoryFileSystem();
        PerformanceMeasurementResults session = new()
        {
            TotalBytesRead = 100,
            MaxConnections = 1,
            Summaries = new[] { new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 } },
            Behavior = new(1, 1, false)
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem)
            .Parse($"diff --files session0.json --files session0.json")
            .InvokeAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Contains("| Mean:            1.000 s             0 ns   |", console.Text);
        Assert.Contains("| StdDev:          0.000 ns            0 ns   |", console.Text);
        Assert.Contains("| Error:           0.000 ns            0 ns   |", console.Text);
        Assert.Contains("| Median:          1.000 s             0 ns   |", console.Text);
        Assert.Contains("| Min:             1.000 s             0 ns   |", console.Text);
        Assert.Contains("| Max:             1.000 s             0 ns   |", console.Text);
        Assert.Contains("| 95th:            1.000 s             0 ns   |", console.Text);
        Assert.Contains("| Throughput:    100.000  B/s          0  B/s |", console.Text);
        Assert.Contains("| Req/Sec:             1               0      |", console.Text);
        Assert.Contains("1xx: 0 +0, 2xx: 1 +0, 3xx: 0 +0, 4xx: 0 +0, 5xx: 0 +0, Other: 0 +0", console.Text);
    }

    [Fact]
    public async Task DisplayingMultipleDifferentFile()
    {
        var console = new TestConsoleAsOuput();
        var fileSystem = new MemoryFileSystem();
        PerformanceMeasurementResults session0 = new()
        {
            TotalBytesRead = 100,
            MaxConnections = 1,
            Summaries = new[] { new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 } },
            Behavior = new(1, 1, false)
        };
        PerformanceMeasurementResults session1 = new()
        {
            TotalBytesRead = 400,
            MaxConnections = 1,
            Summaries = new[] { new Summary("url", new DateTime(2023, 06, 08, 0, 0, 1, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 400 } },
            Behavior = new(1, 1, false)
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session0);
        await PerformanceFileHandler.SaveAsync(fileSystem, "session1.json", session1);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem)
            .Parse($"diff --files session0.json --files session1.json")
            .InvokeAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Contains($"RequestCount: 1, Clients: 1", console.Text);
        Assert.Contains("| Mean:            1.000 s        +1.000 s    |", console.Text);
        Assert.Contains("| StdDev:          0.000 ns            0 ns   |", console.Text);
        Assert.Contains("| Error:           0.000 ns            0 ns   |", console.Text);
        Assert.Contains("| Median:          1.000 s        +1.000 s    |", console.Text);
        Assert.Contains("| Min:             1.000 s        +1.000 s    |", console.Text);
        Assert.Contains("| Max:             1.000 s        +1.000 s    |", console.Text);
        Assert.Contains("| 95th:            1.000 s        +1.000 s    |", console.Text);
        Assert.Contains("| Throughput:    100.000  B/s   +100.000  B/s |", console.Text);
        Assert.Contains("| Req/Sec:             1            -0.5      |", console.Text);
        Assert.Contains("1xx: 0 +0, 2xx: 1 -1, 3xx: 0 +0, 4xx: 0 +1, 5xx: 0 +0, Other: 0 +0", console.Text);
    }

    [Fact]
    public async Task DisplayingDifferentEndpoints_ShowsWarnings()
    {
        var console = new TestConsoleAsOuput();
        var fileSystem = new MemoryFileSystem();
        PerformanceMeasurementResults session0 = new()
        {
            TotalBytesRead = 100,
            MaxConnections = 1,
            Summaries = new[] { new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 } },
            Behavior = new(1, 1, false)
        };
        PerformanceMeasurementResults session1 = new()
        {
            TotalBytesRead = 400,
            MaxConnections = 1,
            Summaries = new[] { new Summary("different_url", new DateTime(2023, 06, 08, 0, 0, 1, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 400 } },
            Behavior = new(1, 1, false)
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session0);
        await PerformanceFileHandler.SaveAsync(fileSystem, "session1.json", session1);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem)
            .Parse($"diff --files session0.json --files session1.json")
            .InvokeAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Contains("*Warning: session files contain different urls: url,different_url", console.Text);
    }

    [Fact]
    public async Task DisplayingDifferent_RequestCount_ShowsWarnings()
    {
        var console = new TestConsoleAsOuput();
        var fileSystem = new MemoryFileSystem();
        PerformanceMeasurementResults session0 = new()
        {
            TotalBytesRead = 100,
            MaxConnections = 1,
            Summaries = new[] { new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 } },
            Behavior = new(1, 1, false)
        };
        PerformanceMeasurementResults session1 = new()
        {
            TotalBytesRead = 400,
            MaxConnections = 1,
            Summaries = new[] {
                new Summary("url", new DateTime(2023, 06, 08, 0, 0, 1, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 400 },
                new Summary("url", new DateTime(2023, 06, 08, 0, 0, 1, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 400 }
            },
            Behavior = new(2, 1, false)
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session0);
        await PerformanceFileHandler.SaveAsync(fileSystem, "session1.json", session1);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem)
            .Parse($"diff --files .\\session0.json --files session1.json")
            .InvokeAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Contains("*Warning: session files use different test parameters: PerformanceBehavior { RequestCount = 1, ClientsCount = 1, SharedSocketsHandler = False } and PerformanceBehavior { RequestCount = 2, ClientsCount = 1, SharedSocketsHandler = False }", console.Text);
    }

    [Fact]
    public async Task DisplayingDifferent_ClientCount_ShowsWarnings()
    {
        var console = new TestConsoleAsOuput();
        var fileSystem = new MemoryFileSystem();
        PerformanceMeasurementResults session0 = new()
        {
            TotalBytesRead = 100,
            MaxConnections = 1,
            Summaries = new[] {
                new Summary("url", new DateTime(2023, 06, 08, 0, 0, 1, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 400 },
                new Summary("url", new DateTime(2023, 06, 08, 0, 0, 1, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 400 }
            },
            Behavior = new(2, 1, false)
        };
        PerformanceMeasurementResults session1 = new()
        {
            TotalBytesRead = 400,
            MaxConnections = 1,
            Summaries = new[] {
                new Summary("url", new DateTime(2023, 06, 08, 0, 0, 1, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 400 },
                new Summary("url", new DateTime(2023, 06, 08, 0, 0, 1, DateTimeKind.Utc), TimeSpan.FromSeconds(2)) { ErrorCode = ErrorType.None, HttpStatusCode = 400 }
            },
            Behavior = new(2, 2, false)
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session0);
        await PerformanceFileHandler.SaveAsync(fileSystem, "session1.json", session1);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem)
            .Parse("diff --files session0.json --files session1.json")
            .InvokeAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Contains($"RequestCount: 2, Clients: 1", console.Text);
        Assert.Contains("*Warning: session files use different test parameters: PerformanceBehavior { RequestCount = 2, ClientsCount = 1, SharedSocketsHandler = False } and PerformanceBehavior { RequestCount = 2, ClientsCount = 2, SharedSocketsHandler = False }", console.Text);
    }

    [Fact]
    public async Task DisplayingMultipleHistogram()
    {
        var console = new TestConsoleAsOuput(windowWidth: 40);
        var fileSystem = new MemoryFileSystem();
        var summaries0 = new List<Summary>();
        for (int i = 0; i < 100; i++)
            summaries0.Add(new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMilliseconds(i)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 });
        PerformanceMeasurementResults session0 = new()
        {
            TotalBytesRead = 100,
            MaxConnections = 1,
            Summaries = summaries0,
            Behavior = new(100, 10, false)
        };
        var summaries1 = new List<Summary>();
        for (int i = 0; i < 100; i++)
            summaries1.Add(new Summary("url", new DateTime(2023, 06, 08, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMilliseconds(50)) { ErrorCode = ErrorType.None, HttpStatusCode = 200 });
        PerformanceMeasurementResults session1 = new()
        {
            TotalBytesRead = 400,
            MaxConnections = 1,
            Summaries = summaries1,
            Behavior = new(100, 10, false)
        };

        await PerformanceFileHandler.SaveAsync(fileSystem, "session0.json", session0);
        await PerformanceFileHandler.SaveAsync(fileSystem, "session1.json", session1);

        var client = await CommandFactory.CreateRootCommand(console: console, fileSystem: fileSystem)
            .Parse($"diff --files session0.json --files session1.json")
            .InvokeAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

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
