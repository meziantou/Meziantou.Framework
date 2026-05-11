namespace Meziantou.Framework.Internal;

internal sealed class BitBuffer
{
    private byte[] _bytes = [];
    private int _bitCount;

    public int BitCount => _bitCount;

    public void Append(int value, int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);
        if (bitCount > sizeof(int) * 8)
            throw new ArgumentOutOfRangeException(nameof(bitCount));

        if (bitCount == 0)
            return;

        EnsureCapacity(_bitCount + bitCount);

        var remainingBits = bitCount;
        if ((_bitCount & 7) == 0)
        {
            while (remainingBits >= 8)
            {
                remainingBits -= 8;
                _bytes[_bitCount >> 3] = (byte)(value >> remainingBits);
                _bitCount += 8;
            }
        }

        while (remainingBits > 0)
        {
            remainingBits--;
            var byteIndex = _bitCount >> 3;
            var bitIndex = 7 - (_bitCount & 7);

            if (((value >> remainingBits) & 1) == 1)
            {
                _bytes[byteIndex] |= (byte)(1u << bitIndex);
            }

            _bitCount++;
        }
    }

    private void EnsureCapacity(int bitCount)
    {
        var requiredByteCount = (bitCount + 7) >> 3;
        if (requiredByteCount <= _bytes.Length)
            return;

        var newLength = _bytes.Length == 0 ? 8 : _bytes.Length;
        while (newLength < requiredByteCount)
        {
            newLength *= 2;
        }

        Array.Resize(ref _bytes, newLength);
    }

    public byte[] ToByteArray()
    {
        var byteCount = (_bitCount + 7) >> 3;
        if (byteCount == 0)
            return [];

        return _bytes[..byteCount].ToArray();
    }
}
