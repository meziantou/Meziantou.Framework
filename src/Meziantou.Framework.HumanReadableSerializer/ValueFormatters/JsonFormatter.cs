using System.Text.Json.Nodes;
using System.Text.Json;

namespace Meziantou.Framework.HumanReadable.ValueFormatters;

internal sealed class JsonFormatter : ValueFormatter
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

    public JsonFormatter(JsonFormatterOptions options)
    {
        _options = options;
    }

    public override void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
    {
        try
        {
            var node = JsonSerializer.Deserialize<JsonNode>(value);
            if (_options.OrderProperties)
            {
                OrderNode(node);
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
}
