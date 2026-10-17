using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The adaptation of a cumulative distribution after one of its symbols is coded (AV1 specification, section 8.2.6
/// "Symbol decoding process"). The symbol decoder and the symbol encoder adapt their distributions with the same method, so
/// that both sides keep the same probabilities.
/// </summary>
internal static class Av1Cdf
{
    /// <summary>
    /// Adapts <paramref name="cdf"/>, the cumulative values of N symbols followed by the adaptation counter, to the coding of
    /// <paramref name="symbol"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Update(Span<ushort> cdf, int symbol)
    {
        var n = cdf.Length - 1;
        var counter = cdf[n];
        var rate = 3 + (counter > 15 ? 1 : 0) + (counter > 31 ? 1 : 0) + Math.Min(BitOperations.Log2((uint)n), 2);
        for (var i = 0; i < n - 1; i++)
        {
            // tmp is 0 before the coded symbol and 32768 from it
            if (i < symbol)
            {
                cdf[i] -= (ushort)(cdf[i] >> rate);
            }
            else
            {
                cdf[i] += (ushort)(((1 << 15) - cdf[i]) >> rate);
            }
        }

        if (counter < 32)
            cdf[n] = (ushort)(counter + 1);
    }
}
