using System.Diagnostics;
using System.Text.Json.Nodes;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class JsonNodeConverter : HumanReadableConverter<JsonNode>
{
    protected override void WriteValue(HumanReadableTextWriter writer, JsonNode? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        var str = value.ToJsonString(JsonElementConverter.IndentedOptions);
        writer.WriteFormattedValue(ValueFormatter.JsonMediaTypeName, str);
    }
}
