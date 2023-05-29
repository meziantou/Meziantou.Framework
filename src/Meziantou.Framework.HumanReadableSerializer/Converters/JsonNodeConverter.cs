using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class JsonNodeConverter : HumanReadableConverter<JsonNode>
{
    protected override void WriteValue(HumanReadableTextWriter writer, JsonNode? value, HumanReadableSerializerOptions options)
    {
        var str = JsonSerializer.Serialize(value, JsonElementConverter.IndentedOptions);
        writer.WriteValue(options.FormatValue("json", str));
    }
}
