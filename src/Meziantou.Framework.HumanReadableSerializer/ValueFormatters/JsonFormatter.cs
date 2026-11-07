using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.Json;
using Meziantou.Framework.HumanReadable.Converters;

namespace Meziantou.Framework.HumanReadable.ValueFormatters;

/// <summary>Formats JSON values for human-readable output.</summary>
public sealed class JsonFormatter : ValueFormatter
{
    private static readonly JsonSerializerOptions NonIndentedOptions = new()
    {
        RespectNullableAnnotations = false,
        RespectRequiredConstructorParameters = false,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        RespectNullableAnnotations = false,
        RespectRequiredConstructorParameters = false,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonFormatterOptions _options;

    /// <summary>Initializes a new instance of the <see cref="JsonFormatter"/> class with default options.</summary>
    public JsonFormatter()
        : this(options: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsonFormatter"/> class with the specified options.</summary>
    /// <param name="options">The formatting options.</param>
    public JsonFormatter(JsonFormatterOptions? options)
    {
        _options = options ?? new();
    }

    public override void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        try
        {
            var node = JsonSerializer.Deserialize<JsonNode>(value);
            if (node is null)
            {
                writer.WriteValue(value);
                return;
            }

            if (_options.OrderProperties)
            {
                OrderNode(node);
            }

            if (_options.FormatAsStandardObject)
            {
                WriteValueAsObject(writer, node, options);
                return;
            }

            writer.WriteValue(JsonSerializer.Serialize(node, _options.WriteIndented ? IndentedOptions : NonIndentedOptions));
        }
        catch (Exception ex) when (ex is not HumanReadableSerializerException)
        {
            // The value could not be parsed or reformatted: fall back to the raw text.
            // HumanReadableSerializerException is this library's own signal (for example
            // MaxDepth) and must not be turned into a silent formatting fallback.
            writer.WriteValue(value);
        }
    }

    private static void OrderNode(JsonNode node)
    {
        var queue = new Queue<JsonNode?>();
        queue.Enqueue(node);

        while (queue.Count > 0)
        {
            var currentNode = queue.Dequeue();

            if (currentNode is JsonObject jsonObject)
            {
                var properties = jsonObject.AsEnumerable().OrderBy(prop => prop.Key, StringComparer.Ordinal).ToArray();
                foreach (var property in properties)
                {
                    jsonObject.Remove(property.Key);
                }

                foreach (var property in properties)
                {
                    jsonObject.Add(property.Key, property.Value);
                    queue.Enqueue(property.Value);
                }
            }
            else if (currentNode is JsonArray jsonArray)
            {
                foreach (var value in jsonArray)
                {
                    queue.Enqueue(value);
                }
            }
        }
    }

    private static void WriteValueAsObject(HumanReadableTextWriter writer, JsonNode? value, HumanReadableSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var kind = value.GetValueKind();
        switch (kind)
        {
            case JsonValueKind.Undefined:
                writer.WriteFormattedValue(ValueFormatter.JsonMediaTypeName, value.ToJsonString(JsonElementConverter.IndentedOptions));
                break;

            case JsonValueKind.Object:
                var obj = value.AsObject();
                if (obj.Count is 0)
                {
                    writer.WriteEmptyObject();
                }
                else
                {
                    writer.StartObject();
                    foreach (var item in obj)
                    {
                        writer.WritePropertyName(item.Key);
                        HumanReadableSerializer.Serialize(writer, item.Value, options);
                    }

                    writer.EndObject();
                }

                break;

            case JsonValueKind.Array:
                var array = value.AsArray();
                EnumerableConverter<JsonNode?>.WriteValueCore(writer, array, options);
                break;

            case JsonValueKind.String:
                HumanReadableSerializer.Serialize(writer, value.GetValue<string>(), options);
                break;
            case JsonValueKind.Number:
                // decimal.Parse silently rounds the numbers it cannot represent (e.g. 1e-30 becomes 0), and double loses precision,
                // so the number is written as it appears in the JSON text when decimal cannot represent it exactly
                var rawText = value.ToJsonString();
                if (TryParseExactDecimal(rawText, out var decimalValue))
                {
                    HumanReadableSerializer.Serialize(writer, decimalValue, options);
                }
                else
                {
                    HumanReadableSerializer.Serialize(writer, rawText, options);
                }

                break;
            case JsonValueKind.True:
                HumanReadableSerializer.Serialize(writer, value: true, options);
                break;
            case JsonValueKind.False:
                HumanReadableSerializer.Serialize(writer, value: false, options);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
        }
    }

    private static bool TryParseExactDecimal(string text, out decimal value)
    {
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;

        // JSON numbers: -?digits(.digits)?([eE][+-]?digits)?
        var span = text.AsSpan();
        var isNegative = span.StartsWith("-");
        if (isNegative)
        {
            span = span[1..];
        }

        var exponent = 0;
        var exponentIndex = span.IndexOfAny('e', 'E');
        if (exponentIndex >= 0)
        {
            if (!int.TryParse(span[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
                return false;

            span = span[..exponentIndex];
        }

        var dotIndex = span.IndexOf('.');
        var textMantissa = dotIndex >= 0 ? BigInteger.Parse(string.Concat(span[..dotIndex], span[(dotIndex + 1)..]), NumberStyles.None, CultureInfo.InvariantCulture) : BigInteger.Parse(span, NumberStyles.None, CultureInfo.InvariantCulture);
        var textExponent = (long)exponent - (dotIndex >= 0 ? span.Length - dotIndex - 1 : 0);

        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        var decimalMantissa = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        long decimalExponent = -((bits[3] >> 16) & 0xFF);

        if (textMantissa.IsZero || decimalMantissa.IsZero)
            return textMantissa.IsZero && decimalMantissa.IsZero;

        if (isNegative != bits[3] < 0)
            return false;

        // Compare textMantissa * 10^textExponent with decimalMantissa * 10^decimalExponent
        var minExponent = Math.Min(textExponent, decimalExponent);
        return textMantissa * BigInteger.Pow(10, (int)(textExponent - minExponent)) == decimalMantissa * BigInteger.Pow(10, (int)(decimalExponent - minExponent));
    }
}
