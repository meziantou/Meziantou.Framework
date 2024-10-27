using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class EnumConverter : HumanReadableConverter
{
    public override bool CanConvert(Type type) => type.IsEnum;

    public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);

        writer.WriteValue(value.ToString() ?? "");
    }
}
