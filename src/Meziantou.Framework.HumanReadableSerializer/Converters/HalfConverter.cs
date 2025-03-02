#if NET5_0_OR_GREATER
using System.Globalization;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class HalfConverter : HumanReadableConverter<Half>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Half value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
#endif
