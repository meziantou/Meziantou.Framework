using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>The range decoder of the 7z PPMd coder (Ppmd7z_RangeDec of 7-Zip).</summary>
internal struct Ppmd7zRangeDecoder : IPpmd7RangeDecoder
{
    private const uint TopValue = 1 << 24;

    private PpmdByteSource _source;
    private uint _range;
    private uint _code;
    private int _extraBytes;

    /// <summary>Gets the number of bytes the decoder read after the end of the data (it reads zeros there).</summary>
    public readonly int ExtraBytes => _extraBytes;

    /// <summary>Gets a value indicating whether the code is 0, as at the end of a stream the encoder flushed.</summary>
    public readonly bool IsFinishedOK => _code == 0;

    /// <summary>Reads the 5 first bytes. Returns <see langword="false"/> when they are not a valid start of a stream.</summary>
    public bool Initialize(PpmdByteSource source)
    {
        _source = source;
        _code = 0;
        _range = 0xFFFFFFFF;
        _extraBytes = 0;
        if (ReadByte() != 0)
            return false;

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

        _range /= total;
        return _code / _range;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Decode(uint start, uint size)
    {
        _code -= start * _range;
        _range *= size;
        Normalize();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint DecodeBit(uint size0)
    {
        var newBound = (_range >> 14) * size0;
        uint symbol;
        if (_code < newBound)
        {
            symbol = 0;
            _range = newBound;
        }
        else
        {
            symbol = 1;
            _code -= newBound;
            _range -= newBound;
        }

        Normalize();
        return symbol;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Normalize()
    {
        if (_range < TopValue)
        {
            NormalizeSlow();
        }
    }

    private void NormalizeSlow()
    {
        if (_range == 0)
        {
            // Only an inconsistent model can get here: report it as an error rather than looping forever
            _range = 1;
            _extraBytes = int.MaxValue;
            return;
        }

        do
        {
            _code = (_code << 8) | ReadByte();
            _range <<= 8;
        }
        while (_range < TopValue);
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
