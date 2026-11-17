using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>
/// The carry-less range decoder of Dmitry Shkarin (Subbotin's coder), used by RAR 2.9/4.x (Ppmd7a_RangeDec of 7-Zip) and by
/// PPMd var.I (Ppmd8).
/// </summary>
internal struct PpmdCarrylessRangeDecoder : IPpmd7RangeDecoder
{
    private const uint Top = 1 << 24;
    private const uint Bot = 1 << 15;
    private const uint BinScale = 1 << 14;

    private PpmdByteSource _source;
    private uint _low;
    private uint _range;
    private uint _code;
    private int _extraBytes;

    /// <summary>Gets the number of bytes the decoder read after the end of the data (it reads zeros there).</summary>
    public readonly int ExtraBytes => _extraBytes;

    /// <summary>Gets a value indicating whether the code equals the low bound, as at the end of a stream the encoder flushed.</summary>
    public readonly bool IsFinishedOK => _code == _low;

    /// <summary>Reads the 4 first bytes. Returns <see langword="false"/> when they are not a valid start of a stream.</summary>
    public bool Initialize(PpmdByteSource source)
    {
        _source = source;
        _code = 0;
        _low = 0;
        _range = 0xFFFFFFFF;
        _extraBytes = 0;
        for (var i = 0; i < 4; i++)
        {
            _code = (_code << 8) | ReadByte();
        }

        return _code < 0xFFFFFFFF && _extraBytes == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetThreshold(uint total)
    {
        if (total == 0)
            return uint.MaxValue;

        var range = _range / total;
        if (range == 0)
            return uint.MaxValue;

        _range = range;
        return (_code - _low) / range;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Decode(uint start, uint size)
    {
        _low += start * _range;
        _range *= size;
        Normalize();
    }

    /// <summary>Divides the range by 2^14 and returns the count of the current code in it (binary contexts of Ppmd8).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetBinaryThreshold()
    {
        _range >>= 14;
        return _range == 0 ? uint.MaxValue : (_code - _low) / _range;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint DecodeBit(uint size0)
    {
        if (GetBinaryThreshold() < size0)
        {
            Decode(0, size0);
            return 0;
        }

        Decode(size0, BinScale - size0);
        return 1;
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
        if (_range == 0)
        {
            // Only an inconsistent model can get here: report it as an error rather than looping forever
            _range = Bot;
            _extraBytes = int.MaxValue;
            return;
        }

        while (true)
        {
            if ((_low ^ (_low + _range)) >= Top)
            {
                if (_range >= Bot)
                    break;

                _range = (0 - _low) & (Bot - 1);
            }

            _code = (_code << 8) | ReadByte();
            _range <<= 8;
            _low <<= 8;
        }
    }

    private uint ReadByte()
    {
        var value = _source.ReadByte();
        if (value < 0)
        {
            if (_extraBytes < int.MaxValue)
            {
                _extraBytes++;
            }

            return 0;
        }

        return (uint)value;
    }
}
