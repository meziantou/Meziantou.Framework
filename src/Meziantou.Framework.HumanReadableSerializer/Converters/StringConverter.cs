namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class StringConverter : HumanReadableConverter<string>
{
    protected override void WriteValue(HumanReadableTextWriter writer, string value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value);
    }
}

