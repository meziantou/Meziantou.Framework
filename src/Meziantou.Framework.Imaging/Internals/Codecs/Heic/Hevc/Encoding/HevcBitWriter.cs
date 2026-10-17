using System.Diagnostics;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the syntax elements that appear directly in a raw byte sequence payload, most significant bit first (Rec. ITU-T
/// H.265, clauses 7.2 "Specification of syntax functions and descriptors" and 9.2 "Parsing process for 0-th order
/// Exp-Golomb codes"): <c>u(n)</c>, <c>ue(v)</c>, <c>se(v)</c>, <c>rbsp_trailing_bits()</c> and <c>byte_alignment()</c>.
/// The mirror of <see cref="HevcBitReader"/>. The bytes are those of the payload: the emulation prevention bytes are added
/// when the payload is put in a NAL unit (<see cref="HevcNalWriter"/>).
/// </summary>
internal ref struct HevcBitWriter
{
    private readonly PooledPayloadWriter _output;
    private readonly int _start;
    private uint _pending;
    private int _pendingBits;

    /// <summary>Creates a writer that appends to <paramref name="output"/>.</summary>
    public HevcBitWriter(PooledPayloadWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _start = output.Length;
    }

    /// <summary>Gets the number of bits written.</summary>
    public readonly long BitPosition => ((long)(_output.Length - _start) << 3) + _pendingBits;

    /// <summary>Gets whether the position is the first bit of a byte (<c>byte_aligned()</c>).</summary>
    public readonly bool IsByteAligned => _pendingBits is 0;

    /// <summary>Writes an unsigned <paramref name="count"/>-bit number (<c>u(n)</c>), 0 to 32 bits.</summary>
    public void WriteBits(uint value, int count)
    {
        Debug.Assert(count is >= 0 and <= 32);
        Debug.Assert(count is 32 || (value >> count) is 0);

        // At most 7 bits are pending: the accumulator has at most 39 bits
        var accumulator = ((ulong)_pending << count) | value;
        var bits = _pendingBits + count;
        while (bits >= 8)
        {
            bits -= 8;
            _output.Write((byte)(accumulator >> bits));
        }

        _pending = (uint)(accumulator & ((1UL << bits) - 1));
        _pendingBits = bits;
    }

    /// <summary>Writes a one-bit flag (<c>u(1)</c>).</summary>
    public void WriteFlag(bool value) => WriteBits(value ? 1u : 0u, 1);

    /// <summary>
    /// Writes an unsigned Exp-Golomb-coded number (<c>ue(v)</c>, clause 9.2.1), 0 to 2^32 - 2: as many zero bits as
    /// <c>codeNum + 1</c> has bits after its first one, then <c>codeNum + 1</c>.
    /// </summary>
    public void WriteUE(uint value)
    {
        Debug.Assert(value < uint.MaxValue);
        var coded = value + 1;
        var leadingZeroBits = BitOperations.Log2(coded);
        WriteBits(0, leadingZeroBits);
        WriteBits(coded, leadingZeroBits + 1);
    }

    /// <summary>
    /// Writes a signed Exp-Golomb-coded number (<c>se(v)</c>, clause 9.2.2): the positive value <c>v</c> has the code
    /// number <c>2 * v - 1</c>, the other ones <c>-2 * v</c> (Table 9-3).
    /// </summary>
    public void WriteSE(int value)
    {
        Debug.Assert(value > int.MinValue);
        WriteUE(value > 0 ? ((uint)value << 1) - 1 : (uint)(-(long)value << 1));
    }

    /// <summary>
    /// Writes the trailing bits that end a payload (<c>rbsp_trailing_bits()</c>, clause 7.3.2.11): a bit equal to 1, then
    /// bits equal to 0 up to the next byte.
    /// </summary>
    public void WriteRbspTrailingBits()
    {
        WriteBits(1, 1);
        WriteAlignmentZeroBits();
    }

    /// <summary>
    /// Writes the bits that align the position on a byte (<c>byte_alignment()</c>, clause 7.3.2.12): a bit equal to 1, then
    /// bits equal to 0. They end a slice segment header.
    /// </summary>
    public void WriteByteAlignment() => WriteRbspTrailingBits();

    /// <summary>Writes bits equal to 0 up to the next byte, none when the position is aligned (<c>pcm_alignment_zero_bit</c>).</summary>
    public void WriteAlignmentZeroBits()
    {
        if (_pendingBits is not 0)
        {
            WriteBits(0, 8 - _pendingBits);
        }
    }

    /// <summary>Returns the number of bits of the <c>ue(v)</c> form of <paramref name="value"/>.</summary>
    public static int GetUELength(uint value)
    {
        Debug.Assert(value < uint.MaxValue);
        return (BitOperations.Log2(value + 1) << 1) + 1;
    }

    /// <summary>Returns the number of bits of the <c>se(v)</c> form of <paramref name="value"/>.</summary>
    public static int GetSELength(int value)
    {
        Debug.Assert(value > int.MinValue);
        return GetUELength(value > 0 ? ((uint)value << 1) - 1 : (uint)(-(long)value << 1));
    }
}
