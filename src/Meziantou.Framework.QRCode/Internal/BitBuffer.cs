namespace Meziantou.Framework.Internal;

internal sealed class BitBuffer
{
    private byte[]? _bytes;

    public int BitCount { get; private set; }

    public void Append(int value, int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitCount, sizeof(int) * 8);

        if (bitCount == 0)
            return;

        EnsureCapacity(BitCount + bitCount);
        var bytes = _bytes;

        var remainingBits = bitCount;
        if ((BitCount & 7) is 0)
        {
            while (remainingBits >= 8)
            {
                remainingBits -= 8;
                bytes[BitCount >> 3] = (byte)(value >> remainingBits);
                BitCount += 8;
            }
        }

        while (remainingBits > 0)
        {
            remainingBits--;
            var byteIndex = BitCount >> 3;
            var bitIndex = 7 - (BitCount & 7);

            if (((value >> remainingBits) & 1) == 1)
            {
                bytes[byteIndex] |= (byte)(1u << bitIndex);
            }

            BitCount++;
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
        var byteCount = (BitCount + 7) >> 3;
        if (byteCount is 0 || _bytes is null)
            return [];

        return [.. _bytes[..byteCount]];
    }
}
