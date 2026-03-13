using System.Text.Json.Serialization;

namespace Meziantou.Framework.Http;

[JsonSerializable(typeof(HttpCachePersistenceEntry))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault, PropertyNameCaseInsensitive = false)]
internal sealed partial class SqliteSerializationContext : JsonSerializerContext
{
}
