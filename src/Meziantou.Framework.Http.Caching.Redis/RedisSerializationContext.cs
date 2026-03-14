using System.Text.Json.Serialization;

namespace Meziantou.Framework.Http.Caching.Redis;

[JsonSerializable(typeof(HttpCachePersistenceEntry))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault, PropertyNameCaseInsensitive = false)]
internal sealed partial class RedisSerializationContext : JsonSerializerContext
{
}
