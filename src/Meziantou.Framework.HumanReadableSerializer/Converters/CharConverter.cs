namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class CharConverter : HumanReadableConverter<char>
{
    protected override void WriteValue(HumanReadableTextWriter writer, char value, HumanReadableSerializerOptions options)
    {
        Span<char> buffer = stackalloc char[1];
        buffer[0] = value;
        writer.WriteValue(buffer);
    }
}

