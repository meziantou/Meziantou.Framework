namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class TimeSpanConverter : HumanReadableConverter<TimeSpan>
{
    protected override void WriteValue(HumanReadableTextWriter writer, TimeSpan value, HumanReadableSerializerOptions options)
    {
        Write(writer, value);
    }

    internal static void Write(HumanReadableTextWriter writer, TimeSpan value)
    {
        writer.WriteValue(value.ToString("c", CultureInfo.InvariantCulture));
    }
}
