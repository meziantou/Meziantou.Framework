#if NETCOREAPP3_0_OR_GREATER
using System.Text.Json;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class JsonElementConverter : HumanReadableConverter<JsonElement>
{
    internal static readonly JsonSerializerOptions IndentedOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    protected override void WriteValue(HumanReadableTextWriter writer, JsonElement value, HumanReadableSerializerOptions options)
    {
        var str = JsonSerializer.Serialize(value, IndentedOptions);
        writer.WriteValue(str);
    }
}
#endif
