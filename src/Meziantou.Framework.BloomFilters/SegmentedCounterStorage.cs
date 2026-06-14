using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

internal sealed class SegmentedCounterStorage
{
    private const int SegmentShift = 30;
    private const int CountersPerSegment = 1 << SegmentShift;
    private const int SegmentMask = CountersPerSegment - 1;

    private readonly int[][] _segments;

    public SegmentedCounterStorage(long counterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(counterCount);

        CounterCount = counterCount;
        var segmentCount = checked((int)(((counterCount - 1) / CountersPerSegment) + 1));
        _segments = new int[segmentCount][];

        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var remainingCounters = counterCount - ((long)segmentIndex * CountersPerSegment);
            var currentSegmentLength = (int)Math.Min(CountersPerSegment, remainingCounters);
            _segments[segmentIndex] = new int[currentSegmentLength];
        }
    }

    public long CounterCount { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Increment(long counterIndex)
    {
        ref var counter = ref GetCounterReference(counterIndex);
        var currentValue = Volatile.Read(ref counter);
        while (currentValue < int.MaxValue)
        {
            var previousValue = Interlocked.CompareExchange(ref counter, currentValue + 1, currentValue);
            if (previousValue == currentValue)
                return;

            currentValue = previousValue;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Decrement(long counterIndex)
    {
        ref var counter = ref GetCounterReference(counterIndex);
        var currentValue = Volatile.Read(ref counter);
        while (currentValue > 0)
        {
            var previousValue = Interlocked.CompareExchange(ref counter, currentValue - 1, currentValue);
            if (previousValue == currentValue)
                return;

            currentValue = previousValue;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Get(long counterIndex)
    {
        return Volatile.Read(ref GetCounterReference(counterIndex));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref int GetCounterReference(long counterIndex)
    {
        ValidateCounterIndex(counterIndex);

        var segment = _segments[(int)(counterIndex >> SegmentShift)];
        ref var baseRef = ref MemoryMarshal.GetArrayDataReference(segment);
        return ref Unsafe.Add(ref baseRef, (nint)(counterIndex & SegmentMask));
    }

    [Conditional("DEBUG")]
    private void ValidateCounterIndex(long counterIndex)
    {
        if ((ulong)counterIndex >= (ulong)CounterCount)
        {
            ThrowInvalidCounterIndex(counterIndex);
        }

        [DoesNotReturn]
        static void ThrowInvalidCounterIndex(long counterIndex) => throw new ArgumentOutOfRangeException(nameof(counterIndex), $"Counter index must be between 0 and {long.MaxValue} (inclusive). Actual value: {counterIndex}");
    }
}
