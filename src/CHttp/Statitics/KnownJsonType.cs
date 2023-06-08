using System.Text.Json.Serialization;

namespace CHttp.Statitics;

[JsonSerializable(typeof(PerformanceMeasurementResults))]
public partial class KnownJsonType : JsonSerializerContext
{
}