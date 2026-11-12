using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The JSON serialization of the .NET values, used when there is no script engine (see
/// <see cref="ScriptEngine.SerializeJson(Dom.GlobalScope, object?)"/>): the primitive values and the JSON nodes.
/// </summary>
internal static class DotNetJson
{
    public static string? Serialize(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case JsonNode node:
                return node.ToJsonString();
            case JsonElement element:
                return element.GetRawText();
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            switch (value)
            {
                case string s:
                    writer.WriteStringValue(s);
                    break;
                case bool b:
                    writer.WriteBooleanValue(b);
                    break;
                case decimal d:
                    writer.WriteNumberValue(d);
                    break;
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double:
                    var number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);

                    // JSON.stringify() serializes the numbers that are not finite as null
                    if (double.IsFinite(number))
                    {
                        writer.WriteNumberValue(number);
                    }
                    else
                    {
                        writer.WriteNullValue();
                    }

                    break;
                default:
                    throw new ArgumentException($"A value of type '{value.GetType().Name}' cannot be serialized to JSON", nameof(value));
            }
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static object? Parse(string json, out bool isObject)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new Dom.JavaScriptSyntaxErrorException(ex.Message, ex);
        }

        isObject = node is JsonObject or JsonArray;
        return node;
    }
}
