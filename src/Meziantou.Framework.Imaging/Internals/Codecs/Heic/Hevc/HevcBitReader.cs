using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the syntax elements that appear directly in a raw byte sequence payload, most significant bit first (Rec. ITU-T
/// H.265, clauses 7.2 "Specification of syntax functions and descriptors" and 9.2 "Parsing process for 0-th order
/// Exp-Golomb codes"): <c>u(n)</c>, <c>ue(v)</c>, <c>se(v)</c>, <c>more_rbsp_data()</c>, <c>rbsp_trailing_bits()</c> and
/// <c>byte_alignment()</c>. The data are the payload of a NAL unit without its emulation prevention bytes
/// (<see cref="HevcRbspBuffer"/>). Reading past the end of the data is <see cref="InvalidImageContentException"/>: missing
/// bits are never read as zeros.
/// </summary>
internal ref struct HevcBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private long _position;
    private long _trailingBitPosition;

    public HevcBitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _trailingBitPosition = -1;
    }

    /// <summary>Gets the position of the next bit, from the start of the data.</summary>
    public readonly long BitPosition => _position;

    /// <summary>Gets the position of the first byte that holds no consumed bit.</summary>
    public readonly int BytePosition => (int)((_position + 7) >> 3);

    /// <summary>Gets the number of bits that are not consumed yet.</summary>
    public readonly long RemainingBits => ((long)_data.Length << 3) - _position;

    /// <summary>Gets whether the position is the first bit of a byte (<c>byte_aligned()</c>).</summary>
    public readonly bool IsByteAligned => (_position & 7) is 0;

    /// <summary>Reads an unsigned <paramref name="count"/>-bit number (<c>u(n)</c>), 0 to 32 bits.</summary>
    public uint ReadBits(int count)
    {
        Debug.Assert(count is >= 0 and <= 32);
        if (count is 0)
            return 0;

        if (count > RemainingBits)
            throw HevcErrors.Truncated();

        var index = (int)(_position >> 3);
        var bitOffset = (int)(_position & 7);
        var byteCount = (bitOffset + count + 7) >> 3;
        ulong accumulator = 0;
        for (var i = 0; i < byteCount; i++)
        {
            accumulator = (accumulator << 8) | _data[index + i];
        }

        _position += count;
        return (uint)((accumulator >> ((byteCount << 3) - bitOffset - count)) & ((1UL << count) - 1));
    }

    /// <summary>Reads a one-bit flag (<c>u(1)</c>).</summary>
    public bool ReadFlag() => ReadBits(1) is not 0;

    /// <summary>
    /// Reads an unsigned Exp-Golomb-coded number (<c>ue(v)</c>, clause 9.2.1): 0 to 2^32 - 2. The prefix of zero bits is
    /// bounded: a prefix of more than 32 bits is not a value of the syntax.
    /// </summary>
    public uint ReadUE()
    {
        var leadingZeroBits = 0;
        while (!ReadFlag())
        {
            if (++leadingZeroBits > 32)
                throw HevcErrors.Invalid("an Exp-Golomb code has a prefix of more than 32 bits.");
        }

        if (leadingZeroBits is 0)
            return 0;

        var codeNum = ((1UL << leadingZeroBits) - 1) + ReadBits(leadingZeroBits);
        if (codeNum > uint.MaxValue - 1)
            throw HevcErrors.Invalid("an Exp-Golomb code is larger than the largest value of the syntax.");

        return (uint)codeNum;
    }

    /// <summary>Reads an unsigned Exp-Golomb-coded number that the syntax limits to the range 0 to <paramref name="maximum"/>.</summary>
    /// <exception cref="InvalidImageContentException">The value is larger than <paramref name="maximum"/>.</exception>
    public int ReadUE(int maximum, string name)
    {
        Debug.Assert(maximum >= 0);
        var value = ReadUE();
        if (value > (uint)maximum)
            throw HevcErrors.Invalid($"{name} is out of range.");

        return (int)value;
    }

    /// <summary>Reads a signed Exp-Golomb-coded number (<c>se(v)</c>, clause 9.2.2).</summary>
    public int ReadSE()
    {
        var codeNum = ReadUE();
        var magnitude = (long)(((ulong)codeNum + 1) >> 1);
        return (int)((codeNum & 1) is not 0 ? magnitude : -magnitude);
    }

    /// <summary>
    /// Reads a signed Exp-Golomb-coded number that the syntax limits to the range <paramref name="minimum"/> to
    /// <paramref name="maximum"/>.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The value is out of the range.</exception>
    public int ReadSE(int minimum, int maximum, string name)
    {
        var value = ReadSE();
        if (value < minimum || value > maximum)
            throw HevcErrors.Invalid($"{name} is out of range.");

        return value;
    }

    /// <summary>Skips <paramref name="count"/> bits.</summary>
    public void Skip(long count)
    {
        Debug.Assert(count >= 0);
        if (count > RemainingBits)
            throw HevcErrors.Truncated();

        _position += count;
    }

    /// <summary>
    /// Gets whether syntax elements follow in the payload before its trailing bits (<c>more_rbsp_data()</c>, clause 7.2):
    /// whether the position is before the last bit equal to 1 of the data, which is the <c>rbsp_stop_one_bit</c>.
    /// </summary>
    public bool MoreRbspData()
    {
        if (_trailingBitPosition < 0)
        {
            _trailingBitPosition = 0;
            for (var i = _data.Length - 1; i >= 0; i--)
            {
                if (_data[i] is not 0)
                {
                    _trailingBitPosition = ((long)i << 3) + 7 - int.TrailingZeroCount(_data[i]);
                    break;
                }
            }
        }

        return _position < _trailingBitPosition;
    }

    /// <summary>
    /// Skips the extension data of a parameter set, the bits that a decoder ignores up to the trailing bits of the payload:
    /// <c>while( more_rbsp_data( ) ) extension_data_flag</c>.
    /// </summary>
    public void SkipExtensionData()
    {
        if (MoreRbspData())
        {
            _position = _trailingBitPosition;
        }
    }

    /// <summary>
    /// Reads the trailing bits that end a payload (<c>rbsp_trailing_bits()</c>, clause 7.3.2.11): a bit equal to 1, then
    /// bits equal to 0 up to the end of the data.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The trailing bits are not the end of the data, or have another value.</exception>
    public void ReadRbspTrailingBits()
    {
        if (RemainingBits <= 0)
            throw HevcErrors.Truncated();

        if (!ReadFlag())
            throw HevcErrors.Invalid("a payload does not end with its stop bit.");

        while (!IsByteAligned)
        {
            if (ReadFlag())
                throw HevcErrors.Invalid("the alignment bits of a payload are not zero.");
        }

        if (RemainingBits is not 0)
            throw HevcErrors.Invalid("data follow the trailing bits of a payload.");
    }

    /// <summary>
    /// Reads the bits that align the position on a byte (<c>byte_alignment()</c>, clause 7.3.2.12): a bit equal to 1, then
    /// bits equal to 0.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The bits have another value.</exception>
    public void ReadByteAlignment()
    {
        if (!ReadFlag())
            throw HevcErrors.Invalid("an alignment does not start with a bit equal to 1.");

        while (!IsByteAligned)
        {
            if (ReadFlag())
                throw HevcErrors.Invalid("the alignment bits are not zero.");
        }
    }
}
