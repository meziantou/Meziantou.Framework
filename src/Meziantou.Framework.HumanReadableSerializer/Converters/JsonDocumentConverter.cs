using System.Text.Json;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class JsonDocumentConverter : HumanReadableConverter<JsonDocument>
{
    protected override void WriteValue(HumanReadableTextWriter writer, JsonDocument? value, HumanReadableSerializerOptions options)
    {
        var str = JsonSerializer.Serialize(value, JsonElementConverter.IndentedOptions);
        writer.WriteFormattedValue("application/json", str);
    }
}
