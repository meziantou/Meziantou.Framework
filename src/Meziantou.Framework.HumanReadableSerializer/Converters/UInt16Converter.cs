using System.Globalization;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class UInt16Converter : HumanReadableConverter<ushort>
{
    protected override void WriteValue(HumanReadableTextWriter writer, ushort value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}

