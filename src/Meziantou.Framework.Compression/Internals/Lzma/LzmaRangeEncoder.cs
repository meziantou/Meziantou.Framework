// A port of the range encoder of the LZMA SDK (LzmaEnc.c) by Igor Pavlov, which is in the public domain.
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>The range encoder of the LZMA SDK. It writes to a growable buffer that the caller empties.</summary>
internal sealed class LzmaRangeEncoder
{
    private const uint TopValue = 1u << 24;
    private const int NumBitModelTotalBits = 11;
    private const uint BitModelTotal = 1u << NumBitModelTotalBits;
    private const int NumMoveBits = 5;

    private uint _range;
    private ulong _low;
    private byte _cache;
    private long _cacheSize;
    private byte[] _buffer = new byte[1 << 16];
    private int _length;

    public LzmaRangeEncoder()
    {
        Init();
    }

    /// <summary>Gets the buffer that contains the encoded bytes.</summary>
    public byte[] Buffer => _buffer;

    /// <summary>Gets the number of encoded bytes in <see cref="Buffer"/>.</summary>
    public int Length => _length;

    /// <summary>Gets the number of bytes the encoder will have produced once flushed, since the last <see cref="Init"/>.</summary>
    public long Processed => _written + _length + _cacheSize + 4;

    private long _written;

    /// <summary>Resets the encoder for a new range-coded stream.</summary>
    public void Init()
    {
        _low = 0;
        _range = uint.MaxValue;
        _cacheSize = 1;
        _cache = 0;
        _length = 0;
        _written = 0;
    }

    /// <summary>Removes the encoded bytes from the buffer, once the caller has written them.</summary>
    public void Clear()
    {
        _written += _length;
        _length = 0;
    }

    /// <summary>Writes the last bytes of the stream.</summary>
    public void Flush()
    {
        for (var i = 0; i < 5; i++)
        {
            ShiftLow();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeBit(ushort[] probs, int index, uint bit)
    {
        uint prob = probs[index];
        var bound = (_range >> NumBitModelTotalBits) * prob;
        if (bit == 0)
        {
            _range = bound;
            probs[index] = (ushort)(prob + ((BitModelTotal - prob) >> NumMoveBits));
        }
        else
        {
            _low += bound;
            _range -= bound;
            probs[index] = (ushort)(prob - (prob >> NumMoveBits));
        }

        if (_range < TopValue)
        {
            _range <<= 8;
            ShiftLow();
        }
    }

    public void EncodeDirectBits(uint value, int numBits)
    {
        do
        {
            _range >>= 1;
            _low += _range & (0 - ((value >> --numBits) & 1));
            if (_range < TopValue)
            {
                _range <<= 8;
                ShiftLow();
            }
        }
        while (numBits != 0);
    }

    public void EncodeTree(ushort[] probs, int baseIndex, int numBits, uint symbol)
    {
        uint m = 1;
        for (var i = numBits; i != 0;)
        {
            i--;
            var bit = (symbol >> i) & 1;
            EncodeBit(probs, baseIndex + (int)m, bit);
            m = (m << 1) | bit;
        }
    }

    public void EncodeReverseTree(ushort[] probs, int baseIndex, int numBits, uint symbol)
    {
        uint m = 1;
        for (var i = 0; i < numBits; i++)
        {
            var bit = symbol & 1;
            EncodeBit(probs, baseIndex + (int)m, bit);
            m = (m << 1) | bit;
            symbol >>= 1;
        }
    }

    private void ShiftLow()
    {
        if ((uint)_low < 0xFF000000u || (_low >> 32) != 0)
        {
            var temp = _cache;
            do
            {
                WriteByte((byte)(temp + (byte)(_low >> 32)));
                temp = 0xFF;
            }
            while (--_cacheSize != 0);

            _cache = (byte)((uint)_low >> 24);
        }

        _cacheSize++;
        _low = (uint)_low << 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteByte(byte value)
    {
        if (_length == _buffer.Length)
        {
            Array.Resize(ref _buffer, _buffer.Length * 2);
        }

        _buffer[_length++] = value;
    }
}
