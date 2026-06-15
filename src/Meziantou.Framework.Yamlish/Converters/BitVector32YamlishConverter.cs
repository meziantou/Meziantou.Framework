using System.Collections.Specialized;

namespace Meziantou.Framework.Yamlish;

internal sealed class BitVector32YamlishConverter : ScalarYamlishConverter<BitVector32>
{
    protected override BitVector32 Parse(string value)
    {
        if (value.Length is not 32)
            throw new FormatException($"Cannot convert '{value}' to '{typeof(BitVector32)}'.");

        var data = 0u;
        foreach (var character in value)
        {
            data = character switch
            {
                '0' => data << 1,
                '1' => (data << 1) | 1,
                _ => throw new FormatException($"Cannot convert '{value}' to '{typeof(BitVector32)}'."),
            };
        }

        return new BitVector32(unchecked((int)data));
    }

    protected override string Format(BitVector32 value) => Convert.ToString(value.Data, toBase: 2).PadLeft(32, '0');
}
