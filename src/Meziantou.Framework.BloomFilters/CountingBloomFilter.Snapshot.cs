using System.Diagnostics;
using static Meziantou.Framework.BloomFilters.BloomFilterSnapshot;

namespace Meziantou.Framework.BloomFilters;

public closed partial class CountingBloomFilter
{
    /// <summary>
    /// Writes a snapshot of the filter to a stream: its hash algorithm, its size and its counters.
    /// </summary>
    /// <param name="stream">The stream to write the snapshot to.</param>
    /// <remarks>
    /// <para>
    /// The counters are copied through a small buffer, so saving a large filter does not allocate a second copy of it.
    /// Saturated counters are saved as they are, and stay saturated once loaded.
    /// </para>
    /// <para>
    /// Stop adding and removing values while the snapshot is written. Each counter is read atomically, but the
    /// counters of a value updated during the write may be saved partially updated, so the restored filter may
    /// count it wrongly or report it as absent.
    /// </para>
    /// <para>
    /// Keys are hashed from their in-memory bytes, so the snapshot records the byte order of the machine, and
    /// <see cref="Load(Stream)"/> rejects a snapshot written on a machine with another byte order.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs" ref:region="CountingBloomFilter_Snapshot" ref:dedent="true">
    /// var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));
    /// filter.Add("alice@example.com");
    /// filter.Add("alice@example.com");
    ///
    /// using var stream = new MemoryStream();
    /// await filter.SaveAsync(stream);
    ///
    /// stream.Position = 0;
    /// var restored = await CountingBloomFilter.LoadAsync&lt;CountingBloomFilterXXHash128&gt;(stream);
    ///
    /// Assert.Equal(2, restored.GetEstimatedCount("alice@example.com"));
    /// </code>
    /// </example>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        BloomFilterSnapshot.Write(stream, GetSnapshotHeader(), new CounterPayload(Counters));
    }

    /// <summary>
    /// Writes a snapshot of the filter to a stream: its hash algorithm, its size and its counters.
    /// </summary>
    /// <param name="stream">The stream to write the snapshot to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the snapshot is written.</returns>
    /// <remarks>See <see cref="Save(Stream)"/>. Stop adding and removing values until the task completes.</remarks>
    public Task SaveAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return BloomFilterSnapshot.WriteAsync(stream, GetSnapshotHeader(), new CounterPayload(Counters), cancellationToken);
    }

    /// <summary>
    /// Creates a counting filter from a snapshot written by <see cref="Save(Stream)"/>.
    /// </summary>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <returns>A new filter of the hash algorithm, size and content of the snapshot, such as a <see cref="CountingBloomFilterXXHash128"/>.</returns>
    /// <exception cref="InvalidDataException">The data is not a Bloom filter snapshot, is truncated or corrupted, uses an unsupported version, contains a Bloom filter that is not a counting one, or was written on a machine with another byte order.</exception>
    /// <remarks>
    /// The header is validated before the filter is allocated. When the stream is seekable, its length is also
    /// checked against the size of the filter first. The snapshot ends with a checksum, verified once it is read.
    /// </remarks>
    public static CountingBloomFilter Load(Stream stream) => Load<CountingBloomFilter>(stream);

    /// <summary>
    /// Creates a counting filter of a given type from a snapshot written by <see cref="Save(Stream)"/>.
    /// </summary>
    /// <typeparam name="TFilter">The type of the filter, such as <see cref="CountingBloomFilterXXHash128"/>.</typeparam>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <returns>A new filter of the size and content of the snapshot.</returns>
    /// <exception cref="InvalidDataException">The snapshot contains a filter of another type, or is not valid (see <see cref="Load(Stream)"/>).</exception>
    public static TFilter Load<TFilter>(Stream stream)
        where TFilter : CountingBloomFilter
    {
        ArgumentNullException.ThrowIfNull(stream);
        return BloomFilterSnapshot.Read(stream, FilterKind.CountingBloomFilter, CreateFromSnapshot<TFilter>);
    }

    /// <summary>
    /// Creates a counting filter from a snapshot written by <see cref="Save(Stream)"/> or <see cref="SaveAsync(Stream, CancellationToken)"/>.
    /// </summary>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A new filter of the hash algorithm, size and content of the snapshot.</returns>
    /// <exception cref="InvalidDataException">The snapshot is not valid (see <see cref="Load(Stream)"/>).</exception>
    public static Task<CountingBloomFilter> LoadAsync(Stream stream, CancellationToken cancellationToken = default) => LoadAsync<CountingBloomFilter>(stream, cancellationToken);

    /// <summary>
    /// Creates a counting filter of a given type from a snapshot written by <see cref="Save(Stream)"/> or <see cref="SaveAsync(Stream, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="TFilter">The type of the filter, such as <see cref="CountingBloomFilterXXHash128"/>.</typeparam>
    /// <param name="stream">The stream to read the snapshot from. It is left positioned after the snapshot.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A new filter of the size and content of the snapshot.</returns>
    /// <exception cref="InvalidDataException">The snapshot contains a filter of another type, or is not valid (see <see cref="Load(Stream)"/>).</exception>
    public static Task<TFilter> LoadAsync<TFilter>(Stream stream, CancellationToken cancellationToken = default)
        where TFilter : CountingBloomFilter
    {
        ArgumentNullException.ThrowIfNull(stream);
        return BloomFilterSnapshot.ReadAsync(stream, FilterKind.CountingBloomFilter, CreateFromSnapshot<TFilter>, cancellationToken);
    }

    private Header GetSnapshotHeader()
    {
        var algorithm = this switch
        {
            CountingBloomFilterXXHash128 => HashAlgorithm.XXHash128,
            CountingBloomFilterXXHash64 => HashAlgorithm.XXHash64,
            CountingBloomFilterXXHash32 => HashAlgorithm.XXHash32,
            CountingBloomFilterXXHash3 => HashAlgorithm.XXHash3,
            CountingBloomFilterCrc64 => HashAlgorithm.Crc64,
            CountingBloomFilterCrc32 => HashAlgorithm.Crc32,
            CountingBloomFilterAdler32 => HashAlgorithm.Adler32,
            _ => throw new UnreachableException(),
        };

        return new Header(FilterKind.CountingBloomFilter, algorithm, HashCount, Counters.CounterCount);
    }

    private static (TFilter Filter, Payload Payload) CreateFromSnapshot<TFilter>(Header header)
        where TFilter : CountingBloomFilter
    {
        var filterType = header.Algorithm switch
        {
            HashAlgorithm.XXHash128 => typeof(CountingBloomFilterXXHash128),
            HashAlgorithm.XXHash64 => typeof(CountingBloomFilterXXHash64),
            HashAlgorithm.XXHash32 => typeof(CountingBloomFilterXXHash32),
            HashAlgorithm.XXHash3 => typeof(CountingBloomFilterXXHash3),
            HashAlgorithm.Crc64 => typeof(CountingBloomFilterCrc64),
            HashAlgorithm.Crc32 => typeof(CountingBloomFilterCrc32),
            HashAlgorithm.Adler32 => typeof(CountingBloomFilterAdler32),
            _ => throw new UnreachableException(),
        };

        // Checked before the filter is allocated
        if (!typeof(TFilter).IsAssignableFrom(filterType))
            throw CreateTypeMismatchException(header.Algorithm, filterType, typeof(TFilter));

        CountingBloomFilter filter = header.Algorithm switch
        {
            HashAlgorithm.XXHash128 => new CountingBloomFilterXXHash128(header.PositionCount, header.HashCount),
            HashAlgorithm.XXHash64 => new CountingBloomFilterXXHash64(header.PositionCount, header.HashCount),
            HashAlgorithm.XXHash32 => new CountingBloomFilterXXHash32(header.PositionCount, header.HashCount),
            HashAlgorithm.XXHash3 => new CountingBloomFilterXXHash3(header.PositionCount, header.HashCount),
            HashAlgorithm.Crc64 => new CountingBloomFilterCrc64(header.PositionCount, header.HashCount),
            HashAlgorithm.Crc32 => new CountingBloomFilterCrc32(header.PositionCount, header.HashCount),
            HashAlgorithm.Adler32 => new CountingBloomFilterAdler32(header.PositionCount, header.HashCount),
            _ => throw new UnreachableException(),
        };

        return ((TFilter)filter, new CounterPayload(filter.Counters));
    }
}
