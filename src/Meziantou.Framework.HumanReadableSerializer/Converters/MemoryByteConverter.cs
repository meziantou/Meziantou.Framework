namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class MemoryByteConverter : HumanReadableConverter<Memory<byte>>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Memory<byte> value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(Convert.ToBase64String(value.ToArray()));
    }
}
