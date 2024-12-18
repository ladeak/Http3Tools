using System.Text.Json;
using CHttp.Abstractions;
using CHttp.Data;
using CHttp.Performance.Data;

namespace CHttp.Performance.Statitics;

internal class PerformanceFileHandler
{
    public static async ValueTask<PerformanceMeasurementResults> LoadAsync(IFileSystem fileSystem, string diffFile)
    {
        using (var file1 = fileSystem.Open(diffFile, FileMode.Open, FileAccess.Read))
            return await JsonSerializer.DeserializeAsync(file1, PerformanceKnownJsonType.Default.PerformanceMeasurementResults) ?? PerformanceMeasurementResults.Default;
    }

    public static ValueTask SaveAsync(IFileSystem fileSystem, string filePath, PerformanceBehavior behavior, IReadOnlyCollection<Summary> summaries, long bytesRead, long maxConnectionCount)
    {
        var session = new PerformanceMeasurementResults { Summaries = summaries, TotalBytesRead = bytesRead, MaxConnections = maxConnectionCount, Behavior = behavior };
        return SaveAsync(fileSystem, filePath, session);
    }

    public static async ValueTask SaveAsync(IFileSystem fileSystem, string filePath, PerformanceMeasurementResults session)
    {
        using var fileStream = fileSystem.Open(filePath, FileMode.Create, FileAccess.Write);
        await JsonSerializer.SerializeAsync(fileStream, session, PerformanceKnownJsonType.Default.PerformanceMeasurementResults);
        await fileStream.FlushAsync();
    }
}
