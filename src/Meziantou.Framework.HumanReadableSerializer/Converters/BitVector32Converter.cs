using System.Collections.Specialized;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class BitVector32Converter : HumanReadableConverter<BitVector32>
{
    protected override void WriteValue(HumanReadableTextWriter writer, BitVector32 value, HumanReadableSerializerOptions options)
    {
        Span<char> str = stackalloc char[32];

        var locdata = unchecked(value.Data);
        for (var i = 0; i < str.Length; i++)
        {
            str[i] = (locdata & 0x80000000) != 0 ? '1' : '0';
            locdata <<= 1;
        }

        writer.WriteValue(str);
    }
}
