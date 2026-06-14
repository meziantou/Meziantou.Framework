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
    private protected void AddHash(Hash128 hash)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash.Hash1;
        for (var i = 0; i < HashCount; i++)
        {
            Counters.Increment(Reduce(combined, counterCount));
            combined += hash.Hash2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void RemoveHash(Hash128 hash)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash.Hash1;
        for (var i = 0; i < HashCount; i++)
        {
            Counters.Decrement(Reduce(combined, counterCount));
            combined += hash.Hash2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected int GetEstimatedCountHash(Hash128 hash)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash.Hash1;
        var result = int.MaxValue;
        for (var i = 0; i < HashCount; i++)
        {
            result = Math.Min(result, Counters.Get(Reduce(combined, counterCount)));
            combined += hash.Hash2;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void AddHash(Hash64 hash) => AddHashCore(hash.Hash1, hash.Hash2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void RemoveHash(Hash64 hash) => RemoveHashCore(hash.Hash1, hash.Hash2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected int GetEstimatedCountHash(Hash64 hash) => GetEstimatedCountHashCore(hash.Hash1, hash.Hash2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void AddHash(Hash32 hash) => AddHashCore32(hash.Hash1, hash.Hash2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void RemoveHash(Hash32 hash) => RemoveHashCore32(hash.Hash1, hash.Hash2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected int GetEstimatedCountHash(Hash32 hash) => GetEstimatedCountHashCore32(hash.Hash1, hash.Hash2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddHashCore(ulong hash1, ulong hash2)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash1;
        for (var i = 0; i < HashCount; i++)
        {
            Counters.Increment(Reduce(combined, counterCount));
            unchecked
            {
                combined += hash2;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveHashCore(ulong hash1, ulong hash2)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash1;
        for (var i = 0; i < HashCount; i++)
        {
            Counters.Decrement(Reduce(combined, counterCount));
            unchecked
            {
                combined += hash2;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetEstimatedCountHashCore(ulong hash1, ulong hash2)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash1;
        var result = int.MaxValue;
        for (var i = 0; i < HashCount; i++)
        {
            result = Math.Min(result, Counters.Get(Reduce(combined, counterCount)));
            unchecked
            {
                combined += hash2;
            }
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddHashCore32(uint hash1, uint hash2)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash1;
        for (var i = 0; i < HashCount; i++)
        {
            Counters.Increment(Reduce(combined, counterCount));
            unchecked
            {
                combined += hash2;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveHashCore32(uint hash1, uint hash2)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash1;
        for (var i = 0; i < HashCount; i++)
        {
            Counters.Decrement(Reduce(combined, counterCount));
            unchecked
            {
                combined += hash2;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetEstimatedCountHashCore32(uint hash1, uint hash2)
    {
        var counterCount = (ulong)Counters.CounterCount;
        var combined = hash1;
        var result = int.MaxValue;
        for (var i = 0; i < HashCount; i++)
        {
            result = Math.Min(result, Counters.Get(Reduce(combined, counterCount)));
            unchecked
            {
                combined += hash2;
            }
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Reduce(ulong hash, ulong range) => (long)(((UInt128)hash * range) >> 64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Reduce(uint hash, ulong range) => (long)(((UInt128)hash * range) >> 32);
}
