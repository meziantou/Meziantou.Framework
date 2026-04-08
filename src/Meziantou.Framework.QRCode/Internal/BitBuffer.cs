namespace Meziantou.Framework.Internal;

internal sealed class BitBuffer
{
    private readonly List<byte> _bytes = [];
    private int _bitCount;

    public int BitCount => _bitCount;

    public void Append(int value, int bitCount)
    {
        for (var i = bitCount - 1; i >= 0; i--)
        {
            var byteIndex = _bitCount >> 3;
            var bitIndex = 7 - (_bitCount & 7);

            if (byteIndex >= _bytes.Count)
            {
                _bytes.Add(0);
            }

            if (((value >> i) & 1) == 1)
            {
                _bytes[byteIndex] |= (byte)(1 << bitIndex);
            }

            _bitCount++;
        }
    }

    public byte[] ToByteArray()
    {
        return [.. _bytes];
    }
}
