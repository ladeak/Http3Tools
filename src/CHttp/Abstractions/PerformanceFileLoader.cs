using System.Text.Json;
using CHttp.Statitics;

namespace CHttp.Abstractions;

internal class PerformanceFileLoader
{
    public static async Task<PerformanceMeasurementResults> LoadAsync(IFileSystem fileSystem, string diffFile)
    {
        using (var file1 = fileSystem.Open(diffFile, FileMode.Open, FileAccess.Read))
            return (await JsonSerializer.DeserializeAsync(file1, KnownJsonType.Default.PerformanceMeasurementResults)) ?? PerformanceMeasurementResults.Default;
    }
}
