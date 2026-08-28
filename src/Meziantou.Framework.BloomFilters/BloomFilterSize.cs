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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedItemCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(falsePositiveProbability);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(falsePositiveProbability, 1);

        const double Ln2 = 0.6931471805599453d; // Math.Log(2)
        var exactBitCount = Math.Ceiling(-expectedItemCount * Math.Log(falsePositiveProbability) / (Ln2 * Ln2));

        // A double-to-long conversion saturates rather than overflowing, so without this check an
        // oversized request silently returns long.MaxValue and, because the hash count is derived from
        // it, a hash count of 1 instead of the correct value.
        if (exactBitCount >= (double)long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expectedItemCount), expectedItemCount, "The optimal size for these parameters exceeds the maximum supported size. Lower expectedItemCount or raise falsePositiveProbability.");

        var bitCount = (long)exactBitCount;
        var hashCount = (int)Math.Ceiling(exactBitCount / expectedItemCount * Ln2);

        return new BloomFilterSize(bitCount, hashCount);
    }

    public static BloomFilterSize CreateExact(long bitCount, int hashCount) => new(bitCount, hashCount);
}
