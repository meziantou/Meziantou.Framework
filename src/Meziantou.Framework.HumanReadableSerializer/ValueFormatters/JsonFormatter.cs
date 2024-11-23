using System.Text.Json.Nodes;
using System.Text.Json;
using Meziantou.Framework.HumanReadable.Converters;

namespace Meziantou.Framework.HumanReadable.ValueFormatters;

public sealed class JsonFormatter : ValueFormatter
{
    private static readonly JsonSerializerOptions NonIndentedOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonFormatterOptions _options;

    public JsonFormatter()
        : this(options: null)
    {
    }

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
        catch
        {
            writer.WriteValue(value);
        }
    }

    private static void OrderNode(JsonNode node)
    {
        var queue = new Queue<JsonNode>();
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
                EnumerableConverter<JsonNode>.WriteValueCore(writer, array, options);
                break;

            case JsonValueKind.String:
                HumanReadableSerializer.Serialize(writer, value.GetValue<string>(), options);
                break;
            case JsonValueKind.Number:
                var jsonValue = value.AsValue();
                if (jsonValue.TryGetValue<decimal>(out var decimalValue))
                {
                    HumanReadableSerializer.Serialize(writer, decimalValue, options);
                }
                else if (jsonValue.TryGetValue<double>(out var doubleValue))
                {
                    HumanReadableSerializer.Serialize(writer, doubleValue, options);
                }
                else if (jsonValue.TryGetValue<float>(out var floatValue))
                {
                    HumanReadableSerializer.Serialize(writer, floatValue, options);
                }
                else if (jsonValue.TryGetValue<long>(out var longValue))
                {
                    HumanReadableSerializer.Serialize(writer, longValue, options);
                }
                else if (jsonValue.TryGetValue<ulong>(out var ulongValue))
                {
                    HumanReadableSerializer.Serialize(writer, ulongValue, options);
                }
                else if (jsonValue.TryGetValue<int>(out var intValue))
                {
                    HumanReadableSerializer.Serialize(writer, intValue, options);
                }
                else if (jsonValue.TryGetValue<uint>(out var uintValue))
                {
                    HumanReadableSerializer.Serialize(writer, uintValue, options);
                }
                else if (jsonValue.TryGetValue<short>(out var shortValue))
                {
                    HumanReadableSerializer.Serialize(writer, shortValue, options);
                }
                else if (jsonValue.TryGetValue<ushort>(out var ushortValue))
                {
                    HumanReadableSerializer.Serialize(writer, ushortValue, options);
                }
                else if (jsonValue.TryGetValue<byte>(out var byteValue))
                {
                    HumanReadableSerializer.Serialize(writer, byteValue, options);
                }
                else if (jsonValue.TryGetValue<sbyte>(out var sbyteValue))
                {
                    HumanReadableSerializer.Serialize(writer, sbyteValue, options);
                }
                else
                {
                    HumanReadableSerializer.Serialize(writer, value.ToJsonString(JsonElementConverter.IndentedOptions), options);
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
}
