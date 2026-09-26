using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

[StructLayout(LayoutKind.Auto)]
public readonly struct BloomFilterSize
{
    private const double Ln2 = 0.6931471805599453d; // Math.Log(2)

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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedItemCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(falsePositiveProbability);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(falsePositiveProbability, 1);

        var exactBitCount = Math.Ceiling(-expectedItemCount * Math.Log(falsePositiveProbability) / (Ln2 * Ln2));

        // A double-to-long conversion saturates rather than overflowing, so without this check an
        // oversized request silently returns long.MaxValue and, because the hash count is derived from
        // it, a hash count of 1 instead of the correct value.
        if (exactBitCount >= (double)long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expectedItemCount), expectedItemCount, "The optimal size for these parameters exceeds the maximum supported size. Lower expectedItemCount or raise falsePositiveProbability.");

        var bitCount = (long)exactBitCount;
        var hashCount = GetOptimalHashCount(exactBitCount / expectedItemCount);

        return new BloomFilterSize(bitCount, hashCount);
    }

    public static BloomFilterSize CreateExact(long bitCount, int hashCount) => new(bitCount, hashCount);

    // The optimal hash count (m / n) ln 2 is rarely an integer, and rounding it up is not always best: at a 10%
    // target it is 3.32, and 4 hashes cost an extra probe per operation for a higher false positive rate than 3.
    // Keep whichever neighbor has the lower false positive rate, and the smaller one on a tie.
    internal static int GetOptimalHashCount(double bitsPerItem)
    {
        var exactHashCount = bitsPerItem * Ln2;
        var lower = Math.Max(1, (int)Math.Floor(exactHashCount));
        var upper = Math.Max(1, (int)Math.Ceiling(exactHashCount));

        return GetFalsePositiveProbability(bitsPerItem, upper) < GetFalsePositiveProbability(bitsPerItem, lower) ? upper : lower;

        static double GetFalsePositiveProbability(double bitsPerItem, int hashCount) => Math.Pow(1 - Math.Exp(-hashCount / bitsPerItem), hashCount);
    }
}
