using System.Globalization;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class Int64Converter : HumanReadableConverter<long>
{
    protected override void WriteValue(HumanReadableTextWriter writer, long value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}

