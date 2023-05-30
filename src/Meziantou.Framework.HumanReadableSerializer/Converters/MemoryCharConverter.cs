namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class MemoryCharConverter : HumanReadableConverter<Memory<char>>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Memory<char> value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString());
    }
}

