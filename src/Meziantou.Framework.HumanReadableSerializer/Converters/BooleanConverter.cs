namespace Meziantou.Framework.HumanReadable.Converters;
internal sealed class BooleanConverter : HumanReadableConverter<bool>
{
    protected override void WriteValue(HumanReadableTextWriter writer, bool value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value ? "true" : "false");
    }
}
