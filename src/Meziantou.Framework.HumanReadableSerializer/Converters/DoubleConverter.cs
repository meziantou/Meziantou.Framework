namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class DoubleConverter : HumanReadableConverter<double>
{
    protected override void WriteValue(HumanReadableTextWriter writer, double value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
