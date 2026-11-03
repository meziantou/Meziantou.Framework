using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The registers of the arithmetic decoding engine of <see cref="HevcCabacDecoder"/>: <c>ivlCurrRange</c>, and
/// <c>ivlOffset</c> followed by the bits that the specification has not read yet (clause 9.3.4.3).
/// </summary>
/// <remarks>
/// The decoder keeps them in a field, which every bin reads and writes. A loop that decodes many bins works on a local copy
/// of <see cref="HevcCabacDecoder.Engine"/> instead, and stores it back before anything else decodes through the decoder or
/// reads its position. The JIT keeps the copy in registers in a small method only: in a method with as many locals as
/// <c>HevcSliceCoder.ResidualCoding</c>, it does not promote the struct and the copy stays in memory. The bytes of the data
/// are read by <see cref="HevcCabacDecoder.Refill"/> and <see cref="HevcCabacDecoder.ReadAhead"/>, which take and return the
/// registers by value so that the copy is not exposed by its address.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal struct HevcCabacEngine
{
    /// <summary><c>ivlCurrRange</c>.</summary>
    public uint Range;

    /// <summary><c>ivlOffset</c>, followed by the <see cref="ExtraBits"/> bits that the specification has not read yet.</summary>
    public ulong Offset;

    /// <summary>The number of bits of <see cref="Offset"/> that the specification has not read yet.</summary>
    public int ExtraBits;

    /// <summary>
    /// Decodes a bin with a context variable (clause 9.3.4.3.2 "Arithmetic decoding process for a binary decision"), with
    /// masks instead of branches: the value of many bins, such as <c>sig_coeff_flag</c>, is hard to predict, and a
    /// mispredicted branch costs more than the work of both paths.
    /// </summary>
    /// <param name="contexts">The context variables, in the layout of <see cref="HevcContextLayout"/>.</param>
    /// <param name="ctxIdx">The index of the context variable.</param>
    /// <param name="source">The decoder that reads the data.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeDecision(Span<byte> contexts, int ctxIdx, HevcCabacDecoder source)
    {
        ref var context = ref contexts[ctxIdx];
        var state = (uint)context;

        // rangeTabLps[ pStateIdx ][ qRangeIdx ]: pStateIdx is in the bits 1 to 6 of the context variable
        var ivlLpsRange = (uint)HevcCabacTables.RangeTabLps[(byte)(((state & 0x7E) << 1) | ((Range >> 6) & 3))];
        var range = Range - ivlLpsRange;
        var scaledRange = (ulong)range << ExtraBits;

        // All the bits are set when the bin is the least probable symbol: ivlOffset >= ivlCurrRange. Both values are less
        // than 2^50, so that the difference does not overflow
        var lps = (long)(scaledRange - Offset - 1) >> 63;
        Offset -= scaledRange & (ulong)lps;
        range ^= (range ^ ivlLpsRange) & (uint)lps;

        // Clause 9.3.4.3.2.2 "State transition process"
        context = HevcCabacTables.NextContextState[(byte)(state | (uint)(lps & 128))];

        // Clause 9.3.4.3.3 "Renormalization process in the arithmetic decoding engine": the range is at least 2
        var shift = BitOperations.LeadingZeroCount(range) - 23;
        Range = range << shift;
        ExtraBits -= shift;
        if (ExtraBits < 0)
        {
            (Offset, ExtraBits) = source.Refill(Offset, ExtraBits);
        }

        return (int)((state ^ (uint)lps) & 1);
    }

    /// <summary>Decodes a bin without context (clause 9.3.4.3.4 "Bypass decoding process for binary decisions").</summary>
    /// <param name="source">The decoder that reads the data.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeBypass(HevcCabacDecoder source)
    {
        if (--ExtraBits < 0)
        {
            (Offset, ExtraBits) = source.Refill(Offset, ExtraBits);
        }

        var scaledRange = (ulong)Range << ExtraBits;
        if (Offset >= scaledRange)
        {
            Offset -= scaledRange;
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Decodes <c>coeff_abs_level_remaining</c> (clause 9.3.3.11): a prefix of at most 4 bins and a suffix of
    /// <paramref name="cRiceParam"/> bins, or a prefix of 4 bins and an Exp-Golomb code of order <paramref name="cRiceParam"/> + 1.
    /// </summary>
    /// <param name="cRiceParam"><c>cRiceParam</c>.</param>
    /// <param name="source">The decoder that reads the data.</param>
    /// <exception cref="InvalidImageContentException">The value is larger than the levels of transform coefficients.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeCoeffAbsLevelRemaining(int cRiceParam, HevcCabacDecoder source)
    {
        // The prefix is a unary code: the next bins are the binary digits of a quotient (DecodeBypassBits), and the prefix is
        // their number of leading ones, without a branch on each bin. The bins after the first 0 are not read
        if (ExtraBits < 16)
        {
            (Offset, ExtraBits) = source.ReadAhead(Offset, ExtraBits);
        }

        var prefix = 0;
        var ended = false;
        var peek = Math.Min(ExtraBits, 16);
        if (peek > 0)
        {
            var shift = ExtraBits - peek;
            var bins = (uint)((Offset >> shift) / Range);
            var ones = BitOperations.LeadingZeroCount(~(bins << (32 - peek)));
            ended = ones < peek;

            // The ones and the 0 that ends them, or all the bins when they are ones
            var unread = ended ? peek - ones - 1 : 0;
            Offset -= ((bins >> unread) * (ulong)Range) << (shift + unread);
            ExtraBits = shift + unread;
            prefix = ones;
        }

        while (!ended && DecodeBypass(source) is not 0)
        {
            // The longest codes are those of the largest transform coefficient levels: a longer prefix is not a value
            if (++prefix > 32)
                throw HevcErrors.Invalid("a transform coefficient level is out of range.");
        }

        long value;
        if (prefix <= 3)
        {
            value = (prefix << cRiceParam) + DecodeBypassBits(cRiceParam, source);
        }
        else
        {
            var length = prefix - 3 + cRiceParam;
            if (length > 30)
                throw HevcErrors.Invalid("a transform coefficient level is out of range.");

            value = (((1L << (prefix - 3)) + 3 - 1) << cRiceParam) + DecodeBypassBits(length, source);
            if (value > 2 * -(long)HevcInverseTransform.CoeffMin)
                throw HevcErrors.Invalid("a transform coefficient level is out of range.");
        }

        return (int)value;
    }

    /// <summary>Decodes <paramref name="count"/> bypass bins, the first one being the most significant bit of the result.</summary>
    /// <param name="count">The number of bins.</param>
    /// <param name="source">The decoder that reads the data.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeBypassBits(int count, HevcCabacDecoder source)
    {
        Debug.Assert(count is >= 0 and <= 31);
        if (ExtraBits < count)
        {
            // The bits of the bins are read first: when the data end before them, the bins are past the end of the data
            (Offset, ExtraBits) = source.ReadAhead(Offset, ExtraBits);
            if (ExtraBits < count)
                throw HevcErrors.Truncated();
        }

        // The bins are the binary digits of ivlOffset / ivlCurrRange once the count bits are read into ivlOffset: each
        // bypass bin doubles ivlOffset, adds a bit, and subtracts ivlCurrRange when it is not smaller. ivlOffset stays below
        // ivlCurrRange, so the quotient has count bits.
        var shift = ExtraBits - count;
        var quotient = (Offset >> shift) / Range;
        Offset -= (quotient * Range) << shift;
        ExtraBits = shift;
        return (int)quotient;
    }
}
