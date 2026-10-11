using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The estimated number of bits of a symbol coded by the range encoder, in units of 1/256 bit (<see cref="Scale"/>): the
/// binary logarithm of the inverse of its probability. The estimate ignores the rounding of the range encoder, which is
/// what the mode decision needs: a rate to compare candidates with, not the exact size of the output.
/// </summary>
/// <remarks>
/// The logarithms are computed with integers, so that the decisions of the encoder, and therefore its output, do not
/// depend on the floating-point library of the platform.
/// </remarks>
internal static class Av1Cost
{
    /// <summary>The number of rate units of one bit.</summary>
    public const int Scale = 256;

    private const int ProbabilityBits = 15;
    private const int MantissaBits = 8;

    // The symbol decoder gives every symbol at least EC_MIN_PROB of the 32768 units of probability
    private const int MinimumProbability = Av1Constants.EcMinProb;

    // Log2Table[ i ] is log2( 1 + i / 256 ) in units of 1/256
    private static readonly byte[] Log2Table = CreateLog2Table();

    /// <summary>
    /// Returns the cost of <paramref name="symbol"/> for <paramref name="cdf"/>, the cumulative values of N symbols that end
    /// with 32768, followed by the adaptation counter.
    /// </summary>
    public static int GetSymbolCost(ReadOnlySpan<ushort> cdf, int symbol)
    {
        var probability = symbol is 0 ? cdf[0] : cdf[symbol] - cdf[symbol - 1];
        return GetCost(probability);
    }

    /// <summary>Returns the cost of a symbol whose probability is <paramref name="probability"/> / 32768.</summary>
    public static int GetCost(int probability)
    {
        var p = (uint)Math.Clamp(probability, MinimumProbability, 1 << ProbabilityBits);
        var exponent = BitOperations.Log2(p);

        // p is 2^exponent * ( 1 + index / 256 ), index truncated
        var index = (int)(((p << (ProbabilityBits - exponent)) >> (ProbabilityBits - MantissaBits)) & ((1 << MantissaBits) - 1));
        return ((ProbabilityBits - exponent) * Scale) - Log2Table[index];
    }

    /// <summary>Returns the cost of <paramref name="count"/> bits coded with equal probabilities.</summary>
    public static int GetLiteralCost(int count) => count * Scale;

    private static byte[] CreateLog2Table()
    {
        var table = new byte[1 << MantissaBits];
        for (var i = 0; i < table.Length; i++)
        {
            // The binary logarithm of x in [1, 2), one fractional bit at a time: squaring x doubles its logarithm
            var x = (ulong)((1 << MantissaBits) + i) << (32 - MantissaBits);
            var log = 0;
            for (var bit = 0; bit < 16; bit++)
            {
                x = (ulong)(((UInt128)x * x) >> 32);
                log <<= 1;
                if (x >= 2UL << 32)
                {
                    log |= 1;
                    x >>= 1;
                }
            }

            // From 16 to 8 fractional bits, rounded to nearest
            table[i] = (byte)Math.Min(byte.MaxValue, (log + 128) >> 8);
        }

        return table;
    }
}
