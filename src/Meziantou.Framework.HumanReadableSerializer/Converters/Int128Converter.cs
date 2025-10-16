#if NET7_0_OR_GREATER
namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class Int128Converter : HumanReadableConverter<Int128>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Int128 value, HumanReadableSerializerOptions options)
    {
        writer.WriteValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
#endif