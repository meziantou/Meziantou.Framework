using System.Globalization;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class DateTimeConverter : HumanReadableConverter<DateTime>
{
    protected override void WriteValue(HumanReadableTextWriter writer, DateTime value, HumanReadableSerializerOptions options)
    {
        var format = "yyyy-MM-dd'T'HH:mm:ss";
        if (value.Millisecond != 0)
        {
            format += ".fffffff";
        }

        if (value.Kind == DateTimeKind.Utc)
        {
            format += "'Z'";
        }
        else if (value.Kind == DateTimeKind.Local)
        {
            format += "zzz";
        }

        writer.WriteValue(value.ToString(format, CultureInfo.InvariantCulture));
    }
}

