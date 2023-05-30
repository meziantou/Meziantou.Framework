namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ReadOnlyMemoryByteConverter : HumanReadableConverter<ReadOnlyMemory<byte>>
{
    protected override void WriteValue(HumanReadableTextWriter writer, ReadOnlyMemory<byte> value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(Convert.ToBase64String(value.ToArray()));
    }
}

