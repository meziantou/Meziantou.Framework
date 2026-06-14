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
        var counterCount = (long)Math.Ceiling(-expectedItemCount * Math.Log(falsePositiveProbability) / (Ln2 * Ln2));
        var hashCount = (int)Math.Ceiling((double)counterCount / expectedItemCount * Ln2);

        return new CountingBloomFilterSize(counterCount, hashCount);
    }

    public static CountingBloomFilterSize CreateExact(long counterCount, int hashCount) => new(counterCount, hashCount);
}
