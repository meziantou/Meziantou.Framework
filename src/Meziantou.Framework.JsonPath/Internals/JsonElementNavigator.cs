using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Unicode;

namespace Meziantou.Framework.Json.Internals;

internal sealed class JsonElementNavigator : JsonPathNavigator<JsonElement>
{
    public override bool IsPure => true;

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

    /// <summary>
    /// The indexer of an array whose elements are objects or arrays walks the array from its start, so reading
    /// every element by index would take quadratic time.
    /// </summary>
    public override bool HasConstantTimeElementAccess => false;

    public override bool HasConstantTimePropertyLookup => false;

    /// <summary><see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> returns the last of duplicate properties.</summary>
    internal override bool PropertyLookupMatchesEnumeration => true;

    public override void CopyElements(JsonElement value, JsonElement[] destination)
    {
        var index = 0;
        foreach (var element in value.EnumerateArray())
        {
            destination[index] = element;
            index++;
        }
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

    public override bool TryGetNumberText(JsonElement value, [NotNullWhen(true)] out string? result)
    {
        if (value.ValueKind is JsonValueKind.Number)
        {
            result = value.GetRawText();
            return true;
        }

        result = null;
        return false;
    }

    internal override bool TryStringEquals(JsonElement value, ReadOnlySpan<byte> utf8, out bool equals)
    {
        return TryRawStringEquals(value, utf8, out equals);
    }

    /// <summary>Compares a string with UTF-8 text using the raw text of the string, which has no escape sequence to decode most of the time.</summary>
    /// <returns>
    /// <see langword="false"/> when the string has an escape sequence, or is not valid UTF-8 and differs from <paramref name="utf8"/>:
    /// <see cref="JsonElement.GetString"/> throws for it, and the caller must too.
    /// </returns>
    internal static bool TryRawStringEquals(JsonElement value, ReadOnlySpan<byte> utf8, out bool equals)
    {
        if (value.ValueKind is JsonValueKind.String)
        {
            // The raw value is quoted
            var raw = JsonMarshal.GetRawUtf8Value(value);
            raw = raw[1..^1];
            if (!raw.Contains((byte)'\\'))
            {
                equals = raw.SequenceEqual(utf8);
                if (equals || Utf8.IsValid(raw))
                    return true;
            }
        }

        equals = false;
        return false;
    }

    internal override bool TryGetNumberUtf8(JsonElement value, Span<byte> buffer, out ReadOnlySpan<byte> result)
    {
        if (value.ValueKind is JsonValueKind.Number)
        {
            result = JsonMarshal.GetRawUtf8Value(value);
            return true;
        }

        result = default;
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
