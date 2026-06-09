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
        var bitCount = (long)Math.Ceiling(-expectedItemCount * Math.Log(falsePositiveProbability) / (Ln2 * Ln2));
        var hashCount = (int)Math.Ceiling((double)bitCount / expectedItemCount * Ln2);

        return new BloomFilterSize(bitCount, hashCount);
    }

    public static BloomFilterSize CreateExact(long bitCount, int hashCount) => new(bitCount, hashCount);
}
