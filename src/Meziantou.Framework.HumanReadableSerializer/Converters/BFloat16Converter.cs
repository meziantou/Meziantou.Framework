#if NET11_0_OR_GREATER
using System.Numerics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class BFloat16Converter : HumanReadableConverter<BFloat16>
{
    protected override void WriteValue(HumanReadableTextWriter writer, BFloat16 value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
#endif
