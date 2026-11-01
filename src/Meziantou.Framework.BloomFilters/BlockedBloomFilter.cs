using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

/// <summary>
/// A Bloom filter that stores the bits of each value in a single 64-byte block (one cache line).
/// </summary>
/// <remarks>
/// A <see cref="BloomFilter"/> spreads the bits of a value over the whole filter, so adding or finding a value in a
/// filter larger than the CPU caches reads <c>HashCount</c> unrelated cache lines. A blocked filter chooses one block
/// from the hash and sets every bit of the value inside it: one cache line per operation. Blocks do not fill evenly,
/// so a blocked filter needs a little more memory for the same false positive probability; size it with
/// <see cref="BlockedBloomFilterSize"/>.
/// </remarks>
public closed partial class BlockedBloomFilter
{
    private const int WordsPerBlock = BlockedBloomFilterSize.BitsPerBlock / 64;
    private const int BlockSize = WordsPerBlock * sizeof(ulong);
    private const int SegmentBlockShift = 24; // 2^24 blocks of 64 bytes: 1 GiB per segment
    private const long SegmentBlockMask = (1L << SegmentBlockShift) - 1;
    private const ulong LcgMultiplier = 6364136223846793005UL;
    private const ulong LcgIncrement = 1442695040888963407UL;

    private readonly Segment[] _segments;
    private readonly ulong _blockCount;
    private readonly int _hashCount;

    private protected BlockedBloomFilter(long blockCount, int hashCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hashCount);

        _blockCount = (ulong)blockCount;
        _hashCount = hashCount;

        var segmentCount = checked((int)(((blockCount - 1) >> SegmentBlockShift) + 1));
        _segments = new Segment[segmentCount];
        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var segmentBlockCount = (int)Math.Min(1L << SegmentBlockShift, blockCount - ((long)segmentIndex << SegmentBlockShift));

            // A pinned array never moves, so the offset of its first 64-byte boundary stays valid. The extra words
            // make room for that offset, so no block straddles two cache lines.
            var words = GC.AllocateArray<ulong>((segmentBlockCount * WordsPerBlock) + WordsPerBlock - 1, pinned: true);
            var address = unsafe((nuint)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(words)));
            var offset = (int)((BlockSize - (address % BlockSize)) % BlockSize / sizeof(ulong));
            _segments[segmentIndex] = new Segment(words, offset, segmentBlockCount);
        }
    }

    public double GetEstimateCount()
    {
        long setBitCount = 0;
        foreach (var segment in _segments)
        {
            var words = segment.Words.AsSpan(segment.Offset, segment.BlockCount * WordsPerBlock);
            for (var index = 0; index < words.Length; index++)
            {
                setBitCount += BitOperations.PopCount(Volatile.Read(ref words[index]));
            }
        }

        var bitCount = (double)_blockCount * BlockedBloomFilterSize.BitsPerBlock;
        if (setBitCount == bitCount)
            return double.PositiveInfinity;

        return -bitCount / _hashCount * Math.Log(1 - (setBitCount / bitCount));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void AddHash(BloomFilterHash hash)
    {
        ref var block = ref GetBlock(hash.Hash1);
        var seed = hash.Hash2;
        for (var i = 0; i < _hashCount; i++)
        {
            var bit = (int)(seed >> 55);
            ref var word = ref unsafe(Unsafe.Add(ref block, bit >> 6));
            Interlocked.Or(ref word, 1UL << bit);
            seed = (seed * LcgMultiplier) + LcgIncrement;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool MayContainHash(BloomFilterHash hash)
    {
        ref var block = ref GetBlock(hash.Hash1);
        var seed = hash.Hash2;
        for (var i = 0; i < _hashCount; i++)
        {
            var bit = (int)(seed >> 55);
            ref var word = ref unsafe(Unsafe.Add(ref block, bit >> 6));
            if ((Volatile.Read(ref word) & (1UL << bit)) == 0)
                return false;

            seed = (seed * LcgMultiplier) + LcgIncrement;
        }

        return true;
    }

    // The block index comes from Hash1 and the positions inside the block from Hash2 (9 bits per position, drawn
    // from a 64-bit LCG), so the choice of the block and the positions inside it are independent.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref ulong GetBlock(ulong hash)
    {
        var blockIndex = BloomFilterHash.Reduce(hash, _blockCount);
        var segment = _segments[(int)(blockIndex >> SegmentBlockShift)];
        ref var baseRef = ref unsafe(MemoryMarshal.GetArrayDataReference(segment.Words));
        return ref unsafe(Unsafe.Add(ref baseRef, segment.Offset + ((nint)(blockIndex & SegmentBlockMask) * WordsPerBlock)));
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Segment(ulong[] Words, int Offset, int BlockCount);
}
