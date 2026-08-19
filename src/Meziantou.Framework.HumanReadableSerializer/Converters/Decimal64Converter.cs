#if NET11_0_OR_GREATER
using System.Numerics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class Decimal64Converter : HumanReadableConverter<Decimal64>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Decimal64 value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
#endif
