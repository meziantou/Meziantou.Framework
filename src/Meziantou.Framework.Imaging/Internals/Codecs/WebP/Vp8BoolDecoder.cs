using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The boolean entropy decoder of VP8 (RFC 6386 section 7): a binary arithmetic decoder with 8-bit probabilities over one
/// partition of a fully buffered payload. Bytes past the end of the partition read as zeros and are counted in
/// <see cref="OverrunBytes"/>, so that the caller can reject partitions that are too short.
/// </summary>
/// <remarks>
/// <para>
/// The decoder is a mutable value: hot loops copy its state into locals (registers) and store it back.
/// </para>
/// <para>
/// The 64-bit value holds the 8-bit window compared with the split in bits 40-47, 16 bits of headroom above it, and up to 40
/// lookahead bits below it, refilled 5 bytes at a time. This reproduces a decoder that holds the window in bits 8-15 of a
/// 32-bit register and reads one byte after every 8 bits consumed: both keep the same 16 bits above the window, which
/// matters for invalid partitions that start with 0xFF (the window is then not below the range, and the excess bits are
/// truncated after 16 shifts). <see cref="OverrunBytes"/> is derived from the number of bits consumed, as if the bytes were
/// read one at a time (two bytes when decoding starts, then one byte after every 8 bits consumed).
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal struct Vp8BoolDecoder
{
    private readonly byte[] _data;
    private readonly int _start;
    private readonly int _end;

    // The next byte to load; it may run past _end (those bytes read as zeros)
    private int _position;

    // The bit position of the window, and the number of meaningful bits below it (negative: the low bits of the window are
    // not loaded yet and read as zeros)
    private const int WindowShift = 40;

    private ulong _value;
    private uint _range;
    private int _bits;

    /// <summary>Starts decoding <paramref name="data"/>[<paramref name="offset"/>..<paramref name="offset"/> + <paramref name="length"/>].</summary>
    public Vp8BoolDecoder(byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, data.Length - length);
        if (offset + length < 8)
        {
            // The refill reads the 8 bytes that end the partition when it reaches the end: give them room
            var padded = new byte[8 + length];
            data.AsSpan(offset, length).CopyTo(padded.AsSpan(8));
            data = padded;
            offset = 8;
        }

        _data = data;
        _start = offset;
        _position = offset;
        _end = offset + length;
        _range = 255;
        _value = NextBytes() << 8;
        _bits = WindowShift - 8;
    }

    /// <summary>Gets the number of bytes read past the end of the partition (as zeros).</summary>
    public readonly int OverrunBytes
    {
        get
        {
            // Bits consumed by the normalization shifts; a byte-at-a-time reader holds two bytes, then reads one per 8 bits
            var consumed = ((long)(_position - _start) * 8) - 8 - _bits;
            var read = 2 + (consumed >> 3);
            return (int)Math.Max(0, read - (_end - _start));
        }
    }

    /// <summary>Decodes one boolean whose probability of being <see langword="false"/> is <paramref name="probability"/>/256.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBool(int probability) => ReadBool(ref this, ref _value, ref _range, ref _bits, probability);

    /// <summary>Copies the arithmetic decoder state into locals, for a hot loop that calls the static <see cref="ReadBool(ref Vp8BoolDecoder, ref ulong, ref uint, ref int, int)"/>.</summary>
    public readonly void Load(out ulong value, out uint range, out int bits)
    {
        value = _value;
        range = _range;
        bits = _bits;
    }

    /// <summary>Stores the arithmetic decoder state loaded by <see cref="Load"/> back.</summary>
    public void Store(ulong value, uint range, int bits)
    {
        _value = value;
        _range = range;
        _bits = bits;
    }

    /// <summary>
    /// Decodes one boolean with the arithmetic decoder state held by the caller (see <see cref="Load"/>): once inlined, the state
    /// stays in registers.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadBool(ref Vp8BoolDecoder decoder, ref ulong value, ref uint range, ref int bits, int probability)
    {
        // The window is refilled before it is used rather than after the shift, so that no call separates the decision from
        // its use; the state may therefore be stored with up to 7 missing bits
        var currentBits = bits;
        var currentValue = value;
        if (currentBits < 0)
        {
            currentValue |= decoder.NextBytes() << -currentBits;
            currentBits += WindowShift;
        }

        var currentRange = range;
        var split = 1 + (((currentRange - 1) * (uint)probability) >> 8);
        var bigSplit = (ulong)split << WindowShift;
        var result = currentValue >= bigSplit;
        currentRange = result ? currentRange - split : split;
        currentValue = result ? currentValue - bigSplit : currentValue;

        // Normalize: shift the range back to [128, 255], and the value by as many bits
        var shift = BitOperations.LeadingZeroCount(currentRange) - 24;
        range = currentRange << shift;
        value = currentValue << shift;
        bits = currentBits - shift;
        return result;
    }

    /// <summary>Decodes a boolean with probability 1/2 (<c>B(128)</c>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    /// <summary>Reads the next 5 bytes of the partition (zeros past its end) as a big-endian value, and moves past them.</summary>
    /// <remarks>
    /// The window has at most 7 missing bits when it is refilled, so 5 more bytes fit below it. Near the end of the partition,
    /// the last 8 bytes of the partition are read and shifted so that zeros replace the bytes past its end; the constructor
    /// guarantees that these 8 bytes exist in the buffer. No call keeps the caller's state in registers.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong NextBytes()
    {
        var position = _position;
        _position = position + (WindowShift / 8);
        var start = Math.Min(position, _end - 8);
        var bytes = BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(start, 8));
        var skipped = position - start;
        return skipped < 8 ? (bytes << (8 * skipped)) >> (64 - WindowShift) : 0;
    }
}
