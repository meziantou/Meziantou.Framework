using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The arithmetic encoding engine of the slice segment data (Rec. ITU-T H.265, clause 9.3.5 "Arithmetic encoding
/// process", which is informative): the engine whose output <see cref="HevcCabacDecoder"/> decodes. A context variable is
/// the byte of the decoder, <c>pStateIdx</c> in the bits 1 to 6 and <c>valMps</c> in the bit 0, and the tables are those of
/// the decoder (<see cref="HevcCabacTables"/>).
/// </summary>
/// <remarks>
/// <para>
/// The specification keeps the 10 low bits of <c>ivlLow</c> and counts the bits that it cannot write yet, because a later
/// addition may carry into them (<c>bitsOutstanding</c>, <c>PutBit</c>). This encoder keeps more bits of <c>ivlLow</c>
/// instead (<c>_low</c>, <c>_lowBits</c> bits of it): the bits above the 10 low ones are the bits that the specification
/// has written or counted. They are written by whole bytes as soon as a byte of them is complete, and an addition that
/// overflows the bits still held carries into the bytes already written
/// (<see cref="PooledPayloadWriter.PropagateCarry"/>). The interval never grows, so a carry never goes past the first byte
/// of the codeword. The bytes are the same as those of the specification.
/// </para>
/// <para>
/// The first bit that the specification would write is not written (<c>firstBitFlag</c>): it is the bit 9 of the initial
/// <c>ivlLow</c>, which is always 0 since the interval starts at 0 with a range of 510. The encoder therefore starts with
/// 9 bits of <c>ivlLow</c>, which are the 9 bits that a decoder reads first.
/// </para>
/// <para>
/// A codeword starts and ends on a byte of the output: the slice segment data follow the byte alignment of the slice
/// segment header, and the bits that follow a terminating bin are zero up to the next byte in each of its three uses
/// (<c>rbsp_slice_segment_trailing_bits()</c>, <c>byte_alignment()</c> and <c>pcm_alignment_zero_bit</c>).
/// </para>
/// </remarks>
internal sealed class HevcCabacEncoder
{
    // The bits of ivlLow that the specification keeps
    private const int RegisterBits = 10;

    private PooledPayloadWriter? _output;
    private ulong _low;
    private int _lowBits;
    private uint _range;

    /// <summary>
    /// Gets or sets <c>BinCountsInNalUnits</c>: the number of bins encoded since it was set to 0, which is done before the
    /// first slice segment of a picture (clause 9.3.5.2).
    /// </summary>
    public long BinCountsInNalUnits { get; set; }

    /// <summary>Gets whether a codeword is being written: <see cref="Initialize"/> was called, and no bin terminated it.</summary>
    public bool IsActive => _output is not null;

    /// <summary>
    /// Starts a codeword, which is appended to <paramref name="output"/> (clause 9.3.5.2 "Initialization process for the
    /// arithmetic encoding engine"): before the first coding tree unit of a slice segment, of a tile and of a row of the
    /// wavefront, and after the samples of a PCM coding unit.
    /// </summary>
    public void Initialize(PooledPayloadWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _low = 0;
        _lowBits = RegisterBits - 1;
        _range = 510;
    }

    /// <summary>
    /// Applies to a context variable the state transition of a bin (clause 9.3.4.3.2.2 "State transition process"), which
    /// is the one of the decoder.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UpdateContext(ref byte context, int binVal)
    {
        var pStateIdx = context >> 1;
        var valMps = context & 1;
        if (binVal == valMps)
        {
            context = (byte)((HevcCabacTables.TransIdxMps[pStateIdx] << 1) | valMps);
            return;
        }

        if (pStateIdx is 0)
        {
            valMps = 1 - valMps;
        }

        context = (byte)((HevcCabacTables.TransIdxLps[pStateIdx] << 1) | valMps);
    }

    /// <summary>
    /// Encodes a bin with a context variable, which is updated (clause 9.3.5.3 "Encoding process for a binary decision").
    /// </summary>
    /// <param name="context">The context variable: an element of <see cref="HevcCabacDecoder.Contexts"/>.</param>
    /// <param name="binVal">The bin: 0 or 1.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeDecision(ref byte context, int binVal)
    {
        Debug.Assert(_output is not null && binVal is 0 or 1);
        var pStateIdx = context >> 1;
        var valMps = context & 1;
        var qRangeIdx = (int)(_range >> 6) & 3;
        var ivlLpsRange = (uint)HevcCabacTables.RangeTabLps[(pStateIdx << 2) + qRangeIdx];
        _range -= ivlLpsRange;
        BinCountsInNalUnits++;
        UpdateContext(ref context, binVal);
        if (binVal == valMps)
        {
            // The range is at least 256 - 2: at most one bit
            if (_range < 256)
            {
                Renormalize(1);
            }

            return;
        }

        _low += _range;
        _range = ivlLpsRange;
        PropagateCarry();

        // Clause 9.3.5.4: the range is at least 6 and less than 256
        Renormalize(int.LeadingZeroCount((int)_range) - 23);
    }

    /// <summary>Encodes a bin without context (clause 9.3.5.5 "Bypass encoding process for binary decisions").</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncodeBypass(int binVal)
    {
        Debug.Assert(_output is not null && binVal is 0 or 1);
        BinCountsInNalUnits++;
        _low <<= 1;
        _lowBits++;
        if (binVal is not 0)
        {
            _low += _range;
            PropagateCarry();
        }

        WriteCompleteBytes();
    }

    /// <summary>
    /// Encodes <paramref name="count"/> bypass bins, the first one being the most significant bit of
    /// <paramref name="value"/>: 0 to 32 bins. The interval of each bin is the half of the one before it, so that the bins
    /// are one addition.
    /// </summary>
    public void EncodeBypassBits(uint value, int count)
    {
        Debug.Assert(_output is not null && count is >= 0 and <= 32);
        Debug.Assert(count is 32 || (value >> count) is 0);
        BinCountsInNalUnits += count;

        // At most 17 bits of ivlLow are held: 49 bits after the bins, and the product has at most 41 bits
        _low = (_low << count) + ((ulong)value * _range);
        _lowBits += count;
        PropagateCarry();
        WriteCompleteBytes();
    }

    /// <summary>
    /// Encodes a bin before termination (clause 9.3.5.6 "Encoding process for a binary decision before termination"):
    /// <c>end_of_slice_segment_flag</c>, <c>end_of_subset_one_bit</c> and <c>pcm_flag</c>. A bin equal to 1 terminates the
    /// codeword: it is flushed, its last bit is a bit equal to 1 (<c>rbsp_stop_one_bit</c>,
    /// <c>alignment_bit_equal_to_one</c>, or the bit that precedes <c>pcm_alignment_zero_bit</c>), and the output is
    /// completed with zero bits up to the next byte. <see cref="Initialize"/> starts the codeword that follows, if any.
    /// </summary>
    public void EncodeTerminate(int binVal)
    {
        Debug.Assert(_output is not null && binVal is 0 or 1);
        BinCountsInNalUnits++;
        _range -= 2;
        if (binVal is 0)
        {
            if (_range < 256)
            {
                Renormalize(1);
            }

            return;
        }

        _low += _range;
        PropagateCarry();
        Flush();
    }

    /// <summary>
    /// Returns the number of <c>cabac_zero_word</c> syntax elements to append after the slice segment data of the last
    /// slice segment of a picture (clause 9.3.5.7 "Byte stuffing process"), so that the picture has no more bins than
    /// clause A.4.1, item i, allows for its size in bytes.
    /// </summary>
    /// <param name="binCountsInNalUnits"><c>BinCountsInNalUnits</c> of the picture.</param>
    /// <param name="numBytesInVclNalUnits">The number of bytes of the slice segment NAL units of the picture, headers and emulation prevention bytes included.</param>
    /// <param name="rawMinCuBits"><c>RawMinCuBits</c> (Equation 7-41).</param>
    /// <param name="picSizeInMinCbsY">The number of minimum coding blocks of the picture.</param>
    public static long GetCabacZeroWordCount(long binCountsInNalUnits, long numBytesInVclNalUnits, int rawMinCuBits, long picSizeInMinCbsY)
    {
        // k = Ceil( ( Ceil( 3 * ( 32 * BinCountsInNalUnits - RawMinCuBits * PicSizeInMinCbsY ) / 1024 ) - NumBytesInVclNalUnits ) / 3 )
        var bits = 3 * ((32 * binCountsInNalUnits) - (rawMinCuBits * picSizeInMinCbsY));
        var k = CeilDivide(CeilDivide(bits, 1024) - numBytesInVclNalUnits, 3);
        return Math.Max(k, 0);
    }

    private static long CeilDivide(long dividend, long divisor)
    {
        var quotient = Math.DivRem(dividend, divisor, out var remainder);
        return remainder > 0 ? quotient + 1 : quotient;
    }

    // Clause 9.3.5.4 "Renormalization process in the arithmetic encoding engine": the range is doubled until it is at
    // least 256, and ivlLow with it
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Renormalize(int shift)
    {
        _range <<= shift;
        _low <<= shift;
        _lowBits += shift;
        WriteCompleteBytes();
    }

    // An addition to ivlLow that overflows the bits that are held changes the bits that are written: PutBit( 1 ) followed
    // by the outstanding bits as zeros, in the specification
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PropagateCarry()
    {
        if ((_low >> _lowBits) is 0)
            return;

        var output = _output!;
        output.PropagateCarry(output.Length - 1);
        _low &= (1UL << _lowBits) - 1;
    }

    // The bits above the 10 low bits of ivlLow are bits of the codeword
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteCompleteBytes()
    {
        while (_lowBits >= RegisterBits + 8)
        {
            _lowBits -= 8;
            _output!.Write((byte)(_low >> _lowBits));
            _low &= (1UL << _lowBits) - 1;
        }
    }

    // Figure 9-15 "Flowchart of flushing at termination": the range becomes 2, which the renormalization doubles 7 times,
    // then the bits 9 and 8 of ivlLow are written, followed by a bit equal to 1
    private void Flush()
    {
        var output = _output!;
        var value = (((_low << 7) >> 8) << 1) | 1;
        var bits = _lowBits + 7 - 8 + 1;

        // The bits up to the next byte are zero
        var padding = (8 - (bits & 7)) & 7;
        value <<= padding;
        bits += padding;
        while (bits > 0)
        {
            bits -= 8;
            output.Write((byte)(value >> bits));
        }

        _output = null;
    }
}
