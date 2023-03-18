#if NET7_0_OR_GREATER
using System.Globalization;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class UInt128Converter : HumanReadableConverter<UInt128>
{
    protected override void WriteValue(HumanReadableTextWriter writer, UInt128 value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
#endif