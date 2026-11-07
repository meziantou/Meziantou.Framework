using System.Diagnostics;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The estimated number of bits of the bins of the slice segment data, in units of 1/256 bit (<see cref="Scale"/>): what
/// the mode decision compares candidates with, not the exact size of the output.
/// </summary>
/// <remarks>
/// <para>
/// A bin coded with a context variable divides the interval of the arithmetic encoder, of range <c>ivlCurrRange</c>, into
/// the interval of the least probable symbol, of range <c>rangeTabLps[ pStateIdx ][ qRangeIdx ]</c>, and the rest (clause
/// 9.3.5.3). The cost of a bin is the binary logarithm of the ratio between the range and the range of its interval. The
/// cost of a state is the mean of this cost over the 256 values that the range has after a renormalization, 256 to 511:
/// it is derived from the table of the decoder (<see cref="HevcCabacTables.RangeTabLps"/>), not written out.
/// </para>
/// <para>
/// The logarithms are computed with integers, so that the decisions of the encoder, and therefore its output, do not
/// depend on the floating-point library of the platform.
/// </para>
/// </remarks>
internal static class HevcCost
{
    /// <summary>The number of rate units of one bit.</summary>
    public const int Scale = 256;

    // The fractional bits of the logarithms that the costs are sums of
    private const int LogBits = 16;

    // The bits that the flushing of a codeword writes after the ones that tell its interval (Figure 9-15)
    private const int FlushBits = 2;

    // BinCosts[ ( context << 1 ) | binVal ], a context being ( pStateIdx << 1 ) | valMps
    private static readonly ushort[] BinCosts = CreateBinCosts();
    private static readonly int[] TerminateCosts = CreateTerminateCosts();

    /// <summary>Returns the cost of the bin <paramref name="binVal"/> coded with a context variable in the state <paramref name="context"/>.</summary>
    public static int GetBinCost(byte context, int binVal)
    {
        Debug.Assert(binVal is 0 or 1 && context < 128);
        return BinCosts[(context << 1) | binVal];
    }

    /// <summary>Returns the cost of <paramref name="count"/> bins coded in bypass mode: one bit each.</summary>
    public static int GetBypassCost(int count) => count * Scale;

    /// <summary>
    /// Returns the cost of a bin coded before termination (clause 9.3.5.6): <c>end_of_slice_segment_flag</c>,
    /// <c>end_of_subset_one_bit</c> and <c>pcm_flag</c>. The cost of a bin equal to 1 includes the bits that the flushing
    /// of the codeword adds, and not the alignment bits that follow them.
    /// </summary>
    public static int GetTerminateCost(int binVal)
    {
        Debug.Assert(binVal is 0 or 1);
        return TerminateCosts[binVal];
    }

    /// <summary>Returns the cost of the <c>ue(v)</c> form of <paramref name="value"/> (clause 9.2).</summary>
    public static int GetUECost(uint value) => HevcBitWriter.GetUELength(value) * Scale;

    /// <summary>Returns the cost of the <c>se(v)</c> form of <paramref name="value"/> (clause 9.2).</summary>
    public static int GetSECost(int value) => HevcBitWriter.GetSELength(value) * Scale;

    /// <summary>
    /// Returns the number of bins of the k-th order Exp-Golomb binarization of <paramref name="value"/> (clause 9.3.3.3):
    /// a unary prefix that doubles the range of the values at each bin, a bin equal to 0, then a suffix.
    /// </summary>
    public static int GetExpGolombLength(uint value, int k)
    {
        Debug.Assert(k is >= 0 and < 31 && value < 1u << 31);
        var length = 0;
        while (value >= 1u << k)
        {
            length++;
            value -= 1u << k;
            k++;
        }

        return length + 1 + k;
    }

    /// <summary>Returns the cost of the k-th order Exp-Golomb binarization of <paramref name="value"/>, whose bins are coded in bypass mode.</summary>
    public static int GetExpGolombCost(uint value, int k) => GetExpGolombLength(value, k) * Scale;

    private static ushort[] CreateBinCosts()
    {
        var costs = new ushort[256];
        var rangeTabLps = HevcCabacTables.RangeTabLps;
        for (var pStateIdx = 0; pStateIdx < 64; pStateIdx++)
        {
            long mps = 0;
            long lps = 0;
            for (var range = 256u; range < 512; range++)
            {
                // Equation 9-64: qRangeIdx = ( ivlCurrRange >> 6 ) & 3
                var ivlLpsRange = (uint)rangeTabLps[(pStateIdx << 2) + (int)((range >> 6) & 3)];
                var log = Log2(range);
                mps += log - Log2(range - ivlLpsRange);
                lps += log - Log2(ivlLpsRange);
            }

            // The mean of 256 costs in bits is their sum in units of 1/256 bit
            for (var valMps = 0; valMps < 2; valMps++)
            {
                var context = (pStateIdx << 1) | valMps;
                costs[(context << 1) | valMps] = ToCost(mps);
                costs[(context << 1) | (1 - valMps)] = ToCost(lps);
            }
        }

        return costs;
    }

    private static int[] CreateTerminateCosts()
    {
        long zero = 0;
        long one = 0;
        for (var range = 256u; range < 512; range++)
        {
            // The interval of a bin equal to 1 has a range of 2
            var log = Log2(range);
            zero += log - Log2(range - 2);
            one += log - Log2(2);
        }

        return [ToCost(zero), ToCost(one) + (FlushBits * Scale)];
    }

    private static ushort ToCost(long sum) => (ushort)((sum + (1L << (LogBits - 1))) >> LogBits);

    // The binary logarithm of a positive integer, with LogBits fractional bits: the exponent, then the logarithm of the
    // mantissa in [1, 2) one bit at a time, since squaring a number doubles its logarithm
    private static long Log2(uint value)
    {
        var exponent = BitOperations.Log2(value);
        var x = (ulong)value << (32 - exponent);
        long log = exponent;
        for (var bit = 0; bit < LogBits; bit++)
        {
            x = (ulong)(((UInt128)x * x) >> 32);
            log <<= 1;
            if (x >= 2UL << 32)
            {
                log |= 1;
                x >>= 1;
            }
        }

        return log;
    }
}
