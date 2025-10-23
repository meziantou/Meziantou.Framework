#if NET6_0_OR_GREATER
namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class TimeOnlyConverter : HumanReadableConverter<TimeOnly>
{
    protected override void WriteValue(HumanReadableTextWriter writer, TimeOnly value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString("O", CultureInfo.InvariantCulture));
    }
}
#endif
