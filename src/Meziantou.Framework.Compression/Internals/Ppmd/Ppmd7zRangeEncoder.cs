using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>The range encoder of the 7z PPMd coder (Ppmd7z_RangeEnc of 7-Zip). It writes to a buffer the owner flushes.</summary>
internal struct Ppmd7zRangeEncoder : IPpmd7RangeEncoder
{
    private const uint TopValue = 1 << 24;

    private byte[] _buffer;
    private int _position;
    private Stream _output;
    private ulong _low;
    private uint _range;
    private byte _cache;
    private long _cacheSize;

    public void Initialize(Stream output, byte[] buffer)
    {
        _output = output;
        _buffer = buffer;
        _position = 0;
        _low = 0;
        _range = 0xFFFFFFFF;
        _cache = 0;
        _cacheSize = 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Encode(uint start, uint size, uint total)
    {
        _range /= total;
        _low += start * _range;
        _range *= size;
        Normalize();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeBit0(uint size0)
    {
        _range = (_range >> 14) * size0;
        Normalize();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeBit1(uint size0)
    {
        var newBound = (_range >> 14) * size0;
        _low += newBound;
        _range -= newBound;
        Normalize();
    }

    /// <summary>Writes the last bytes of the code (Ppmd7z_Flush_RangeEnc).</summary>
    public void FlushData()
    {
        for (var i = 0; i < 5; i++)
        {
            ShiftLow();
        }
    }

    /// <summary>Writes the buffered bytes to the output stream.</summary>
    public void FlushBuffer()
    {
        if (_position > 0)
        {
            _output.Write(_buffer, 0, _position);
            _position = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Normalize()
    {
        while (_range < TopValue)
        {
            _range <<= 8;
            ShiftLow();
        }
    }

    private void ShiftLow()
    {
        if ((uint)_low < 0xFF000000u || (uint)(_low >> 32) != 0)
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

    private void WriteByte(byte value)
    {
        if (_position == _buffer.Length)
        {
            FlushBuffer();
        }

        _buffer[_position++] = value;
    }
}
