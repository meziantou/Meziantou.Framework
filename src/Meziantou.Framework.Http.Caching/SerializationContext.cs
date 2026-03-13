using System.Text.Json.Serialization;

namespace Meziantou.Framework.Http.Caching;

[JsonSerializable(typeof(SerializedResponseMessage))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault, PropertyNameCaseInsensitive = false)]
internal sealed partial class SerializationContext : JsonSerializerContext
{

}
