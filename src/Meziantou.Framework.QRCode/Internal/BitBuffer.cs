namespace Meziantou.Framework.Internal;

internal sealed class BitBuffer
{
    private byte[]? _bytes;
    private int _bitCount;

    public int BitCount => _bitCount;

    public void Append(int value, int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitCount, sizeof(int) * 8);

        if (bitCount == 0)
            return;

        EnsureCapacity(_bitCount + bitCount);
        var bytes = _bytes;

        var remainingBits = bitCount;
        if ((_bitCount & 7) == 0)
        {
            while (remainingBits >= 8)
            {
                remainingBits -= 8;
                bytes[_bitCount >> 3] = (byte)(value >> remainingBits);
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
                bytes[byteIndex] |= (byte)(1u << bitIndex);
            }

            _bitCount++;
        }
    }

    [MemberNotNull(nameof(_bytes))]
    private void EnsureCapacity(int bitCount)
    {
        var requiredByteCount = (bitCount + 7) >> 3;
        if (_bytes is null)
        {
            _bytes = new byte[Math.Max(requiredByteCount, 8)];
            return;
        }

        if (requiredByteCount <= _bytes.Length)
            return;

        var newLength = _bytes.Length;
        while (newLength < requiredByteCount)
        {
            newLength *= 2;
        }

        Array.Resize(ref _bytes, newLength);
    }

    public byte[] ToByteArray()
    {
        var byteCount = (_bitCount + 7) >> 3;
        if (byteCount == 0 || _bytes is null)
            return [];

        return _bytes[..byteCount].ToArray();
    }
}
