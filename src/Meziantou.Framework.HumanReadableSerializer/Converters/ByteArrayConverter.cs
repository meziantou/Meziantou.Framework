namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ByteArrayConverter : HumanReadableConverter<byte[]>
{
    protected override void WriteValue(HumanReadableTextWriter writer, byte[] value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(Convert.ToBase64String(value));
    }
}

