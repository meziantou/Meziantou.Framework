using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

[StructLayout(LayoutKind.Auto)]
public readonly struct CountingBloomFilterSize
{
    private CountingBloomFilterSize(long counterCount, int hashCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(counterCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hashCount);

        CounterCount = counterCount;
        HashCount = hashCount;
    }

    public long CounterCount { get; }
    public int HashCount { get; }

    public static CountingBloomFilterSize CreateOptimalSize(long expectedItemCount, double falsePositiveProbability)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedItemCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(falsePositiveProbability);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(falsePositiveProbability, 1);

        const double Ln2 = 0.6931471805599453d; // Math.Log(2)
        var exactCounterCount = Math.Ceiling(-expectedItemCount * Math.Log(falsePositiveProbability) / (Ln2 * Ln2));

        // A double-to-long conversion saturates rather than overflowing, so without this check an
        // oversized request silently returns long.MaxValue and, because the hash count is derived from
        // it, a hash count of 1 instead of the correct value.
        if (exactCounterCount >= (double)long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expectedItemCount), expectedItemCount, "The optimal size for these parameters exceeds the maximum supported size. Lower expectedItemCount or raise falsePositiveProbability.");

        var counterCount = (long)exactCounterCount;
        var hashCount = (int)Math.Ceiling(exactCounterCount / expectedItemCount * Ln2);

        return new CountingBloomFilterSize(counterCount, hashCount);
    }

    public static CountingBloomFilterSize CreateExact(long counterCount, int hashCount) => new(counterCount, hashCount);
}
