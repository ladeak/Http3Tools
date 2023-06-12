using CHttp.Abstractions;

namespace CHttp.Statitics;

internal class FilePrinter : ISummaryPrinter
{
    private readonly string _filePath;
    private readonly IFileSystem _fileSystem;

    public FilePrinter(string filePath, IFileSystem fileSystem)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public ValueTask SummarizeResultsAsync(PerformanceMeasurementResults session) =>
        PerformanceFileHandler.SaveAsync(_fileSystem, _filePath, session);
}
