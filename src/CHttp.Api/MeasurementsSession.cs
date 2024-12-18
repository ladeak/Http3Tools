using System.Net;
using System.Runtime.InteropServices;
using CHttp.Abstractions;
using CHttp.Performance.Data;
using CHttp.Performance.Statitics;

namespace CHttp;

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

    public void StartMeasurement()
    {
        if (_summaries.Count > 0 && _summaries.Last().EndTime == default)
            throw new InvalidOperationException("Current Summary is not completed");
        var summary = new Summary(_url);
        _summaries.Add(summary);
    }

    public void EndMeasurement(HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        ref var summary = ref CollectionsMarshal.AsSpan(_summaries)[^1];
        summary.RequestCompleted(statusCode);
    }

    public IReadOnlyCollection<Summary> GetSession() => _summaries.ToList();

    public ValueTask PrintStatsAsync()
    {
        var printer = new StatisticsPrinter(_console);
        return printer.SummarizeResultsAsync(CreateMeasurementResults(_summaries));
    }

    public ValueTask SaveAsync(string filePath)
    {
        var printer = new FilePrinter(filePath, _fileSystem);
        return printer.SummarizeResultsAsync(CreateMeasurementResults(_summaries));
    }

    public async ValueTask DiffAsync(string filePath0, string filePath1)
    {
        var printer = new DiffPrinter(_console);
        var session0 = await PerformanceFileHandler.LoadAsync(_fileSystem, filePath0);
        var session1 = await PerformanceFileHandler.LoadAsync(_fileSystem, filePath1);
        printer.Compare(session0, session1);
    }

    public void Diff(IReadOnlyCollection<Summary> session0, IReadOnlyCollection<Summary> session1)
    {
        var printer = new DiffPrinter(_console);
        printer.Compare(CreateMeasurementResults(session0), CreateMeasurementResults(session1));
    }

    public void Diff(IReadOnlyCollection<Summary> other)
    {
        var printer = new DiffPrinter(_console);
        printer.Compare(CreateMeasurementResults(GetSession()), CreateMeasurementResults(other));
    }

    private static PerformanceMeasurementResults CreateMeasurementResults(IReadOnlyCollection<Summary> summaries) =>
        new PerformanceMeasurementResults()
        {
            Summaries = summaries,
            TotalBytesRead = 0,
            MaxConnections = 1,
            Behavior = new PerformanceBehavior(summaries.Count, 1, false)
        };

}