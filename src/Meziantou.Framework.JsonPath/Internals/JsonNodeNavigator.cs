using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.Json.Internals;

internal sealed class JsonNodeNavigator : JsonPathNavigator<JsonNode>
{
    // Long enough for the text of every primitive number type: a UInt128 takes 39 digits, a double 24 characters
    internal const int NumberBufferLength = 64;

    public override bool IsPure => true;

    /// <summary>
    /// The objects and arrays parsed out of a <see cref="JsonValue"/> that wraps a CLR object serialized as one.
    /// Parsing on every access would make walking such a value quadratic. This cache belongs to one evaluation so
    /// changes to the wrapped value and to returned detached nodes cannot affect later evaluations.
    /// </summary>
    private readonly ConditionalWeakTable<JsonValue, JsonNode> _parsedContainers = new();

    public static JsonNodeNavigator Instance => new();

    internal JsonNodeNavigator()
    {
    }

    internal override JsonPathNavigator<JsonNode> CreateEvaluationNavigator()
    {
        return new JsonNodeNavigator();
    }

    /// <summary>
    /// The names of a <see cref="JsonObject"/> are distinct, even ordinally when the object ignores case. A lookup finds
    /// the one property with the name.
    /// </summary>
    internal override bool PropertyLookupMatchesEnumeration => true;

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
                JsonValueKind.Object => JsonPathNodeKind.Object,
                JsonValueKind.Array => JsonPathNodeKind.Array,
                _ => JsonPathNodeKind.Null,
            },
            _ => JsonPathNodeKind.Null,
        };
    }

    public override bool TryGetPropertyValue(JsonNode? value, string name, out JsonNode? result)
    {
        if (AsContainer(value) is JsonObject obj)
        {
            if (obj.Options?.PropertyNameCaseInsensitive is true)
            {
                foreach (var property in obj)
                {
                    if (string.Equals(property.Key, name, StringComparison.Ordinal))
                    {
                        result = property.Value;
                        return true;
                    }
                }

                result = null;
                return false;
            }

            return obj.TryGetPropertyValue(name, out result);
        }

        result = null;
        return false;
    }

    public override IEnumerable<JsonPathProperty<JsonNode>> GetProperties(JsonNode? value)
    {
        if (AsContainer(value) is not JsonObject obj)
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
        return AsContainer(value) is JsonArray array ? array.Count : 0;
    }

    public override bool TryGetElement(JsonNode? value, int index, out JsonNode? result)
    {
        if (AsContainer(value) is JsonArray array && index >= 0 && index < array.Count)
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

    public override bool TryGetNumberText(JsonNode? value, [NotNullWhen(true)] out string? result)
    {
        Span<byte> buffer = stackalloc byte[NumberBufferLength];
        if (TryGetNumberUtf8(value, buffer, out var utf8))
        {
            result = Encoding.UTF8.GetString(utf8);
            return true;
        }

        result = null;
        return false;
    }

    internal override bool TryGetNumberUtf8(JsonNode? value, Span<byte> buffer, out ReadOnlySpan<byte> result)
    {
        if (value is JsonValue jsonValue && jsonValue.GetValueKind() is JsonValueKind.Number)
        {
            return TryGetNumberUtf8(jsonValue, buffer, out result);
        }

        result = default;
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
    /// Gets the object or array a node stands for. A <see cref="JsonValue"/> can wrap a CLR object that serializes
    /// as an object or an array; its members are only reachable from its serialized form, so the nodes found
    /// inside it are detached copies rather than parts of the original tree.
    /// </summary>
    private JsonNode? AsContainer(JsonNode? value)
    {
        if (value is JsonValue jsonValue && jsonValue.GetValueKind() is JsonValueKind.Object or JsonValueKind.Array)
        {
            return _parsedContainers.GetValue(jsonValue, wrapped =>
            {
                var reader = CreateReader(wrapped);
                return JsonNode.Parse(ref reader)!;
            });
        }

        return value;
    }

    /// <summary>
    /// Reads the numeric value out of a <see cref="JsonValue"/>, whatever CLR type it happens to wrap.
    /// Common representations are read directly. Anything else, such as an enum, is read back from its serialized
    /// form, which is what its kind was derived from in the first place.
    /// </summary>
    /// <param name="value">A value whose kind is <see cref="JsonValueKind.Number"/>.</param>
    /// <param name="result">The value as a <see cref="double"/>.</param>
    /// <returns><see langword="true"/> when the value is representable as a <see cref="double"/>; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetDoubleValue(JsonValue value, out double result)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element.TryGetDouble(out result);
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

        var reader = CreateReader(value);
        return reader.TryGetDouble(out result);
    }

    /// <summary>
    /// Writes the text of the number a <see cref="JsonValue"/> holds, consistently with <see cref="TryGetDoubleValue"/>:
    /// the text of a <see cref="double"/> is the one JSON writes, and a <see cref="float"/> or a <see cref="Half"/> is
    /// written as the <see cref="double"/> it converts to, so the text and the <see cref="double"/> describe the same value.
    /// </summary>
    private static bool TryGetNumberUtf8(JsonValue value, Span<byte> buffer, out ReadOnlySpan<byte> result)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            result = JsonMarshal.GetRawUtf8Value(element);
            return true;
        }

        if (TryFormat(value, buffer, out var written))
        {
            result = buffer[..written];
            return true;
        }

        // Anything else, such as an enum, is read back from its serialized form
        var reader = CreateReader(value);
        result = reader.ValueSpan;
        return true;
    }

    private static bool TryFormat(JsonValue value, Span<byte> buffer, out int written)
    {
        var culture = CultureInfo.InvariantCulture;
        if (value.TryGetValue<double>(out var d))
            return d.TryFormat(buffer, out written, "R", culture);

        if (value.TryGetValue<float>(out var f))
            return ((double)f).TryFormat(buffer, out written, "R", culture);

        if (value.TryGetValue<Half>(out var h))
            return ((double)h).TryFormat(buffer, out written, "R", culture);

        if (value.TryGetValue<decimal>(out var dec))
            return dec.TryFormat(buffer, out written, format: default, culture);

        if (value.TryGetValue<long>(out var l))
            return l.TryFormat(buffer, out written, format: default, culture);

        if (value.TryGetValue<ulong>(out var ul))
            return ul.TryFormat(buffer, out written, format: default, culture);

        if (value.TryGetValue<Int128>(out var i128))
            return i128.TryFormat(buffer, out written, format: default, culture);

        if (value.TryGetValue<UInt128>(out var ui128))
            return ui128.TryFormat(buffer, out written, format: default, culture);

        if (value.TryGetValue<int>(out var i))
            return i.TryFormat(buffer, out written, format: default, culture);

        if (value.TryGetValue<uint>(out var ui))
            return ui.TryFormat(buffer, out written, format: default, culture);

        if (value.TryGetValue<short>(out var sh))
            return sh.TryFormat(buffer, out written, format: default, culture);

        if (value.TryGetValue<ushort>(out var us))
            return us.TryFormat(buffer, out written, format: default, culture);

        if (value.TryGetValue<byte>(out var b))
            return b.TryFormat(buffer, out written, format: default, culture);

        if (value.TryGetValue<sbyte>(out var sb))
            return sb.TryFormat(buffer, out written, format: default, culture);

        written = 0;
        return false;
    }

    /// <summary>
    /// Reads the string value out of a <see cref="JsonValue"/>. A value wrapping a CLR type other than
    /// <see cref="string"/>, such as <see cref="DateTime"/>, <see cref="Guid"/> or <see cref="char"/>, is read back
    /// from its serialized form: <see cref="JsonNode.ToString()"/> would return it as quoted and escaped JSON text.
    /// </summary>
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

        var reader = CreateReader(value);
        return reader.GetString();
    }

    /// <summary>Serializes a value and positions a reader on its first token.</summary>
    /// <remarks>
    /// The value is written with the converter it was created with, so this stays safe for trimming, unlike
    /// serializing its CLR value again.
    /// </remarks>
    private static Utf8JsonReader CreateReader(JsonValue value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            value.WriteTo(writer);
        }

        var reader = new Utf8JsonReader(buffer.WrittenSpan);
        reader.Read();
        return reader;
    }
}
