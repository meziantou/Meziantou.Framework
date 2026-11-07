using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework;

/// <summary>Converts a <see cref="FullPath"/> to and from JSON.</summary>
public sealed class FullPathJsonConverter : JsonConverter<FullPath>
{
    public override FullPath Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var path = reader.GetString();
        if (string.IsNullOrEmpty(path))
            return FullPath.Empty;

        try
        {
            return FullPath.FromPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // System.Text.Json only translates InvalidOperationException from a converter, so these would otherwise
            // escape Deserialize and defeat a caller that catches JsonException. The path is left out of the message
            // because it comes from the payload and may be sensitive; it is available on the inner exception.
            throw new JsonException("The JSON value cannot be converted to a FullPath.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, FullPath value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
