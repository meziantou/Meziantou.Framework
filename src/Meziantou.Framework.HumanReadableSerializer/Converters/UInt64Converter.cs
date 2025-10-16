namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class UInt64Converter : HumanReadableConverter<ulong>
{
    protected override void WriteValue(HumanReadableTextWriter writer, ulong value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}

