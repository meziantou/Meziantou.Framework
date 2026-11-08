namespace Meziantou.Framework.Barcodes.Internal.Aztec;

internal sealed class AztecBitList
{
    private readonly List<bool> _bits = [];

    public int Count => _bits.Count;

    public bool this[int index] => _bits[index];

    // The bits of the value, most significant first
    public void Append(int value, int bitCount)
    {
        for (var i = bitCount - 1; i >= 0; i--)
        {
            _bits.Add(((value >> i) & 1) != 0);
        }
    }

    public void Append(bool bit) => _bits.Add(bit);
}
