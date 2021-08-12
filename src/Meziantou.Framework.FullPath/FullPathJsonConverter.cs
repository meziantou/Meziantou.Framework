using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework;

[SuppressMessage("Usage", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated dynamically")]
internal sealed class FullPathJsonConverter : JsonConverter<FullPath>
{
    public override FullPath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var path = reader.GetString();
        if (string.IsNullOrEmpty(path))
            return FullPath.Empty;

        return FullPath.FromPath(path);
    }

    public override void Write(Utf8JsonWriter writer, FullPath value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
