using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public closed partial class CountingBloomFilter
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
        // The fields are read once: see SegmentedCounterStorage.SingleSegment
        var counters = Counters;
        var singleSegment = counters.SingleSegment;
        var counterCount = (ulong)counters.CounterCount;
        var hashCount = HashCount;
        var step = hash.Hash2;
        var combined = hash.Hash1;
        for (var i = 0; i < hashCount; i++)
        {
            SegmentedCounterStorage.Increment(ref counters.GetCounterReference(singleSegment, BloomFilterHash.Reduce(combined, counterCount)));
            combined += step;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void RemoveHash(BloomFilterHash hash)
    {
        // A value with an empty counter is certainly not in the filter. Decrementing its other counters would take
        // them away from the values that set them and turn those values into false negatives.
        if (!MayContainHash(hash))
            return;

        var counters = Counters;
        var singleSegment = counters.SingleSegment;
        var counterCount = (ulong)counters.CounterCount;
        var hashCount = HashCount;
        var step = hash.Hash2;
        var combined = hash.Hash1;
        for (var i = 0; i < hashCount; i++)
        {
            SegmentedCounterStorage.Decrement(ref counters.GetCounterReference(singleSegment, BloomFilterHash.Reduce(combined, counterCount)));
            combined += step;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected int GetEstimatedCountHash(BloomFilterHash hash)
    {
        var counters = Counters;
        var singleSegment = counters.SingleSegment;
        var counterCount = (ulong)counters.CounterCount;
        var hashCount = HashCount;
        var step = hash.Hash2;
        var combined = hash.Hash1;
        var result = int.MaxValue;
        for (var i = 0; i < hashCount; i++)
        {
            // Counters are never negative, so once a counter is zero the minimum is zero
            var counter = SegmentedCounterStorage.Get(ref counters.GetCounterReference(singleSegment, BloomFilterHash.Reduce(combined, counterCount)));
            if (counter == 0)
                return 0;

            result = Math.Min(result, counter);
            combined += step;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool MayContainHash(BloomFilterHash hash)
    {
        var counters = Counters;
        var singleSegment = counters.SingleSegment;
        var counterCount = (ulong)counters.CounterCount;
        var hashCount = HashCount;
        var step = hash.Hash2;
        var combined = hash.Hash1;
        for (var i = 0; i < hashCount; i++)
        {
            if (SegmentedCounterStorage.Get(ref counters.GetCounterReference(singleSegment, BloomFilterHash.Reduce(combined, counterCount))) == 0)
                return false;

            combined += step;
        }

        return true;
    }
}
