#if NET6_0_OR_GREATER
using System.Globalization;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class DateOnlyConverter : HumanReadableConverter<DateOnly>
{
    protected override void WriteValue(HumanReadableTextWriter writer, DateOnly value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString("O", CultureInfo.InvariantCulture));
    }
}
#endif