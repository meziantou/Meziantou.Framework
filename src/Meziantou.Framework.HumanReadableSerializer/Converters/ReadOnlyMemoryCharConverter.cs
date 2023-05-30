namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ReadOnlyMemoryCharConverter : HumanReadableConverter<ReadOnlyMemory<char>>
{
    protected override void WriteValue(HumanReadableTextWriter writer, ReadOnlyMemory<char> value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.Span);
    }
}

