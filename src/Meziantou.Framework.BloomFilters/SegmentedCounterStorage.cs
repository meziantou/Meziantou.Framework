using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

// Counters are bytes: a counting Bloom filter needs only a few bits per counter (4 is the classic choice), and a
// wider counter multiplies the memory footprint and the cache misses of every probe. A counter that reaches
// byte.MaxValue saturates and stays there, because decrementing it could drop below the number of values that
// really share it and turn one of them into a false negative.
internal sealed class SegmentedCounterStorage
{
    private const int SegmentShift = 30;
    private const int CountersPerSegment = 1 << SegmentShift;
    private const int SegmentMask = CountersPerSegment - 1;

    private readonly byte[][] _segments;

    public SegmentedCounterStorage(long counterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(counterCount);

        CounterCount = counterCount;
        var segmentCount = checked((int)(((counterCount - 1) / CountersPerSegment) + 1));
        _segments = new byte[segmentCount][];

        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var remainingCounters = counterCount - ((long)segmentIndex * CountersPerSegment);
            var currentSegmentLength = (int)Math.Min(CountersPerSegment, remainingCounters);
            _segments[segmentIndex] = new byte[currentSegmentLength];
        }

        SingleSegment = segmentCount == 1 ? _segments[0] : null;
    }

    public long CounterCount { get; }

    /// <summary>
    /// The counters when the storage has a single segment (fewer than 2^30 counters), otherwise <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The atomic operations are barriers, so the JIT reloads every field after them and the CPU cannot load them ahead of
    /// them. Reading this property once before a loop of operations, and passing it to
    /// <see cref="GetCounterReference(byte[], long)"/>, leaves only the counter accesses in the loop.
    /// </remarks>
    public byte[]? SingleSegment { get; }

    public ReadOnlySpan<byte[]> Segments => _segments;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Increment(ref byte counter)
    {
        var currentValue = Volatile.Read(ref counter);
        while (currentValue < byte.MaxValue)
        {
            var previousValue = Interlocked.CompareExchange(ref counter, (byte)(currentValue + 1), currentValue);
            if (previousValue == currentValue)
                return;

            currentValue = previousValue;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Decrement(ref byte counter)
    {
        var currentValue = Volatile.Read(ref counter);
        while (currentValue is > 0 and < byte.MaxValue)
        {
            var previousValue = Interlocked.CompareExchange(ref counter, (byte)(currentValue - 1), currentValue);
            if (previousValue == currentValue)
                return;

            currentValue = previousValue;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Get(ref byte counter)
    {
        return Volatile.Read(ref counter);
    }

    /// <summary>
    /// Returns the counter at <paramref name="counterIndex"/>, taken from <paramref name="singleSegment"/> when it is the
    /// <see cref="SingleSegment"/> of this storage.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetCounterReference(byte[]? singleSegment, long counterIndex)
    {
        if (singleSegment is not null)
        {
            ValidateCounterIndex(counterIndex);
            return ref unsafe(Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(singleSegment), (nint)counterIndex));
        }

        return ref GetCounterReference(counterIndex);
    }

    // The masked index is in range by construction, so the reference is taken without a bounds check.
    // See the note in SegmentedBitStorage: the safe indexer measured slower on the probe path.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref byte GetCounterReference(long counterIndex)
    {
        ValidateCounterIndex(counterIndex);

        var segment = _segments[(int)(counterIndex >> SegmentShift)];
        ref var baseRef = ref unsafe(MemoryMarshal.GetArrayDataReference(segment));
        return ref unsafe(Unsafe.Add(ref baseRef, (nint)(counterIndex & SegmentMask)));
    }

    [Conditional("DEBUG")]
    private void ValidateCounterIndex(long counterIndex)
    {
        if ((ulong)counterIndex >= (ulong)CounterCount)
        {
            ThrowInvalidCounterIndex(counterIndex, CounterCount);
        }

        [DoesNotReturn]
        static void ThrowInvalidCounterIndex(long counterIndex, long counterCount) => throw new ArgumentOutOfRangeException(nameof(counterIndex), $"Counter index must be between 0 and {counterCount - 1} (inclusive). Actual value: {counterIndex}");
    }
}
