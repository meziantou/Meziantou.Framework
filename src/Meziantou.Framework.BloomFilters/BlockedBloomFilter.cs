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
            var words = segment.GetWords();
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

    /// <summary>
    /// Adds to this filter every value represented by <paramref name="other"/>, by combining their bits with a bitwise OR.
    /// </summary>
    /// <param name="other">
    /// The filter whose values are added. It must use the same hash algorithm (the same concrete type), the same number of
    /// blocks and the same number of hash functions as this filter. It is not modified.
    /// </param>
    /// <remarks>
    /// <para>
    /// After the call, <c>MayContain</c> returns <see langword="true"/> for every value added to this filter or to
    /// <paramref name="other"/> before the call: the union never introduces a false negative. Calling the method with the
    /// filter itself does nothing.
    /// </para>
    /// <para>
    /// The result holds the values of both filters in the same number of bits. Its false positive probability is higher
    /// than the one the filters were sized for when the total number of values exceeds the expected item count. Size each
    /// filter for the total number of values when you plan to merge them.
    /// </para>
    /// <para>
    /// The compatibility checks happen before any bit is written: when they fail, this filter is left unchanged.
    /// </para>
    /// <para>
    /// The method can run while other threads call <c>Add</c> or <c>MayContain</c> on either filter. The bits of this filter
    /// are set with atomic operations, so no value added concurrently to this filter is lost. The source is read one 64-bit
    /// word at a time, not as a snapshot: a value added to <paramref name="other"/> during the call may or may not be
    /// included. Once the method has returned, every value that was in <paramref name="other"/> when it was called is
    /// included.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="other"/> uses another hash algorithm, number of blocks or number of hash functions.
    /// </exception>
    public void UnionWith(BlockedBloomFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other))
            return;

        if (other.GetType() != GetType())
            throw new ArgumentException($"Cannot combine a {GetType().Name} with a {other.GetType().Name}: the filters must use the same hash algorithm.", nameof(other));

        if (other._blockCount != _blockCount)
            throw new ArgumentException($"Cannot combine a filter of {_blockCount} blocks with a filter of {other._blockCount} blocks: the filters must have the same number of blocks.", nameof(other));

        if (other._hashCount != _hashCount)
            throw new ArgumentException($"Cannot combine a filter that uses {_hashCount} hash functions with a filter that uses {other._hashCount}: the filters must use the same number of hash functions.", nameof(other));

        // Both filters have the same number of blocks, so their segments hold the same blocks; only the alignment
        // offsets of the arrays differ.
        for (var segmentIndex = 0; segmentIndex < _segments.Length; segmentIndex++)
        {
            SegmentedBitStorage.UnionWith(_segments[segmentIndex].GetWords(), other._segments[segmentIndex].GetWords());
        }
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
    private readonly record struct Segment(ulong[] Words, int Offset, int BlockCount)
    {
        // The words of the blocks, without the alignment padding
        public Span<ulong> GetWords() => Words.AsSpan(Offset, BlockCount * WordsPerBlock);
    }

    // Lets the batch hashing of BloomFilterXXHash* add its hashes to a blocked filter. A struct, so the generic method is
    // specialized for it and AddHash is inlined.
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct HashSink(BlockedBloomFilter filter) : IBloomFilterHashSink
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddHash(BloomFilterHash hash) => filter.AddHash(hash);
    }
}
