using System.Text.Json.Serialization;

namespace CHttp.Performance.Data;

[JsonSerializable(typeof(PerformanceMeasurementResults))]
internal partial class PerformanceKnownJsonType : JsonSerializerContext
{
}