namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class GuidConverter : HumanReadableConverter<Guid>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Guid value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString());
    }
}

