using System.Text.Json;

namespace Meziantou.Framework.Json.Internals;

internal sealed class JsonElementNavigator : JsonPathNavigator<JsonElement>
{
    public static JsonElementNavigator Instance { get; } = new();

    private JsonElementNavigator()
    {
    }

    public override JsonPathNodeKind GetKind(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => JsonPathNodeKind.Object,
            JsonValueKind.Array => JsonPathNodeKind.Array,
            JsonValueKind.String => JsonPathNodeKind.String,
            JsonValueKind.Number => JsonPathNodeKind.Number,
            JsonValueKind.True or JsonValueKind.False => JsonPathNodeKind.Boolean,
            JsonValueKind.Null or JsonValueKind.Undefined => JsonPathNodeKind.Null,
            _ => JsonPathNodeKind.Null,
        };
    }

    public override bool TryGetPropertyValue(JsonElement value, string name, out JsonElement result)
    {
        if (value.ValueKind is JsonValueKind.Object && value.TryGetProperty(name, out var propertyValue))
        {
            result = propertyValue;
            return true;
        }

        result = default;
        return false;
    }

    public override IEnumerable<JsonPathProperty<JsonElement>> GetProperties(JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.Object)
            yield break;

        foreach (var property in value.EnumerateObject())
        {
            yield return new JsonPathProperty<JsonElement>(property.Name, property.Value);
        }
    }

    public override int GetArrayLength(JsonElement value)
    {
        return value.ValueKind is JsonValueKind.Array ? value.GetArrayLength() : 0;
    }

    public override bool TryGetElement(JsonElement value, int index, out JsonElement result)
    {
        if (value.ValueKind is JsonValueKind.Array && index >= 0 && index < value.GetArrayLength())
        {
            result = value[index];
            return true;
        }

        result = default;
        return false;
    }

    public override bool TryGetString(JsonElement value, out string? result)
    {
        if (value.ValueKind is JsonValueKind.String)
        {
            result = value.GetString();
            return true;
        }

        result = null;
        return false;
    }

    public override bool TryGetNumber(JsonElement value, out double result)
    {
        if (value.ValueKind is JsonValueKind.Number)
        {
            return value.TryGetDouble(out result);
        }

        result = 0;
        return false;
    }

    public override bool TryGetBoolean(JsonElement value, out bool result)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.True:
                result = true;
                return true;
            case JsonValueKind.False:
                result = false;
                return true;
        }

        result = false;
        return false;
    }
}
