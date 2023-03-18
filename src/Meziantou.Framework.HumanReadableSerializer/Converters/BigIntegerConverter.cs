using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class BigIntegerConverter : HumanReadableConverter<BigInteger>
{
    protected override void WriteValue(HumanReadableTextWriter writer, BigInteger value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
