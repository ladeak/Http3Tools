using System.Text.Json.Serialization;

namespace CHttp.Statitics;

[JsonSerializable(typeof(PerformanceMeasurementResults))]
internal partial class KnownJsonType : JsonSerializerContext
{
}