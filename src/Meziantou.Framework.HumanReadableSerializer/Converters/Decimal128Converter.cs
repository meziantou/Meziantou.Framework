#if NET11_0_OR_GREATER
using System.Numerics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class Decimal128Converter : HumanReadableConverter<Decimal128>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Decimal128 value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
#endif
