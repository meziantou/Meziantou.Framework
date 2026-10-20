using System.Diagnostics;
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

    /// <summary>Decodes a bin with a context variable (clause 9.3.4.3.2 "Arithmetic decoding process for a binary decision").</summary>
    /// <param name="contexts">The context variables, in the layout of <see cref="HevcContextLayout"/>.</param>
    /// <param name="ctxIdx">The index of the context variable.</param>
    /// <param name="source">The decoder that reads the data.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeDecision(Span<byte> contexts, int ctxIdx, HevcCabacDecoder source)
    {
        ref var context = ref contexts[ctxIdx];
        var state = (int)context;
        var pStateIdx = state >> 1;
        var valMps = state & 1;
        var qRangeIdx = (int)(Range >> 6) & 3;
        var ivlLpsRange = (uint)HevcCabacTables.RangeTabLps[(pStateIdx << 2) + qRangeIdx];
        Range -= ivlLpsRange;
        var scaledRange = (ulong)Range << ExtraBits;
        int binVal;
        if (Offset < scaledRange)
        {
            // Clause 9.3.4.3.2.2 "State transition process", then the renormalization: the range is at least 256 - 2 = 254
            context = (byte)((HevcCabacTables.TransIdxMps[pStateIdx] << 1) | valMps);
            if (Range >= 256)
                return valMps;

            binVal = valMps;
            Range <<= 1;
            ExtraBits--;
        }
        else
        {
            Offset -= scaledRange;
            Range = ivlLpsRange;
            binVal = 1 - valMps;
            if (pStateIdx is 0)
            {
                valMps = 1 - valMps;
            }

            context = (byte)((HevcCabacTables.TransIdxLps[pStateIdx] << 1) | valMps);

            // Clause 9.3.4.3.3 "Renormalization process in the arithmetic decoding engine": the range is at least 2
            var shift = int.LeadingZeroCount((int)Range) - 23;
            Range <<= shift;
            ExtraBits -= shift;
        }

        // A single call, which keeps the method small where it is inlined
        if (ExtraBits < 0)
        {
            (Offset, ExtraBits) = source.Refill(Offset, ExtraBits);
        }

        return binVal;
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
