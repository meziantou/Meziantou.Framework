using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

internal sealed class SegmentedBitStorage
{
    private const int SegmentShift = 30;
    private const int WordsPerSegment = 1 << SegmentShift;
    private const int SegmentMask = WordsPerSegment - 1;

    private readonly ulong[][] _segments;

    public SegmentedBitStorage(long bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitCount);

        BitCount = bitCount;
        var wordCount = checked(((bitCount - 1) / 64) + 1);

        var segmentCount = checked((int)((wordCount + WordsPerSegment - 1) / WordsPerSegment));
        _segments = new ulong[segmentCount][];

        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var remainingWords = wordCount - ((long)segmentIndex * WordsPerSegment);
            var currentSegmentLength = (int)Math.Min(WordsPerSegment, remainingWords);
            _segments[segmentIndex] = new ulong[currentSegmentLength];
        }
    }

    public long BitCount { get; }

    public long CountSetBits()
    {
        long count = 0;
        foreach (var segment in _segments)
        {
            for (var index = 0; index < segment.Length; index++)
            {
                count += BitOperations.PopCount(Volatile.Read(ref segment[index]));
            }
        }

        return count;
    }

    // The masked index is in range by construction, so the reference is taken without a bounds check:
    // replacing this with segment[(int)(wordIndex & SegmentMask)] measured ~5% slower on
    // LargeBloomFilterBenchmark.MayContain, and the JIT cannot elide the check because the last segment is
    // shorter than SegmentMask + 1.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(long bitIndex)
    {
        ValidateBitIndex(bitIndex);

        var wordIndex = bitIndex >> 6;
        var segment = _segments[(int)(wordIndex >> SegmentShift)];
        ref var baseRef = ref unsafe(MemoryMarshal.GetArrayDataReference(segment));
        ref var wordRef = ref unsafe(Unsafe.Add(ref baseRef, (nint)(wordIndex & SegmentMask)));
        Interlocked.Or(ref wordRef, 1UL << (int)bitIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSet(long bitIndex)
    {
        ValidateBitIndex(bitIndex);

        var wordIndex = bitIndex >> 6;
        var segment = _segments[(int)(wordIndex >> SegmentShift)];
        ref var baseRef = ref unsafe(MemoryMarshal.GetArrayDataReference(segment));
        ref var wordRef = ref unsafe(Unsafe.Add(ref baseRef, (nint)(wordIndex & SegmentMask)));
        return (Volatile.Read(ref wordRef) & (1UL << (int)bitIndex)) != 0;
    }

    /// <summary>
    /// Sets in this storage every bit that is set in <paramref name="other"/>, which must have the same bit count.
    /// </summary>
    public void UnionWith(SegmentedBitStorage other)
    {
        Debug.Assert(other.BitCount == BitCount);
        UnionWith(_segments, other._segments);
    }

    // Takes the segments rather than the storage so that tests can cover several segments without allocating 8 GiB.
    internal static void UnionWith(ulong[][] destination, ulong[][] source)
    {
        Debug.Assert(destination.Length == source.Length);
        for (var segmentIndex = 0; segmentIndex < destination.Length; segmentIndex++)
        {
            var destinationSegment = destination[segmentIndex].AsSpan();
            var sourceSegment = source[segmentIndex].AsSpan(0, destinationSegment.Length);
            for (var index = 0; index < destinationSegment.Length; index++)
            {
                var sourceWord = Volatile.Read(ref sourceSegment[index]);
                ref var destinationWord = ref destinationSegment[index];

                // Skipping the words that would not change avoids an atomic operation, and a cache line write, for most
                // words of a filter that already contains most of the source.
                if ((sourceWord & ~Volatile.Read(ref destinationWord)) != 0)
                {
                    Interlocked.Or(ref destinationWord, sourceWord);
                }
            }
        }
    }

    [Conditional("DEBUG")]
    private void ValidateBitIndex(long bitIndex)
    {
        if ((ulong)bitIndex >= (ulong)BitCount)
        {
            ThrowInvalidBitIndex(bitIndex, BitCount);
        }

        [DoesNotReturn]
        static void ThrowInvalidBitIndex(long bitIndex, long bitCount) => throw new ArgumentOutOfRangeException(nameof(bitIndex), $"Bit index must be between 0 and {bitCount - 1} (inclusive). Actual value: {bitIndex}");
    }
}
