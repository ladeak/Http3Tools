using System.Text.Json.Serialization;
using CHttp.Data;

namespace CHttp.Statitics;

[JsonSerializable(typeof(PerformanceMeasurementResults))]
[JsonSerializable(typeof(PersistedCookies))]
internal partial class KnownJsonType : JsonSerializerContext
{
}