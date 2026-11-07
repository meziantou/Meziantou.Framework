using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

[StructLayout(LayoutKind.Auto)]
public readonly struct BloomFilterSize
{
    private BloomFilterSize(long bitCount, int hashCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hashCount);

        BitCount = bitCount;
        HashCount = hashCount;
    }

    public long BitCount { get; }
    public int HashCount { get; }

    public static BloomFilterSize CreateOptimalSize(long expectedItemCount, double falsePositiveProbability)
    {
        var (bitCount, hashCount) = GetOptimalSize(expectedItemCount, falsePositiveProbability);
        return new BloomFilterSize(bitCount, hashCount);
    }

    public static BloomFilterSize CreateExact(long bitCount, int hashCount) => new(bitCount, hashCount);

    // The continuous optimum, (m / n) = -ln(p) / ln(2)^2 with k = (m / n) ln 2, is rarely an integer number of hash
    // functions. Rounding k without growing m misses the target: at p = 0.9 the optimum is k = 0.15, and 1 hash with
    // the optimal 220 bits per 1000 items gives p = 0.99. Instead, for each integer k, compute the smallest m whose
    // false positive probability (1 - e^(-kn/m))^k is at most p, and keep the smallest m (the smaller k on a tie).
    internal static (long PositionCount, int HashCount) GetOptimalSize(long expectedItemCount, double falsePositiveProbability)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedItemCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(falsePositiveProbability);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(falsePositiveProbability, 1);

        // The positions per item decrease with k up to the continuous optimum -log2(p), then increase, so the best
        // integer k is next to it. The search also covers one more value on each side to absorb rounding errors.
        var maxHashCount = (int)Math.Max(1, Math.Ceiling(-Math.Log2(falsePositiveProbability))) + 1;
        var logProbability = Math.Log(falsePositiveProbability);

        var bestPositionCount = double.PositiveInfinity;
        var bestHashCount = 0;
        for (var hashCount = Math.Max(1, maxHashCount - 3); hashCount <= maxHashCount; hashCount++)
        {
            // Solve (1 - e^(-kn/m))^k = p for m: m = -kn / ln(1 - p^(1/k))
            var positionCount = Math.Ceiling(-hashCount * (double)expectedItemCount / Math.Log(1 - Math.Exp(logProbability / hashCount)));
            if (positionCount < bestPositionCount)
            {
                bestPositionCount = positionCount;
                bestHashCount = hashCount;
            }
        }

        // A double-to-long conversion saturates rather than overflowing, so without this check an
        // oversized request would silently return long.MaxValue.
        if (bestPositionCount >= (double)long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expectedItemCount), expectedItemCount, "The optimal size for these parameters exceeds the maximum supported size. Lower expectedItemCount or raise falsePositiveProbability.");

        return ((long)bestPositionCount, bestHashCount);
    }
}
