namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ByteConverter : HumanReadableConverter<byte>
{
    protected override void WriteValue(HumanReadableTextWriter writer, byte value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}

