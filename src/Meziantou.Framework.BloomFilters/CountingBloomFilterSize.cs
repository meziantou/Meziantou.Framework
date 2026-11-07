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
        var (counterCount, hashCount) = BloomFilterSize.GetOptimalSize(expectedItemCount, falsePositiveProbability);
        return new CountingBloomFilterSize(counterCount, hashCount);
    }

    public static CountingBloomFilterSize CreateExact(long counterCount, int hashCount) => new(counterCount, hashCount);
}
