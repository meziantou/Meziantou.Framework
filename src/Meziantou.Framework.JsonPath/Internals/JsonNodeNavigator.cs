using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.Json.Internals;

internal sealed class JsonNodeNavigator : JsonPathNavigator<JsonNode>
{
    public static JsonNodeNavigator Instance { get; } = new();

    private JsonNodeNavigator()
    {
    }

    public override JsonPathNodeKind GetKind(JsonNode? value)
    {
        return value switch
        {
            null => JsonPathNodeKind.Null,
            JsonArray => JsonPathNodeKind.Array,
            JsonObject => JsonPathNodeKind.Object,
            JsonValue jsonValue => jsonValue.GetValueKind() switch
            {
                JsonValueKind.True or JsonValueKind.False => JsonPathNodeKind.Boolean,
                JsonValueKind.Number => JsonPathNodeKind.Number,
                JsonValueKind.String => JsonPathNodeKind.String,
                JsonValueKind.Null => JsonPathNodeKind.Null,
                _ => JsonPathNodeKind.Null,
            },
            _ => JsonPathNodeKind.Null,
        };
    }

    public override bool TryGetPropertyValue(JsonNode? value, string name, out JsonNode? result)
    {
        if (value is JsonObject obj)
        {
            return obj.TryGetPropertyValue(name, out result);
        }

        result = null;
        return false;
    }

    public override IEnumerable<JsonPathProperty<JsonNode>> GetProperties(JsonNode? value)
    {
        if (value is not JsonObject obj)
        {
            yield break;
        }

        foreach (var property in obj)
        {
            yield return new JsonPathProperty<JsonNode>(property.Key, property.Value);
        }
    }

    public override int GetArrayLength(JsonNode? value)
    {
        return value is JsonArray array ? array.Count : 0;
    }

    public override bool TryGetElement(JsonNode? value, int index, out JsonNode? result)
    {
        if (value is JsonArray array && index >= 0 && index < array.Count)
        {
            result = array[index];
            return true;
        }

        result = null;
        return false;
    }

    public override bool TryGetString(JsonNode? value, out string? result)
    {
        if (value is JsonValue jsonValue && jsonValue.GetValueKind() is JsonValueKind.String)
        {
            result = GetStringValue(jsonValue);
            return true;
        }

        result = null;
        return false;
    }

    public override bool TryGetNumber(JsonNode? value, out double result)
    {
        if (value is JsonValue jsonValue && jsonValue.GetValueKind() is JsonValueKind.Number)
        {
            result = GetDoubleValue(jsonValue);
            return true;
        }

        result = 0;
        return false;
    }

    public override bool TryGetBoolean(JsonNode? value, out bool result)
    {
        if (value is JsonValue jsonValue)
        {
            switch (jsonValue.GetValueKind())
            {
                case JsonValueKind.True:
                    result = true;
                    return true;
                case JsonValueKind.False:
                    result = false;
                    return true;
            }
        }

        result = false;
        return false;
    }

    private static double GetDoubleValue(JsonValue value)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element.GetDouble();
        }

        if (value.TryGetValue<double>(out var d))
        {
            return d;
        }

        if (value.TryGetValue<float>(out var f))
        {
            return f;
        }

        if (value.TryGetValue<decimal>(out var dec))
        {
            return (double)dec;
        }

        if (value.TryGetValue<long>(out var l))
        {
            return l;
        }

        if (value.TryGetValue<ulong>(out var ul))
        {
            return ul;
        }

        if (value.TryGetValue<int>(out var i))
        {
            return i;
        }

        if (value.TryGetValue<uint>(out var ui))
        {
            return ui;
        }

        if (value.TryGetValue<short>(out var s))
        {
            return s;
        }

        if (value.TryGetValue<ushort>(out var us))
        {
            return us;
        }

        if (value.TryGetValue<byte>(out var b))
        {
            return b;
        }

        if (value.TryGetValue<sbyte>(out var sb))
        {
            return sb;
        }

        return 0;
    }

    private static string? GetStringValue(JsonValue value)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element.GetString();
        }

        if (value.TryGetValue<string>(out var s))
        {
            return s;
        }

        return value.ToString();
    }
}
