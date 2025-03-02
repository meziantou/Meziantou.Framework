namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class CharConverter : HumanReadableConverter<char>
{
    protected override void WriteValue(HumanReadableTextWriter writer, char value, HumanReadableSerializerOptions options)
    {
        ReadOnlySpan<char> buffer = [value];
        writer.WriteValue(buffer);
    }
}

