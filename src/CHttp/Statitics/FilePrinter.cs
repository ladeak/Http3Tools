using System.Text.Json;
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

    public async ValueTask SummarizeResultsAsync(IReadOnlyCollection<Summary> summaries, long bytesRead)
    {
        var data = new PerformanceMeasurementResults { Summaries = summaries, TotalBytesRead = bytesRead };
        using var fileStream = _fileSystem.Open(_filePath, FileMode.Create, FileAccess.Write);
        await JsonSerializer.SerializeAsync(fileStream, data, KnownJsonType.Default.PerformanceMeasurementResults);
        await fileStream.FlushAsync();
    }
}
