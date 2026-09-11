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
            return TryGetDoubleValue(jsonValue, out result);
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

    /// <summary>
    /// Reads the numeric value out of a <see cref="JsonValue"/>, whatever CLR type it happens to wrap.
    /// A representation that is not covered reports failure rather than standing in a value of its own: RFC 9535
    /// turns a comparison it cannot carry out into no match, whereas a stand-in of 0 would silently make the
    /// value compare equal to 0.
    /// </summary>
    /// <param name="value">A value whose kind is <see cref="JsonValueKind.Number"/>.</param>
    /// <param name="result">The value as a <see cref="double"/>.</param>
    /// <returns><see langword="true"/> when the representation is known; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetDoubleValue(JsonValue value, out double result)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            result = element.GetDouble();
            return true;
        }

        if (value.TryGetValue<double>(out var d))
        {
            result = d;
            return true;
        }

        if (value.TryGetValue<float>(out var f))
        {
            result = f;
            return true;
        }

        if (value.TryGetValue<Half>(out var h))
        {
            result = (double)h;
            return true;
        }

        if (value.TryGetValue<decimal>(out var dec))
        {
            result = (double)dec;
            return true;
        }

        if (value.TryGetValue<long>(out var l))
        {
            result = l;
            return true;
        }

        if (value.TryGetValue<ulong>(out var ul))
        {
            result = ul;
            return true;
        }

        if (value.TryGetValue<Int128>(out var i128))
        {
            result = (double)i128;
            return true;
        }

        if (value.TryGetValue<UInt128>(out var ui128))
        {
            result = (double)ui128;
            return true;
        }

        if (value.TryGetValue<int>(out var i))
        {
            result = i;
            return true;
        }

        if (value.TryGetValue<uint>(out var ui))
        {
            result = ui;
            return true;
        }

        if (value.TryGetValue<short>(out var sh))
        {
            result = sh;
            return true;
        }

        if (value.TryGetValue<ushort>(out var us))
        {
            result = us;
            return true;
        }

        if (value.TryGetValue<byte>(out var b))
        {
            result = b;
            return true;
        }

        if (value.TryGetValue<sbyte>(out var sb))
        {
            result = sb;
            return true;
        }

        result = 0;
        return false;
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
