using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The boolean entropy decoder of VP8 (RFC 6386 section 7): a binary arithmetic decoder with 8-bit probabilities over one
/// partition of a fully buffered payload. Bytes past the end of the partition read as zeros and are counted in
/// <see cref="OverrunBytes"/>, so that the caller can reject partitions that are too short.
/// </summary>
internal sealed class Vp8BoolDecoder
{
    private readonly byte[] _data;
    private readonly int _end;
    private int _position;
    private uint _value;
    private uint _range;
    private int _bitCount;

    /// <summary>Starts decoding <paramref name="data"/>[<paramref name="offset"/>..<paramref name="offset"/> + <paramref name="length"/>].</summary>
    public Vp8BoolDecoder(byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, data.Length - length);
        _data = data;
        _position = offset;
        _end = offset + length;

        // The value holds two bytes: the high one is compared with the split, the low one is the lookahead
        _value = ((uint)NextByte() << 8) | NextByte();
        _range = 255;
    }

    /// <summary>Gets the number of bytes read past the end of the partition (as zeros).</summary>
    public int OverrunBytes { get; private set; }

    /// <summary>Decodes one boolean whose probability of being <see langword="false"/> is <paramref name="probability"/>/256.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBool(int probability)
    {
        var split = 1 + (((_range - 1) * (uint)probability) >> 8);
        var bigSplit = split << 8;
        bool result;
        if (_value >= bigSplit)
        {
            result = true;
            _range -= split;
            _value -= bigSplit;
        }
        else
        {
            result = false;
            _range = split;
        }

        if (_range < 128)
        {
            Normalize();
        }

        return result;
    }

    /// <summary>Decodes a boolean with probability 1/2 (<c>B(128)</c>).</summary>
    public int ReadBit() => ReadBool(128) ? 1 : 0;

    /// <summary>Decodes an unsigned literal of <paramref name="bits"/> bits, most significant first (<c>L(n)</c>).</summary>
    public int ReadLiteral(int bits)
    {
        var value = 0;
        while (bits-- > 0)
        {
            value = (value << 1) | ReadBit();
        }

        return value;
    }

    /// <summary>Decodes an optional signed value: a presence flag, then a <paramref name="bits"/>-bit magnitude and a sign bit; absent values are zero.</summary>
    public int ReadOptionalSigned(int bits)
    {
        if (ReadBit() == 0)
            return 0;

        var magnitude = ReadLiteral(bits);
        return ReadBit() != 0 ? -magnitude : magnitude;
    }

    /// <summary>Decodes a tree-coded value (RFC 6386 section 8.1): positive entries are node indexes, others are negated leaves.</summary>
    public int ReadTree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities)
    {
        var index = 0;
        while ((index = tree[index + (ReadBool(probabilities[index >> 1]) ? 1 : 0)]) > 0)
        {
        }

        return -index;
    }

    private void Normalize()
    {
        // Shift until the range is at least 128; a new byte enters the low 8 bits after every 8 shifts
        var shift = BitOperations.LeadingZeroCount(_range) - 24;
        _range <<= shift;
        _value <<= shift;
        _bitCount += shift;
        if (_bitCount >= 8)
        {
            _bitCount -= 8;
            _value |= (uint)NextByte() << _bitCount;
        }
    }

    private byte NextByte()
    {
        if (_position < _end)
            return _data[_position++];

        OverrunBytes++;
        return 0;
    }
}
