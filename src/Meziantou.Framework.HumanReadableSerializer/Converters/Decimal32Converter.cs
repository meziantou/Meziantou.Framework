#if NET11_0_OR_GREATER
using System.Numerics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class Decimal32Converter : HumanReadableConverter<Decimal32>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Decimal32 value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
#endif
