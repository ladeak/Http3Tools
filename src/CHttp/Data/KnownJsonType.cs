using System.Text.Json.Serialization;
using CHttp.Data;

namespace CHttp.Data;

[JsonSerializable(typeof(PersistedCookies))]
internal partial class KnownJsonType : JsonSerializerContext
{
}