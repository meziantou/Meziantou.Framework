using System.Collections;

namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class BitArrayYamlishConverter : ScalarYamlishConverter<BitArray>
{
    protected override BitArray Parse(string value)
    {
        var result = new BitArray(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            result[i] = value[i] switch
            {
                '0' => false,
                '1' => true,
                _ => throw new FormatException($"Cannot convert '{value}' to '{typeof(BitArray)}'."),
            };
        }

        return result;
    }

    protected override string Format(BitArray value)
    {
        return string.Create(value.Length, value, static (span, bits) =>
        {
            for (var i = 0; i < bits.Length; i++)
            {
                span[i] = bits[i] ? '1' : '0';
            }
        });
    }
}
