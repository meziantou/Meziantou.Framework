using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>
/// The carry-less range encoder of Dmitry Shkarin (Subbotin's coder) that PPMd var.I uses (the range encoder of 7-Zip's
/// Ppmd8Enc.c), the counterpart of <see cref="PpmdCarrylessRangeDecoder"/>. It writes to a buffer the owner flushes.
/// </summary>
internal struct PpmdCarrylessRangeEncoder
{
    private const uint Top = 1 << 24;
    private const uint Bot = 1 << 15;
    private const uint BinScale = 1 << 14;

    private byte[] _buffer;
    private int _position;
    private Stream _output;
    private uint _low;
    private uint _range;

    public void Initialize(Stream output, byte[] buffer)
    {
        _output = output;
        _buffer = buffer;
        _position = 0;
        _low = 0;
        _range = 0xFFFFFFFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Encode(uint start, uint size, uint total)
    {
        _range /= total;
        _low += start * _range;
        _range *= size;
        Normalize();
    }

    /// <summary>Encodes a 0 whose probability is <paramref name="size0"/> / 2^14.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeBit0(uint size0)
    {
        _range = (_range >> 14) * size0;
        Normalize();
    }

    /// <summary>Encodes a 1 when the probability of 0 is <paramref name="size0"/> / 2^14.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeBit1(uint size0)
    {
        var bound = (_range >> 14) * size0;
        _low += bound;
        _range = (_range & ~(BinScale - 1)) - bound;
        Normalize();
    }

    /// <summary>Writes the last bytes of the code (Ppmd8_Flush_RangeEnc).</summary>
    public void FlushData()
    {
        for (var i = 0; i < 4; i++)
        {
            WriteByte((byte)(_low >> 24));
            _low <<= 8;
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
        if ((_low ^ (_low + _range)) < Top || _range < Bot)
        {
            NormalizeSlow();
        }
    }

    private void NormalizeSlow()
    {
        while (true)
        {
            if ((_low ^ (_low + _range)) >= Top)
            {
                if (_range >= Bot)
                    break;

                _range = (0 - _low) & (Bot - 1);
            }

            WriteByte((byte)(_low >> 24));
            _range <<= 8;
            _low <<= 8;
        }
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
