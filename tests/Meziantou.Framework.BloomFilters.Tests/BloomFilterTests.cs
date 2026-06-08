using System.Buffers.Binary;
using System.Numerics;
using Meziantou.Framework.BloomFilters;

namespace Meziantou.Framework.Tests;

public sealed class BloomFilterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidBitCount_Throws(long bitCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BloomFilter(bitCount, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidHashCount_Throws(int hashCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BloomFilter(64, hashCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_InvalidExpectedInsertions_Throws(long expectedInsertions)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BloomFilter.Create(expectedInsertions, 0.01));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1)]
    [InlineData(1.1)]
    public void Create_InvalidFalsePositiveProbability_Throws(double falsePositiveProbability)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BloomFilter.Create(100, falsePositiveProbability));
    }

    [Fact]
    public void Create_UsesValidParameters()
    {
        var filter = BloomFilter.Create(1_000, 0.01);

        Assert.True(filter.BitCount > 0);
        Assert.True(filter.HashCount > 0);
    }

    [Fact]
    public void AddAndMightContain_DoubleHashing()
    {
        var filter = new BloomFilter(1_024, 4);

        filter.Add(123_456_789, 987_654_321);

        Assert.True(filter.MightContain(123_456_789, 987_654_321));
    }

    [Fact]
    public void Add_WithZeroHash2_SetsMultipleBits()
    {
        var filter = new BloomFilter(1_024, 5);

        filter.Add(456, 0);

        var snapshot = filter.ToSnapshot();
        var setBitCount = CountSetBits(snapshot.Data);
        Assert.Equal(5, setBitCount);
    }

    [Fact]
    public void MightContain_ReturnsFalseForNonMatchingHash()
    {
        var filter = new BloomFilter(128, 1);
        filter.Add(10, 0);

        Assert.False(filter.MightContain(11, 0));
    }

    [Fact]
    public void Add_SpanHashes_RequiresEnoughValues()
    {
        var filter = new BloomFilter(256, 3);
        Assert.Throws<ArgumentException>(() => filter.Add([1, 2]));
    }

    [Fact]
    public void MightContain_SpanHashes_RequiresEnoughValues()
    {
        var filter = new BloomFilter(256, 3);
        Assert.Throws<ArgumentException>(() => filter.MightContain([1, 2]));
    }

    [Fact]
    public void AddAndMightContain_SpanHashes()
    {
        var filter = new BloomFilter(4_096, 3);
        ulong[] hashes = [111, 222, 333];

        filter.Add(hashes);

        Assert.True(filter.MightContain(hashes));
        Assert.False(filter.MightContain([111, 222, 334]));
    }

    [Fact]
    public void Clear_UnsetsBits()
    {
        var filter = new BloomFilter(128, 1);
        filter.Add(42, 1);

        filter.Clear();

        Assert.False(filter.MightContain(42, 1));
    }

    [Fact]
    public void UnionWith_MergesBits()
    {
        var left = new BloomFilter(1_024, 1);
        var right = new BloomFilter(1_024, 1);
        left.Add(10, 0);
        right.Add(200, 0);

        left.UnionWith(right);

        Assert.True(left.MightContain(10, 0));
        Assert.True(left.MightContain(200, 0));
    }

    [Fact]
    public void UnionWith_IncompatibleFilters_Throws()
    {
        var left = new BloomFilter(1_024, 1);
        var right = new BloomFilter(2_048, 1);

        Assert.Throws<ArgumentException>(() => left.UnionWith(right));
    }

    [Fact]
    public void Union_Static_DoesNotMutateInputs()
    {
        var left = new BloomFilter(1_024, 1);
        var right = new BloomFilter(1_024, 1);
        left.Add(10, 0);
        right.Add(20, 0);

        var union = BloomFilter.Union(left, right);

        Assert.True(union.MightContain(10, 0));
        Assert.True(union.MightContain(20, 0));
        Assert.False(left.MightContain(20, 0));
        Assert.False(right.MightContain(10, 0));
    }

    [Fact]
    public void Resize_SameParameters_ReturnsCopy()
    {
        var filter = new BloomFilter(512, 2);
        filter.Add(11, 13);

        var resized = filter.Resize(512, 2);
        resized.Clear();

        Assert.True(filter.MightContain(11, 13));
    }

    [Fact]
    public void Resize_DifferentParameters_Throws()
    {
        var filter = new BloomFilter(512, 2);
        Assert.Throws<NotSupportedException>(() => filter.Resize(1_024));
    }

    [Fact]
    public void ToSnapshotAndLoad_Roundtrip()
    {
        var filter = new BloomFilter(4_096, 4);
        filter.Add(123, 456);
        filter.Add(456, 789);
        var snapshot = filter.ToSnapshot();

        var loaded = BloomFilter.Load(snapshot);

        Assert.True(loaded.MightContain(123, 456));
        Assert.True(loaded.MightContain(456, 789));
    }

    [Fact]
    public void Snapshot_ReportsSegmentMetadata()
    {
        var filter = new BloomFilter((1L << 20) * 64 + 1, 1);
        var snapshot = filter.ToSnapshot();

        Assert.True(snapshot.SegmentCount > 1);
        Assert.True(snapshot.WordsPerSegment > 0);
    }

    [Fact]
    public void Load_SnapshotWithInvalidMetadata_Throws()
    {
        var filter = new BloomFilter(4_096, 2);
        var snapshot = filter.ToSnapshot();
        var invalidSnapshot = new BloomFilterSnapshot
        {
            BitCount = snapshot.BitCount,
            HashCount = snapshot.HashCount,
            SegmentCount = snapshot.SegmentCount + 1,
            WordsPerSegment = snapshot.WordsPerSegment,
            Data = snapshot.Data,
        };

        Assert.Throws<InvalidDataException>(() => BloomFilter.Load(invalidSnapshot));
    }

    [Fact]
    public void Load_SnapshotWithInvalidLength_Throws()
    {
        var snapshot = new BloomFilterSnapshot
        {
            BitCount = 4_096,
            HashCount = 2,
            SegmentCount = 1,
            WordsPerSegment = 1 << 20,
            Data = [1, 2, 3],
        };

        Assert.Throws<InvalidDataException>(() => BloomFilter.Load(snapshot));
    }

    [Fact]
    public void WriteToAndLoadStream_Roundtrip()
    {
        var filter = new BloomFilter(4_096, 3);
        filter.Add(100, 3);
        filter.Add(200, 7);

        using var stream = new MemoryStream();
        filter.WriteTo(stream);
        stream.Position = 0;

        var loaded = BloomFilter.Load(stream);

        Assert.True(loaded.MightContain(100, 3));
        Assert.True(loaded.MightContain(200, 7));
    }

    [Fact]
    public void WriteTo_NonWritableStream_Throws()
    {
        var filter = new BloomFilter(64, 1);
        using var stream = new MemoryStream(Array.Empty<byte>(), writable: false);

        Assert.Throws<ArgumentException>(() => filter.WriteTo(stream));
    }

    [Fact]
    public void Load_NonReadableStream_Throws()
    {
        using var stream = new NonReadableMemoryStream();
        Assert.Throws<ArgumentException>(() => BloomFilter.Load(stream));
    }

    [Fact]
    public void Load_StreamWithInvalidMagic_Throws()
    {
        using var stream = new MemoryStream(new byte[28]);
        Assert.Throws<InvalidDataException>(() => BloomFilter.Load(stream));
    }

    [Fact]
    public void Load_StreamWithInvalidMetadata_Throws()
    {
        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x464d4c42);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 1);
        BinaryPrimitives.WriteInt64LittleEndian(header[8..], 128);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], 999);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], 1 << 20);
        stream.Write(header);
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => BloomFilter.Load(stream));
    }

    [Fact]
    public void Add_IsThreadSafeForConcurrentWrites()
    {
        var filter = new BloomFilter(1 << 20, 3);
        var hashes = Enumerable.Range(0, 1_000)
            .Select(index => (hash1: (ulong)(index * 97 + 17), hash2: (ulong)(index * 193 + 29)))
            .ToArray();

        Parallel.ForEach(hashes, hash => filter.Add(hash.hash1, hash.hash2));

        foreach (var hash in hashes)
        {
            Assert.True(filter.MightContain(hash.hash1, hash.hash2));
        }
    }

    [Fact]
    public void GenericWrapper_AddAndMightContain()
    {
        var filter = new BloomFilter<TestValue, TestValueHashProvider>(1_024);
        var value = new TestValue(100, 7);
        filter.Add(value);

        Assert.True(filter.MightContain(value));
        Assert.Equal(TestValueHashProvider.HashCount, filter.HashCount);
    }

    [Fact]
    public void GenericWrapper_Create_UsesProviderHashCount()
    {
        var filter = BloomFilter<TestValue, TestValueHashProvider>.Create(100, 0.01);

        Assert.Equal(TestValueHashProvider.HashCount, filter.HashCount);
        Assert.True(filter.BitCount > 0);
    }

    [Fact]
    public void GenericWrapper_InvalidHashProvider_Throws()
    {
        Assert.Throws<TypeInitializationException>(() => _ = new BloomFilter<TestValue, InvalidHashProvider>(128));
    }

    [Fact]
    public void XxHash128StringProvider_ComputeHashes_IsDeterministic()
    {
        Span<ulong> first = stackalloc ulong[XxHash128StringBloomHashProvider.HashCount];
        Span<ulong> second = stackalloc ulong[XxHash128StringBloomHashProvider.HashCount];

        XxHash128StringBloomHashProvider.ComputeHashes("hello", first);
        XxHash128StringBloomHashProvider.ComputeHashes("hello", second);

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public void XxHash128StringProvider_ComputeHashes_NullValue_Throws()
    {
        var hashes = new ulong[XxHash128StringBloomHashProvider.HashCount];
        Assert.Throws<ArgumentNullException>(() => XxHash128StringBloomHashProvider.ComputeHashes(value: null!, hashes));
    }

    [Fact]
    public void XxHash128StringProvider_ComputeHashes_SmallSpan_Throws()
    {
        var hashes = new ulong[1];
        Assert.Throws<ArgumentException>(() => XxHash128StringBloomHashProvider.ComputeHashes("hello", hashes));
    }

    [Fact]
    public void GenericWrapper_WithXxHash128Provider_AddAndMightContain()
    {
        var filter = new BloomFilter<string, XxHash128StringBloomHashProvider>(8_192);
        filter.Add("value-1");
        filter.Add("value-2");

        Assert.True(filter.MightContain("value-1"));
        Assert.True(filter.MightContain("value-2"));
    }

    private static int CountSetBits(ReadOnlySpan<byte> data)
    {
        var result = 0;
        foreach (var value in data)
        {
            result += BitOperations.PopCount((uint)value);
        }

        return result;
    }

    private sealed record TestValue(ulong Hash1, ulong Hash2);

    private readonly struct TestValueHashProvider : IBloomHashProvider<TestValue>
    {
        public static int HashCount => 3;

        public static void ComputeHashes(TestValue value, Span<ulong> hashes)
        {
            hashes[0] = value.Hash1;
            hashes[1] = value.Hash1 + value.Hash2;
            hashes[2] = value.Hash1 + (value.Hash2 * 2);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "MA0182:Internal type is apparently never used", Justification = "Used as a generic type argument in unit tests.")]
    private readonly struct InvalidHashProvider : IBloomHashProvider<TestValue>
    {
        public static int HashCount => 0;

        public static void ComputeHashes(TestValue value, Span<ulong> hashes)
        {
            hashes.Clear();
        }
    }

    private sealed class NonReadableMemoryStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
