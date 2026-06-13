using System.Diagnostics;
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

    public void Clear()
    {
        foreach (var segment in _segments)
        {
            Array.Clear(segment);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(long bitIndex)
    {
        ValidateBitIndex(bitIndex);

        var wordIndex = bitIndex >> 6;
        var segment = _segments[(int)(wordIndex >> SegmentShift)];
        ref var baseRef = ref MemoryMarshal.GetArrayDataReference(segment);
        ref var wordRef = ref Unsafe.Add(ref baseRef, (nint)(wordIndex & SegmentMask));
        Interlocked.Or(ref wordRef, 1UL << (int)bitIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSet(long bitIndex)
    {
        ValidateBitIndex(bitIndex);

        var wordIndex = bitIndex >> 6;
        var segment = _segments[(int)(wordIndex >> SegmentShift)];
        ref var baseRef = ref MemoryMarshal.GetArrayDataReference(segment);
        ref var wordRef = ref Unsafe.Add(ref baseRef, (nint)(wordIndex & SegmentMask));
        return (Volatile.Read(ref wordRef) & (1UL << (int)bitIndex)) != 0;
    }

    [Conditional("DEBUG")]
    private void ValidateBitIndex(long bitIndex)
    {
        if ((ulong)bitIndex >= (ulong)BitCount)
        {
            ThrowInvalidBitIndex(bitIndex);
        }

        [DoesNotReturn]
        static void ThrowInvalidBitIndex(long bitIndex) => throw new ArgumentOutOfRangeException(nameof(bitIndex), $"Bit index must be between 0 and {long.MaxValue} (inclusive). Actual value: {bitIndex}");
    }
}
