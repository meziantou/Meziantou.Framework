using System.Buffers.Binary;
using System.Diagnostics;
using static Meziantou.Framework.BloomFilters.BloomFilterSnapshot;

namespace Meziantou.Framework.BloomFilters;

public closed partial class BlockedBloomFilter
{
    /// <summary>
    /// Writes a snapshot of the filter to a stream: its hash algorithm, its size and its bits.
    /// </summary>
    /// <param name="stream">The stream to write the snapshot to.</param>
    /// <remarks>
    /// <para>
    /// The bits are copied through a small buffer, so saving a large filter does not allocate a second copy of it.
    /// </para>
    /// <para>
    /// Stop adding values while the snapshot is written. Each bit is read atomically, but values added during the
    /// write may be partially saved: some of their bits are in the snapshot, others are not, so the restored
    /// filter may report them as absent.
    /// </para>
    /// <para>
    /// Keys are hashed from their in-memory bytes, so the snapshot records the byte order of the machine, and
    /// <see cref="Load(Stream)"/> rejects a snapshot written on a machine with another byte order.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs" ref:region="BlockedBloomFilter_Snapshot" ref:dedent="true">
    /// var filter = BlockedBloomFilter.CreateXXHash128(BlockedBloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));
    /// filter.Add("alice@example.com");
    ///
    /// using var stream = new MemoryStream();
    /// filter.Save(stream);
    ///
    /// stream.Position = 0;
    /// var restored = BlockedBloomFilter.Load&lt;BlockedBloomFilterXXHash128&gt;(stream);
    ///
    /// Assert.True(restored.MayContain("alice@example.com"));
    /// Assert.False(restored.MayContain("bob@example.com"));
    /// </code>
    /// </example>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        BloomFilterSnapshot.Write(stream, GetSnapshotHeader(), new BlockPayload(this));
    }

    /// <summary>
    /// Writes a snapshot of the filter to a stream: its hash algorithm, its size and its bits.
    /// </summary>
    /// <param name="stream">The stream to write the snapshot to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the snapshot is written.</returns>
    /// <remarks>See <see cref="Save(Stream)"/>. Stop adding values until the task completes.</remarks>
    public Task SaveAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return BloomFilterSnapshot.WriteAsync(stream, GetSnapshotHeader(), new BlockPayload(this), cancellationToken);
    }

    /// <summary>
    /// Creates a filter from a snapshot written by <see cref="Save(Stream)"/>.
    /// </summary>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <returns>A new filter of the hash algorithm, size and content of the snapshot, such as a <see cref="BlockedBloomFilterXXHash128"/>.</returns>
    /// <exception cref="InvalidDataException">The data is not a blocked Bloom filter snapshot, is truncated or corrupted, uses an unsupported version, or was written on a machine with another byte order.</exception>
    /// <remarks>
    /// The header is validated before the filter is allocated. When the stream is seekable, its length is also
    /// checked against the size of the filter first. The snapshot ends with a checksum, verified once it is read.
    /// </remarks>
    public static BlockedBloomFilter Load(Stream stream) => Load<BlockedBloomFilter>(stream);

    /// <summary>
    /// Creates a filter of a given type from a snapshot written by <see cref="Save(Stream)"/>.
    /// </summary>
    /// <typeparam name="TFilter">The type of the filter, such as <see cref="BlockedBloomFilterXXHash128"/>.</typeparam>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <returns>A new filter of the size and content of the snapshot.</returns>
    /// <exception cref="InvalidDataException">The snapshot contains a filter of another type, or is not valid (see <see cref="Load(Stream)"/>).</exception>
    public static TFilter Load<TFilter>(Stream stream)
        where TFilter : BlockedBloomFilter
    {
        ArgumentNullException.ThrowIfNull(stream);
        return BloomFilterSnapshot.Read(stream, FilterKind.BlockedBloomFilter, CreateFromSnapshot<TFilter>);
    }

    /// <summary>
    /// Creates a filter from a snapshot written by <see cref="Save(Stream)"/> or <see cref="SaveAsync(Stream, CancellationToken)"/>.
    /// </summary>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A new filter of the hash algorithm, size and content of the snapshot.</returns>
    /// <exception cref="InvalidDataException">The snapshot is not valid (see <see cref="Load(Stream)"/>).</exception>
    public static Task<BlockedBloomFilter> LoadAsync(Stream stream, CancellationToken cancellationToken = default) => LoadAsync<BlockedBloomFilter>(stream, cancellationToken);

    /// <summary>
    /// Creates a filter of a given type from a snapshot written by <see cref="Save(Stream)"/> or <see cref="SaveAsync(Stream, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TFilter">The type of the filter, such as <see cref="BlockedBloomFilterXXHash128"/>.</typeparam>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A new filter of the size and content of the snapshot.</returns>
    /// <exception cref="InvalidDataException">The snapshot contains a filter of another type, or is not valid (see <see cref="Load(Stream)"/>).</exception>
    public static Task<TFilter> LoadAsync<TFilter>(Stream stream, CancellationToken cancellationToken = default)
        where TFilter : BlockedBloomFilter
    {
        ArgumentNullException.ThrowIfNull(stream);
        return BloomFilterSnapshot.ReadAsync(stream, FilterKind.BlockedBloomFilter, CreateFromSnapshot<TFilter>, cancellationToken);
    }

    private Header GetSnapshotHeader()
    {
        var algorithm = this switch
        {
            BlockedBloomFilterXXHash128 => HashAlgorithm.XXHash128,
            BlockedBloomFilterXXHash64 => HashAlgorithm.XXHash64,
            BlockedBloomFilterXXHash32 => HashAlgorithm.XXHash32,
            BlockedBloomFilterXXHash3 => HashAlgorithm.XXHash3,
            BlockedBloomFilterCrc64 => HashAlgorithm.Crc64,
            BlockedBloomFilterCrc32 => HashAlgorithm.Crc32,
            BlockedBloomFilterAdler32 => HashAlgorithm.Adler32,
            _ => throw new UnreachableException(),
        };

        return new Header(FilterKind.BlockedBloomFilter, algorithm, _hashCount, (long)_blockCount);
    }

    private static (TFilter Filter, Payload Payload) CreateFromSnapshot<TFilter>(Header header)
        where TFilter : BlockedBloomFilter
    {
        var filterType = header.Algorithm switch
        {
            HashAlgorithm.XXHash128 => typeof(BlockedBloomFilterXXHash128),
            HashAlgorithm.XXHash64 => typeof(BlockedBloomFilterXXHash64),
            HashAlgorithm.XXHash32 => typeof(BlockedBloomFilterXXHash32),
            HashAlgorithm.XXHash3 => typeof(BlockedBloomFilterXXHash3),
            HashAlgorithm.Crc64 => typeof(BlockedBloomFilterCrc64),
            HashAlgorithm.Crc32 => typeof(BlockedBloomFilterCrc32),
            HashAlgorithm.Adler32 => typeof(BlockedBloomFilterAdler32),
            _ => throw new UnreachableException(),
        };

        // Checked before the filter is allocated
        if (!typeof(TFilter).IsAssignableFrom(filterType))
            throw CreateTypeMismatchException(header.Algorithm, filterType, typeof(TFilter));

        BlockedBloomFilter filter = header.Algorithm switch
        {
            HashAlgorithm.XXHash128 => new BlockedBloomFilterXXHash128(header.PositionCount, header.HashCount),
            HashAlgorithm.XXHash64 => new BlockedBloomFilterXXHash64(header.PositionCount, header.HashCount),
            HashAlgorithm.XXHash32 => new BlockedBloomFilterXXHash32(header.PositionCount, header.HashCount),
            HashAlgorithm.XXHash3 => new BlockedBloomFilterXXHash3(header.PositionCount, header.HashCount),
            HashAlgorithm.Crc64 => new BlockedBloomFilterCrc64(header.PositionCount, header.HashCount),
            HashAlgorithm.Crc32 => new BlockedBloomFilterCrc32(header.PositionCount, header.HashCount),
            HashAlgorithm.Adler32 => new BlockedBloomFilterAdler32(header.PositionCount, header.HashCount),
            _ => throw new UnreachableException(),
        };

        return ((TFilter)filter, new BlockPayload(filter));
    }

    // Copies the blocks in order, as 64-bit little-endian words. The alignment padding of the segments is not saved, so
    // the snapshot does not depend on where the arrays were allocated.
    private sealed class BlockPayload(BlockedBloomFilter filter) : Payload
    {
        private int _segmentIndex;
        private int _indexInSegment;

        public override long Length => (long)filter._blockCount * BlockSize;

        public override void CopyTo(Span<byte> destination)
        {
            for (var offset = 0; offset < destination.Length; offset += sizeof(ulong))
            {
                BinaryPrimitives.WriteUInt64LittleEndian(destination[offset..], Volatile.Read(ref NextWord()));
            }
        }

        public override void CopyFrom(ReadOnlySpan<byte> source)
        {
            for (var offset = 0; offset < source.Length; offset += sizeof(ulong))
            {
                NextWord() = BinaryPrimitives.ReadUInt64LittleEndian(source[offset..]);
            }
        }

        private ref ulong NextWord()
        {
            var words = filter._segments[_segmentIndex].GetWords();
            ref var word = ref words[_indexInSegment];
            _indexInSegment++;
            if (_indexInSegment == words.Length)
            {
                _segmentIndex++;
                _indexInSegment = 0;
            }

            return ref word;
        }
    }
}
