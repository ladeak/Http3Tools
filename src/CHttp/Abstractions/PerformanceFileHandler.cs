using System.Text.Json;
using CHttp.Statitics;

namespace CHttp.Abstractions;

internal class PerformanceFileHandler
{
    public static async ValueTask<PerformanceMeasurementResults> LoadAsync(IFileSystem fileSystem, string diffFile)
    {
        using (var file1 = fileSystem.Open(diffFile, FileMode.Open, FileAccess.Read))
            return (await JsonSerializer.DeserializeAsync(file1, KnownJsonType.Default.PerformanceMeasurementResults)) ?? PerformanceMeasurementResults.Default;
    }

    public static ValueTask SaveAsync(IFileSystem fileSystem, string filePath, IReadOnlyCollection<Summary> summaries, long bytesRead)
    {
        var session = new PerformanceMeasurementResults { Summaries = summaries, TotalBytesRead = bytesRead };
        return SaveAsync(fileSystem, filePath, session);
    }

    public static async ValueTask SaveAsync(IFileSystem fileSystem, string filePath, PerformanceMeasurementResults session)
    {
        using var fileStream = fileSystem.Open(filePath, FileMode.Create, FileAccess.Write);
        await JsonSerializer.SerializeAsync(fileStream, session, KnownJsonType.Default.PerformanceMeasurementResults);
        await fileStream.FlushAsync();
    }
}
