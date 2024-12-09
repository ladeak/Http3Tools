using System.Net;
using CHttp.Abstractions;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;

namespace CHttp.API;

public class MeasurementsSession
{
    private readonly IConsole _console;
    private readonly IFileSystem _fileSystem;
    private readonly string _url;
    private List<Summary> _summaries = new(100);

    public MeasurementsSession(string url) : this(url, null, null)
    {
    }

    internal MeasurementsSession(string url, IConsole? console, IFileSystem? fileSystem)
    {
        _console = console ?? new CHttpConsole();
        _fileSystem = fileSystem ?? new FileSystem();
        _url = url;
    }

    public Summary StartMeasurement()
    {
        if (_summaries.Count > 0 && _summaries.Last().EndTime == default)
            throw new InvalidOperationException("Current Summary is not completed");
        var summary = new Summary(_url);
        _summaries.Add(summary);
        return summary;
    }

    public void EndMeasurement(Summary summary, HttpStatusCode statusCode = HttpStatusCode.OK) => summary.RequestCompleted(statusCode);

    public ValueTask PrintStatsAsync()
    {
        var printer = new StatisticsPrinter(_console);
        var results = new PerformanceMeasurementResults()
        {
            Summaries = _summaries,
            TotalBytesRead = 0,
            MaxConnections = 1,
            Behavior = new PerformanceBehavior(_summaries.Count, 1, false)
        };
        return printer.SummarizeResultsAsync(results);
    }

    public ValueTask SaveAsync(string filePath)
    {
        var printer = new FilePrinter(filePath, _fileSystem);
        var results = new PerformanceMeasurementResults()
        {
            Summaries = _summaries,
            TotalBytesRead = 0,
            MaxConnections = 1,
            Behavior = new PerformanceBehavior(1, _summaries.Count, false)
        };
        return printer.SummarizeResultsAsync(results);
    }

    public async ValueTask DiffAsync(string filePath0, string filePath1)
    {
        var printer = new DiffPrinter(_console);
        var session0 = await PerformanceFileHandler.LoadAsync(_fileSystem, filePath0);
        var session1 = await PerformanceFileHandler.LoadAsync(_fileSystem, filePath1);
        printer.Compare(session0, session1);
    }
}