using System.Diagnostics;
using static Meziantou.Framework.BloomFilters.BloomFilterSnapshot;

namespace Meziantou.Framework.BloomFilters;

public abstract partial class BloomFilter
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
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs" ref:region="BloomFilter_Snapshot" ref:dedent="true">
    /// var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));
    /// filter.Add("alice@example.com");
    ///
    /// // Save the filter to a stream, such as a FileStream, and load it after a restart
    /// using var stream = new MemoryStream();
    /// filter.Save(stream);
    ///
    /// stream.Position = 0;
    /// var restored = BloomFilter.Load&lt;BloomFilterXXHash128&gt;(stream);
    ///
    /// Assert.True(restored.MayContain("alice@example.com"));
    /// Assert.False(restored.MayContain("bob@example.com"));
    /// </code>
    /// </example>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        BloomFilterSnapshot.Write(stream, GetSnapshotHeader(), new BitPayload(Bits));
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
        return BloomFilterSnapshot.WriteAsync(stream, GetSnapshotHeader(), new BitPayload(Bits), cancellationToken);
    }

    /// <summary>
    /// Creates a filter from a snapshot written by <see cref="Save(Stream)"/>.
    /// </summary>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <returns>A new filter of the hash algorithm, size and content of the snapshot, such as a <see cref="BloomFilterXXHash128"/>.</returns>
    /// <exception cref="InvalidDataException">The data is not a Bloom filter snapshot, is truncated or corrupted, uses an unsupported version, contains a counting Bloom filter, or was written on a machine with another byte order.</exception>
    /// <remarks>
    /// The header is validated before the filter is allocated. When the stream is seekable, its length is also
    /// checked against the size of the filter first. The snapshot ends with a checksum, verified once it is read.
    /// </remarks>
    public static BloomFilter Load(Stream stream) => Load<BloomFilter>(stream);

    /// <summary>
    /// Creates a filter of a given type from a snapshot written by <see cref="Save(Stream)"/>.
    /// </summary>
    /// <typeparam name="TFilter">The type of the filter, such as <see cref="BloomFilterXXHash128"/>.</typeparam>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <returns>A new filter of the size and content of the snapshot.</returns>
    /// <exception cref="InvalidDataException">The snapshot contains a filter of another type, or is not valid (see <see cref="Load(Stream)"/>).</exception>
    public static TFilter Load<TFilter>(Stream stream)
        where TFilter : BloomFilter
    {
        ArgumentNullException.ThrowIfNull(stream);
        return BloomFilterSnapshot.Read(stream, FilterKind.BloomFilter, CreateFromSnapshot<TFilter>);
    }

    /// <summary>
    /// Creates a filter from a snapshot written by <see cref="Save(Stream)"/> or <see cref="SaveAsync(Stream, CancellationToken)"/>.
    /// </summary>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A new filter of the hash algorithm, size and content of the snapshot.</returns>
    /// <exception cref="InvalidDataException">The snapshot is not valid (see <see cref="Load(Stream)"/>).</exception>
    public static Task<BloomFilter> LoadAsync(Stream stream, CancellationToken cancellationToken = default) => LoadAsync<BloomFilter>(stream, cancellationToken);

    /// <summary>
    /// Creates a filter of a given type from a snapshot written by <see cref="Save(Stream)"/> or <see cref="SaveAsync(Stream, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TFilter">The type of the filter, such as <see cref="BloomFilterXXHash128"/>.</typeparam>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A new filter of the size and content of the snapshot.</returns>
    /// <exception cref="InvalidDataException">The snapshot contains a filter of another type, or is not valid (see <see cref="Load(Stream)"/>).</exception>
    public static Task<TFilter> LoadAsync<TFilter>(Stream stream, CancellationToken cancellationToken = default)
        where TFilter : BloomFilter
    {
        ArgumentNullException.ThrowIfNull(stream);
        return BloomFilterSnapshot.ReadAsync(stream, FilterKind.BloomFilter, CreateFromSnapshot<TFilter>, cancellationToken);
    }

    private Header GetSnapshotHeader()
    {
        var algorithm = this switch
        {
            BloomFilterXXHash128 => HashAlgorithm.XXHash128,
            BloomFilterXXHash64 => HashAlgorithm.XXHash64,
            BloomFilterXXHash32 => HashAlgorithm.XXHash32,
            BloomFilterXXHash3 => HashAlgorithm.XXHash3,
            BloomFilterCrc64 => HashAlgorithm.Crc64,
            BloomFilterCrc32 => HashAlgorithm.Crc32,
            BloomFilterAdler32 => HashAlgorithm.Adler32,
            _ => throw new UnreachableException(),
        };

        return new Header(FilterKind.BloomFilter, algorithm, HashCount, Bits.BitCount);
    }

    private static (TFilter Filter, Payload Payload) CreateFromSnapshot<TFilter>(Header header)
        where TFilter : BloomFilter
    {
        var filterType = header.Algorithm switch
        {
            HashAlgorithm.XXHash128 => typeof(BloomFilterXXHash128),
            HashAlgorithm.XXHash64 => typeof(BloomFilterXXHash64),
            HashAlgorithm.XXHash32 => typeof(BloomFilterXXHash32),
            HashAlgorithm.XXHash3 => typeof(BloomFilterXXHash3),
            HashAlgorithm.Crc64 => typeof(BloomFilterCrc64),
            HashAlgorithm.Crc32 => typeof(BloomFilterCrc32),
            HashAlgorithm.Adler32 => typeof(BloomFilterAdler32),
            _ => throw new UnreachableException(),
        };

        // Checked before the filter is allocated
        if (!typeof(TFilter).IsAssignableFrom(filterType))
            throw CreateTypeMismatchException(header.Algorithm, filterType, typeof(TFilter));

        BloomFilter filter = header.Algorithm switch
        {
            HashAlgorithm.XXHash128 => new BloomFilterXXHash128(header.PositionCount, header.HashCount),
            HashAlgorithm.XXHash64 => new BloomFilterXXHash64(header.PositionCount, header.HashCount),
            HashAlgorithm.XXHash32 => new BloomFilterXXHash32(header.PositionCount, header.HashCount),
            HashAlgorithm.XXHash3 => new BloomFilterXXHash3(header.PositionCount, header.HashCount),
            HashAlgorithm.Crc64 => new BloomFilterCrc64(header.PositionCount, header.HashCount),
            HashAlgorithm.Crc32 => new BloomFilterCrc32(header.PositionCount, header.HashCount),
            HashAlgorithm.Adler32 => new BloomFilterAdler32(header.PositionCount, header.HashCount),
            _ => throw new UnreachableException(),
        };

        return ((TFilter)filter, new BitPayload(filter.Bits));
    }
}
