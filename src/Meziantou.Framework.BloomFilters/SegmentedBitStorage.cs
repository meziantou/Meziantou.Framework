using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

internal sealed class SegmentedBitStorage
{
    private const int SegmentShift = 20;
    private const int WordsPerSegmentValue = 1 << SegmentShift;

    private readonly ulong[][] _segments;
    private readonly int _wordsPerSegment;
    private readonly int _segmentMask;

    public SegmentedBitStorage(long bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitCount);

        BitCount = bitCount;
        WordCount = checked(((bitCount - 1) / 64) + 1);
        MemoryBytes = checked(WordCount * sizeof(ulong));
        _wordsPerSegment = WordsPerSegmentValue;
        _segmentMask = _wordsPerSegment - 1;

        var segmentCount = checked((int)((WordCount + _wordsPerSegment - 1) / _wordsPerSegment));
        _segments = new ulong[segmentCount][];

        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var remainingWords = WordCount - ((long)segmentIndex * _wordsPerSegment);
            var currentSegmentLength = (int)Math.Min(_wordsPerSegment, remainingWords);
            _segments[segmentIndex] = new ulong[currentSegmentLength];
        }
    }

    public long BitCount { get; }
    public long WordCount { get; }
    public long MemoryBytes { get; }
    public int SegmentCount => _segments.Length;
    public int WordsPerSegment => _wordsPerSegment;

    public void Clear()
    {
        foreach (var segment in _segments)
        {
            Array.Clear(segment);
        }
    }

    public void Set(long bitIndex)
    {
        ValidateBitIndex(bitIndex);

        var wordIndex = bitIndex >> 6;
        var bitOffset = (int)(bitIndex & 63);
        var mask = 1UL << bitOffset;
        var segmentIndex = (int)(wordIndex >> SegmentShift);
        var offset = (int)(wordIndex & _segmentMask);

        var segment = _segments[segmentIndex];
        ref var baseRef = ref MemoryMarshal.GetArrayDataReference(segment);
        ref var wordRef = ref Unsafe.Add(ref baseRef, (nint)offset);
        Interlocked.Or(ref wordRef, mask);
    }

    public bool IsSet(long bitIndex)
    {
        ValidateBitIndex(bitIndex);

        var wordIndex = bitIndex >> 6;
        var bitOffset = (int)(bitIndex & 63);
        var mask = 1UL << bitOffset;
        var segmentIndex = (int)(wordIndex >> SegmentShift);
        var offset = (int)(wordIndex & _segmentMask);

        var segment = _segments[segmentIndex];
        ref var baseRef = ref MemoryMarshal.GetArrayDataReference(segment);
        ref var wordRef = ref Unsafe.Add(ref baseRef, (nint)offset);
        var word = Volatile.Read(ref wordRef);
        return (word & mask) != 0;
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
