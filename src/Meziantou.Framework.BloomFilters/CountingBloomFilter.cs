using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public abstract partial class CountingBloomFilter
{
    private protected readonly SegmentedCounterStorage Counters;
    private protected readonly int HashCount;

    private protected CountingBloomFilter(long counterCount, int hashCount)
    {
        Counters = new SegmentedCounterStorage(counterCount);
        HashCount = hashCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void AddHash(BloomFilterHash hash)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash.Hash1;
        for (var i = 0; i < HashCount; i++)
        {
            Counters.Increment(BloomFilterHash.Reduce(combined, counterCount));
            combined += hash.Hash2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void RemoveHash(BloomFilterHash hash)
    {
        // A value with an empty counter is certainly not in the filter. Decrementing its other counters would take
        // them away from the values that set them and turn those values into false negatives.
        if (!MayContainHash(hash))
            return;

        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash.Hash1;
        for (var i = 0; i < HashCount; i++)
        {
            Counters.Decrement(BloomFilterHash.Reduce(combined, counterCount));
            combined += hash.Hash2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected int GetEstimatedCountHash(BloomFilterHash hash)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash.Hash1;
        var result = int.MaxValue;
        for (var i = 0; i < HashCount; i++)
        {
            result = Math.Min(result, Counters.Get(BloomFilterHash.Reduce(combined, counterCount)));
            combined += hash.Hash2;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool MayContainHash(BloomFilterHash hash)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash.Hash1;
        for (var i = 0; i < HashCount; i++)
        {
            if (Counters.Get(BloomFilterHash.Reduce(combined, counterCount)) == 0)
                return false;

            combined += hash.Hash2;
        }

        return true;
    }
}
