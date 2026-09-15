using System.Text.Encodings.Web;
using System.Text.Json;
using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class JsonElementConverter : HumanReadableConverter<JsonElement>
{
    internal static readonly JsonSerializerOptions IndentedOptions = new()
    {
        RespectNullableAnnotations = false,
        RespectRequiredConstructorParameters = false,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    protected override void WriteValue(HumanReadableTextWriter writer, JsonElement value, HumanReadableSerializerOptions options)
    {
        // A default JsonElement is not attached to a document, and serializing it throws
        if (value.ValueKind is JsonValueKind.Undefined)
        {
            writer.WriteValue("<undefined>");
            return;
        }

        var str = JsonSerializer.Serialize(value, IndentedOptions);
        writer.WriteFormattedValue(ValueFormatter.JsonMediaTypeName, str);
    }
}
