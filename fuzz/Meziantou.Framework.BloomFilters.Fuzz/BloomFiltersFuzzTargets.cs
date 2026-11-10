using System.Buffers.Binary;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.BloomFilters.Fuzz;

/// <summary>
/// A snapshot that loads saves back to the same bytes, and any other input is rejected with an
/// <see cref="InvalidDataException"/>. On blocked filters, <c>AddRange</c> sets the same bits as <c>Add</c>, the union of
/// two filters has the bits of a filter built from both sets of values, and a snapshot gives back the same filter.
/// </summary>
public static class BloomFiltersFuzzTargets
{
    private const int MaxValues = 4096;

    private static readonly string[] Tokens =
    [
        "MZBF",
        Token(0x01, 0x00), // format version
        Token(0x01), Token(0x02), Token(0x03), // filter kinds
        Token(0x04), Token(0x05), Token(0x06), Token(0x07), // hash algorithms
        Token(0x00, 0x02, 0x00, 0x00), // 512 hash functions
        Token(0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F), // long.MaxValue
        Token(0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x3F, 0x00), // the largest block count
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("bloomfilter-snapshot", Snapshot) { Seeds = GetSnapshotSeeds, Dictionary = Tokens },
        new("bloomfilter-blocked-operations", BlockedOperations) { Seeds = GetOperationSeeds },
    ];

    /// <summary>
    /// <c>kind, snapshot</c>: <c>kind</c> picks the filter that loads the snapshot (Bloom, counting or blocked). A
    /// snapshot that loads saves back to the bytes it was read from; any other input throws an
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    private static void Snapshot(ReadOnlySpan<byte> data)
    {
        var kind = FuzzInput.ReadByte(ref data) % 3;
        var bytes = data.ToArray();

        // A seekable stream: the length is checked before the filter is allocated, so a corrupted size cannot allocate
        // more than the input
        using var stream = new MemoryStream(bytes, writable: false);
        byte[] saved;
        try
        {
            switch (kind)
            {
                case 0:
                    saved = Save(BloomFilter.Load(stream).Save);
                    break;
                case 1:
                    saved = Save(CountingBloomFilter.Load(stream).Save);
                    break;
                default:
                    var blockedFilter = BlockedBloomFilter.Load(stream);
                    saved = Save(blockedFilter.Save);

                    // At most 512 hash functions, so a lookup is cheap
                    _ = ((IBloomFilter)blockedFilter).MayContain(bytes.AsSpan());
                    _ = blockedFilter.GetEstimateCount();
                    break;
            }
        }
        catch (InvalidDataException)
        {
            return;
        }

        FuzzAssert.Equal(stream.Position, saved.LongLength, "The snapshot does not end where the loader stopped reading");
        FuzzAssert.True(saved.AsSpan().SequenceEqual(bytes.AsSpan(0, saved.Length)), "Saving a loaded snapshot does not give back its bytes");
    }

    /// <summary>
    /// <c>algorithm, blocks, hashes (2 bytes), split, values</c>: the values are 64-bit little-endian integers, the first
    /// <c>split</c> go to a filter, the others to a second one.
    /// </summary>
    private static void BlockedOperations(ReadOnlySpan<byte> data)
    {
        var algorithm = FuzzInput.ReadByte(ref data) % 3;
        var blockCount = 1 + (FuzzInput.ReadByte(ref data) % 16);
        var hashCount = 1 + ((FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8)) % 512);
        var split = FuzzInput.ReadByte(ref data);

        var values = new ulong[Math.Min(MaxValues, data.Length / sizeof(ulong))];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt64LittleEndian(data[(i * sizeof(ulong))..]);
        }

        var first = values.AsSpan(0, Math.Min(split, values.Length)).ToArray();
        var second = values.AsSpan(first.Length).ToArray();
        var size = BlockedBloomFilterSize.CreateExact(blockCount, hashCount);
        var classicSize = BloomFilterSize.CreateExact(size.BitCount, hashCount);

        // AddRange sets the same bits as Add, on blocked and on classic filters
        var filter = Create(algorithm, size);
        var expected = Create(algorithm, size);
        AddRange(algorithm, filter, first, signed: (split & 1) is 0);
        Add(algorithm, expected, first);
        FuzzAssert.True(Save(filter.Save).AsSpan().SequenceEqual(Save(expected.Save)), "AddRange does not set the same bits as Add");

        var classicFilter = CreateClassic(algorithm, classicSize);
        var classicExpected = CreateClassic(algorithm, classicSize);
        AddRange(algorithm, classicFilter, values);
        Add(algorithm, classicExpected, values);
        FuzzAssert.True(Save(classicFilter.Save).AsSpan().SequenceEqual(Save(classicExpected.Save)), "AddRange of a Bloom filter does not set the same bits as Add");

        // The union has the bits of a filter built from both sets
        var other = Create(algorithm, size);
        AddRange(algorithm, other, second, signed: (split & 2) is 0);
        var otherSnapshot = Save(other.Save);
        Add(algorithm, expected, second);
        filter.UnionWith(other);
        var snapshot = Save(filter.Save);
        FuzzAssert.True(snapshot.AsSpan().SequenceEqual(Save(expected.Save)), "The union does not have the bits of a filter built from both sets");
        FuzzAssert.True(otherSnapshot.AsSpan().SequenceEqual(Save(other.Save)), "The union modified its source");
        foreach (var value in values)
        {
            FuzzAssert.True(MayContain(algorithm, filter, value), $"The union does not contain {value}");
        }

        // A snapshot gives back the same filter
        var restored = BlockedBloomFilter.Load(new MemoryStream(snapshot, writable: false));
        FuzzAssert.Equal(filter.GetType(), restored.GetType(), "The snapshot restored another type of filter");
        FuzzAssert.True(snapshot.AsSpan().SequenceEqual(Save(restored.Save)), "The restored filter does not save the same snapshot");
        foreach (var value in values)
        {
            FuzzAssert.True(MayContain(algorithm, restored, value), $"The restored filter does not contain {value}");
        }
    }

    private static BlockedBloomFilter Create(int algorithm, BlockedBloomFilterSize size) => algorithm switch
    {
        0 => BlockedBloomFilter.CreateXXHash32(size),
        1 => BlockedBloomFilter.CreateXXHash64(size),
        _ => BlockedBloomFilter.CreateXXHash128(size),
    };

    private static BloomFilter CreateClassic(int algorithm, BloomFilterSize size) => algorithm switch
    {
        0 => BloomFilter.CreateXXHash32(size),
        1 => BloomFilter.CreateXXHash64(size),
        _ => BloomFilter.CreateXXHash128(size),
    };

    private static void AddRange(int algorithm, BlockedBloomFilter filter, ulong[] values, bool signed)
    {
        switch (filter)
        {
            case BlockedBloomFilterXXHash32 f when signed:
                f.AddRange(Array.ConvertAll(values, value => unchecked((int)value)));
                break;
            case BlockedBloomFilterXXHash32 f:
                f.AddRange(Array.ConvertAll(values, value => unchecked((uint)value)));
                break;
            case BlockedBloomFilterXXHash64 f when signed:
                f.AddRange(Array.ConvertAll(values, value => unchecked((long)value)));
                break;
            case BlockedBloomFilterXXHash64 f:
                f.AddRange(values);
                break;
            case BlockedBloomFilterXXHash128 f when signed:
                f.AddRange(Array.ConvertAll(values, value => unchecked((long)value)));
                break;
            case BlockedBloomFilterXXHash128 f:
                f.AddRange(values);
                break;
            default:
                FuzzAssert.Fail($"Unexpected algorithm {algorithm}");
                break;
        }
    }

    private static void AddRange(int algorithm, BloomFilter filter, ulong[] values)
    {
        switch (filter)
        {
            case BloomFilterXXHash32 f:
                f.AddRange(Array.ConvertAll(values, value => unchecked((uint)value)));
                break;
            case BloomFilterXXHash64 f:
                f.AddRange(values);
                break;
            case BloomFilterXXHash128 f:
                f.AddRange(values);
                break;
            default:
                FuzzAssert.Fail($"Unexpected algorithm {algorithm}");
                break;
        }
    }

    // A 32-bit algorithm adds 32-bit values: the signed and unsigned overloads hash the same bytes
    private static void Add(int algorithm, BlockedBloomFilter filter, ulong[] values)
    {
        var target = (IBloomFilter)filter;
        foreach (var value in values)
        {
            if (algorithm is 0)
            {
                target.Add(unchecked((uint)value));
            }
            else
            {
                target.Add(value);
            }
        }
    }

    private static void Add(int algorithm, BloomFilter filter, ulong[] values)
    {
        var target = (IBloomFilter)filter;
        foreach (var value in values)
        {
            if (algorithm is 0)
            {
                target.Add(unchecked((uint)value));
            }
            else
            {
                target.Add(value);
            }
        }
    }

    private static bool MayContain(int algorithm, BlockedBloomFilter filter, ulong value)
    {
        var target = (IBloomFilter)filter;
        return algorithm is 0 ? target.MayContain(unchecked((uint)value)) : target.MayContain(value);
    }

    private static byte[] Save(Action<Stream> save)
    {
        using var stream = new MemoryStream();
        save(stream);
        return stream.ToArray();
    }

    private static IEnumerable<FuzzSeed> GetSnapshotSeeds()
    {
        // The sizes of the snapshot unit tests: one bit, a partial word, whole words, several blocks
        foreach (var bitCount in (long[])[1, 63, 64, 65, 1000])
        {
            var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateExact(bitCount, 3));
            yield return Seed($"bloom-empty-{bitCount}", 0, Save(filter.Save));
            Fill((IBloomFilter)filter);
            yield return Seed($"bloom-{bitCount}", 0, Save(filter.Save));

            var countingFilter = CountingBloomFilter.CreateXXHash64(CountingBloomFilterSize.CreateExact(bitCount, 3));
            for (var i = 0; i < 300; i++)
            {
                countingFilter.Add(i % 10);
            }

            yield return Seed($"counting-{bitCount}", 1, Save(countingFilter.Save));
        }

        foreach (var (blockCount, hashCount) in ((long, int)[])[(1, 1), (3, 6), (2, 512)])
        {
            var filter = BlockedBloomFilter.CreateCrc32(BlockedBloomFilterSize.CreateExact(blockCount, hashCount));
            yield return Seed($"blocked-empty-{blockCount}-{hashCount}", 2, Save(filter.Save));
            Fill(filter);
            yield return Seed($"blocked-{blockCount}-{hashCount}", 2, Save(filter.Save));
        }

        // A snapshot of another kind, rejected
        yield return Seed("blocked-as-bloom", 0, Save(BlockedBloomFilter.CreateXXHash3(BlockedBloomFilterSize.CreateExact(1, 4)).Save));

        static void Fill(IBloomFilter filter)
        {
            for (var i = 0; i < 100; i++)
            {
                filter.Add(i);
                filter.Add("value-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetOperationSeeds()
    {
        // Lengths around the vector widths, so the scalar tails run
        foreach (var count in (int[])[0, 1, 3, 7, 33, 200])
        {
            for (var algorithm = 0; algorithm < 3; algorithm++)
            {
                var data = new byte[5 + (count * sizeof(ulong))];
                data[0] = (byte)algorithm;
                data[1] = (byte)(count % 16);
                data[2] = (byte)(1 + (count % 12));
                data[3] = 0;
                data[4] = (byte)(count / 2);
                for (var i = 0; i < count; i++)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(5 + (i * sizeof(ulong))), unchecked((ulong)i * 0x9E3779B97F4A7C15UL));
                }

                yield return new FuzzSeed($"values-{count}-algorithm-{algorithm}", data);
            }
        }
    }

    private static FuzzSeed Seed(string name, byte kind, byte[] snapshot) => new(name, (byte[])[kind, .. snapshot]);

    private static string Token(params ReadOnlySpan<byte> bytes) => FuzzText.Decode(bytes);
}
