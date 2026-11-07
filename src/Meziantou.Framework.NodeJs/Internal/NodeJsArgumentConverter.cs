using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.NodeJs.Internal;

/// <summary>Prevents <see cref="JSValue"/> and <see cref="JSReference"/> from being serialized as JSON, as only the arguments of a call can represent them.</summary>
internal sealed class NodeJsArgumentConverter<T> : JsonConverter<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotSupportedException($"{typeof(T).Name} cannot be deserialized.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        throw new NotSupportedException($"{typeof(T).Name} cannot be serialized as JSON. It can only be used as an argument of a Node.js call, directly or in a JsonObject or JsonArray argument.");
    }
}
