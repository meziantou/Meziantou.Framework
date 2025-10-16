namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class DateTimeOffsetConverter : HumanReadableConverter<DateTimeOffset>
{
    protected override void WriteValue(HumanReadableTextWriter writer, DateTimeOffset value, HumanReadableSerializerOptions options)
    {
        var format = "yyyy-MM-dd'T'HH:mm:ss";
        if (value.Millisecond != 0)
        {
            format += ".fffffff";
        }

        format += "zzz";

        writer.WriteValue(value.ToString(format, CultureInfo.InvariantCulture));
    }
}

