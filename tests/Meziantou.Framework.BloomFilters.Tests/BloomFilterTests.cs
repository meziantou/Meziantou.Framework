using Meziantou.Framework.BloomFilters;

namespace Meziantou.Framework.Tests;

public sealed class BloomFilterTests
{
    public static TheoryData<string, object> AllAlgorithmsAndValues()
    {
        var data = new TheoryData<string, object>();
        foreach (var method in typeof(BloomFilter).GetMethods().Where(m => m.Name.StartsWith("Create", StringComparison.Ordinal) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(BloomFilterSize)))
        {
            foreach (var value in GetValues())
            {
                data.Add(method.Name, value);
            }
        }

        return data;
    }

    public static TheoryData<string, object> AllCountingAlgorithmsAndValues()
    {
        var data = new TheoryData<string, object>();
        foreach (var method in typeof(CountingBloomFilter).GetMethods().Where(m => m.Name.StartsWith("Create", StringComparison.Ordinal) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(CountingBloomFilterSize)))
        {
            foreach (var value in GetValues())
            {
                data.Add(method.Name, value);
            }
        }

        return data;
    }

    [Fact]
    public void ReadmeExample_BloomFilter()
    {
        #region BloomFilter_Usage
        // Configure the filter for ~1,000 items and 1% false positive probability
        var size = BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01);
        var filter = BloomFilter.CreateXXHash3(size);

        filter.Add("alice@example.com");
        filter.Add("bob@example.com");

        // Item may exist (no false negatives)
        Assert.True(filter.MayContain("alice@example.com"));

        // Approximate number of inserted items
        var estimatedItemCount = filter.GetEstimateCount();
        Assert.Equal(2, estimatedItemCount, tolerance: 0.1);
        #endregion
    }

    [Fact]
    public void ReadmeExample_CountingBloomFilter()
    {
        #region CountingBloomFilter_Usage
        var size = CountingBloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01);
        var filter = CountingBloomFilter.CreateXXHash3(size);

        filter.Add("alice@example.com");
        filter.Add("alice@example.com");

        Assert.True(filter.MayContain("alice@example.com"));
        var estimatedCount = filter.GetEstimatedCount("alice@example.com");
        Assert.True(estimatedCount >= 2);

        filter.Remove("alice@example.com");
        #endregion

        Assert.True(filter.MayContain("alice@example.com"));
    }

    [Fact]
    public void Documentation_Size()
    {
        #region BloomFilter_Size
        // The size for 1 million items and a 1% false positive probability
        var size = BloomFilterSize.CreateOptimalSize(expectedItemCount: 1_000_000, falsePositiveProbability: 0.01);
        Assert.Equal(9_585_059, size.BitCount); // about 1.2 MB of memory
        Assert.Equal(7, size.HashCount);

        // Or choose the number of bits and of hash functions yourself
        var exact = BloomFilterSize.CreateExact(bitCount: 8 * 1024 * 1024, hashCount: 5);
        var filter = BloomFilter.CreateXXHash128(exact);
        filter.Add("alice@example.com");
        Assert.True(filter.MayContain("alice@example.com"));
        #endregion
    }

    [Fact]
    public void Documentation_Values()
    {
        #region BloomFilter_Values
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));

        var userId = new Guid("b2c4a0a6-4f5e-4d58-9a43-5d1c37bf0f4e");
        filter.Add(userId);
        filter.Add(42);
        filter.Add("alice@example.com"u8); // the bytes of a value

        // Add several 64-bit values in one call
        filter.AddRange(1L, 2L, 3L);

        Assert.True(filter.MayContain(userId));
        Assert.True(filter.MayContain(42));
        Assert.True(filter.MayContain("alice@example.com"u8));
        Assert.True(filter.MayContain(2L));

        // The type is part of the value: 42 as an int and 42 as a long are different values,
        // like a string and its UTF-8 bytes
        Assert.False(filter.MayContain(42L));
        Assert.False(filter.MayContain("alice@example.com"));
        #endregion
    }

    [Fact]
    public void Documentation_Concurrency()
    {
        #region BloomFilter_Concurrency
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(expectedItemCount: 100_000, falsePositiveProbability: 0.01));

        // Add values from several threads at the same time, without a lock
        Parallel.For(0, 100_000, i => filter.Add(i));

        for (var i = 0; i < 100_000; i++)
        {
            Assert.True(filter.MayContain(i));
        }
        #endregion
    }

    [Fact]
    public void Documentation_Union()
    {
        #region BloomFilter_Union
        // Size each filter for the total number of values: the merged filter holds them all
        var size = BloomFilterSize.CreateOptimalSize(expectedItemCount: 2000, falsePositiveProbability: 0.01);
        var europe = BloomFilter.CreateXXHash128(size);
        var america = BloomFilter.CreateXXHash128(size);

        europe.Add("alice@example.com");
        america.Add("bob@example.com");

        // Add the values of america to europe
        europe.UnionWith(america);
        Assert.True(europe.MayContain("alice@example.com"));
        Assert.True(europe.MayContain("bob@example.com"));

        // The filters must use the same hash algorithm, number of bits and number of hash functions
        var other = BloomFilter.CreateXXHash3(size);
        Assert.Throws<ArgumentException>(() => europe.UnionWith(other));
        #endregion
    }

    [Theory]
    [InlineData(1L, 1)]
    [InlineData(63L, 2)]
    [InlineData(64L, 3)]
    [InlineData(65L, 3)]
    [InlineData(1_000L, 4)]
    [InlineData(100_003L, 7)]
    public void UnionWith_MatchesFilterBuiltFromBothSets(long bitCount, int hashCount)
    {
        var size = BloomFilterSize.CreateExact(bitCount, hashCount);
        var first = BloomFilter.CreateXXHash128(size);
        var second = BloomFilter.CreateXXHash128(size);
        var expected = BloomFilter.CreateXXHash128(size);
        for (var i = 0; i < 2_000; i++)
        {
            first.Add(i);
            expected.Add(i);
        }

        for (var i = 1_000; i < 5_000; i++)
        {
            second.Add(i);
            expected.Add(i);
        }

        var secondEstimate = second.GetEstimateCount();
        first.UnionWith(second);

        for (var i = 0; i < 5_000; i++)
        {
            Assert.True(first.MayContain(i));
        }

        // Same set bits as a filter built from both sets: same answers for absent values and the same bit count
        for (var i = 5_000; i < 20_000; i++)
        {
            Assert.Equal(expected.MayContain(i), first.MayContain(i));
        }

        Assert.Equal(expected.GetEstimateCount(), first.GetEstimateCount());
        Assert.Equal(secondEstimate, second.GetEstimateCount());
    }

    [Theory]
    [MemberData(nameof(AllAlgorithmsAndValues))]
    public void UnionWith_ForAllAlgorithmsAndTypes_KeepsValuesOfBothFilters(string createMethodName, object value)
    {
        var size = BloomFilterSize.CreateOptimalSize(1000, 0.01);
        var destination = CreateBloomFilter(createMethodName, size);
        var source = CreateBloomFilter(createMethodName, size);
        ((IBloomFilter)destination).Add("destination-value");
        AddValue((IBloomFilter)source, value);

        destination.UnionWith(source);

        Assert.True(((IBloomFilter)destination).MayContain("destination-value"));
        Assert.True(MayContainValue((IBloomFilter)destination, value));
    }

    [Fact]
    public void UnionWith_EmptySource_LeavesDestinationUnchanged()
    {
        var size = BloomFilterSize.CreateOptimalSize(1000, 0.01);
        var destination = BloomFilter.CreateXXHash3(size);
        for (var i = 0; i < 500; i++)
        {
            destination.Add(i);
        }

        var estimate = destination.GetEstimateCount();
        destination.UnionWith(BloomFilter.CreateXXHash3(size));

        Assert.Equal(estimate, destination.GetEstimateCount());
    }

    [Fact]
    public void UnionWith_EmptyDestination_CopiesSource()
    {
        var size = BloomFilterSize.CreateOptimalSize(1000, 0.01);
        var source = BloomFilter.CreateXXHash3(size);
        for (var i = 0; i < 500; i++)
        {
            source.Add(i);
        }

        var destination = BloomFilter.CreateXXHash3(size);
        destination.UnionWith(source);

        Assert.Equal(source.GetEstimateCount(), destination.GetEstimateCount());
        for (var i = 0; i < 10_000; i++)
        {
            Assert.Equal(source.MayContain(i), destination.MayContain(i));
        }
    }

    [Fact]
    public void UnionWith_BothEmpty_StaysEmpty()
    {
        var size = BloomFilterSize.CreateOptimalSize(1000, 0.01);
        var destination = BloomFilter.CreateXXHash3(size);

        destination.UnionWith(BloomFilter.CreateXXHash3(size));

        Assert.Equal(0d, destination.GetEstimateCount());
    }

    [Fact]
    public void UnionWith_Self_IsNoOp()
    {
        var filter = BloomFilter.CreateXXHash64(BloomFilterSize.CreateOptimalSize(1000, 0.01));
        for (var i = 0; i < 500; i++)
        {
            filter.Add(i);
        }

        var estimate = filter.GetEstimateCount();
        filter.UnionWith(filter);

        Assert.Equal(estimate, filter.GetEstimateCount());
    }

    [Fact]
    public void UnionWith_Twice_IsIdempotent()
    {
        var size = BloomFilterSize.CreateOptimalSize(1000, 0.01);
        var destination = BloomFilter.CreateXXHash64(size);
        var source = BloomFilter.CreateXXHash64(size);
        for (var i = 0; i < 500; i++)
        {
            destination.Add(i);
            source.Add(-i - 1);
        }

        destination.UnionWith(source);
        var estimate = destination.GetEstimateCount();
        destination.UnionWith(source);

        Assert.Equal(estimate, destination.GetEstimateCount());
    }

    [Fact]
    public void UnionWith_ConcurrentAddsToDestination_AreKept()
    {
        var size = BloomFilterSize.CreateOptimalSize(200_000, 0.01);
        var destination = BloomFilter.CreateXXHash128(size);
        var source = BloomFilter.CreateXXHash128(size);
        for (var i = 0; i < 100_000; i++)
        {
            source.Add(i);
        }

        Parallel.Invoke(
            () => Parallel.For(100_000, 200_000, i => destination.Add(i)),
            () => destination.UnionWith(source));

        for (var i = 0; i < 200_000; i++)
        {
            Assert.True(destination.MayContain(i));
        }
    }

    [Fact]
    public void UnionWith_Null_Throws()
    {
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(1000, 0.01));

        Assert.Throws<ArgumentNullException>("other", () => filter.UnionWith(null!));
    }

    [Theory]
    [InlineData("CreateXXHash64", 1000L, 3)]
    [InlineData("CreateXXHash128", 1001L, 3)]
    [InlineData("CreateXXHash128", 1000L, 4)]
    public void UnionWith_IncompatibleFilter_ThrowsAndLeavesDestinationUnchanged(string sourceCreateMethodName, long sourceBitCount, int sourceHashCount)
    {
        var destination = BloomFilter.CreateXXHash128(BloomFilterSize.CreateExact(1000, 3));
        var source = CreateBloomFilter(sourceCreateMethodName, BloomFilterSize.CreateExact(sourceBitCount, sourceHashCount));
        for (var i = 0; i < 100; i++)
        {
            ((IBloomFilter)source).Add(i);
        }

        Assert.Throws<ArgumentException>("other", () => destination.UnionWith(source));

        Assert.Equal(0d, destination.GetEstimateCount());
    }

    [Fact]
    public void SegmentedBitStorage_UnionWith_CombinesEverySegment()
    {
        // A real filter only gets a second segment past 2^36 bits, so the segments are built by hand
        ulong[][] destination = [[0b0001, 0, ulong.MaxValue], [0, 0b1000], [0x8000_0000_0000_0000]];
        ulong[][] source = [[0b0010, 0, 0b0110], [0xFF00, 0], [1]];

        SegmentedBitStorage.UnionWith(destination, source);

        Assert.Equal<ulong[][]>([[0b0011, 0, ulong.MaxValue], [0xFF00, 0b1000], [0x8000_0000_0000_0001]], destination);
        Assert.Equal<ulong[][]>([[0b0010, 0, 0b0110], [0xFF00, 0], [1]], source);
    }

    [Fact]
    public void Documentation_CountingSaturation()
    {
        #region CountingBloomFilter_Saturation
        var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));

        filter.Add("bob@example.com");
        filter.Remove("bob@example.com");
        Assert.False(filter.MayContain("bob@example.com"));

        // Removing a value that was never added is ignored when the filter knows it is absent
        filter.Remove("carol@example.com");

        // Counters stop at 255: past that, the count is not tracked and the value cannot be removed
        for (var i = 0; i < 300; i++)
        {
            filter.Add("alice@example.com");
        }

        Assert.Equal(255, filter.GetEstimatedCount("alice@example.com"));
        filter.Remove("alice@example.com");
        Assert.True(filter.MayContain("alice@example.com"));
        #endregion
    }

    [Theory]
    [MemberData(nameof(AllAlgorithmsAndValues))]
    public void Add_ThenMayContain_ReturnsTrue_ForAllAlgorithmsAndTypes(string createMethodName, object value)
    {
        var size = BloomFilterSize.CreateOptimalSize(1000, 0.01);
        var filter = (IBloomFilter)typeof(BloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [size])!;
        AddValue(filter, value);
        Assert.True(MayContainValue(filter, value));
    }

    [Theory]
    [MemberData(nameof(AllCountingAlgorithmsAndValues))]
    public void AddRemoveAndGetEstimatedCount_ForAllAlgorithmsAndTypes(string createMethodName, object value)
    {
        var size = CountingBloomFilterSize.CreateOptimalSize(1000, 0.01);
        var filter = (ICountingBloomFilter)typeof(CountingBloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [size])!;

        AddValue(filter, value);
        AddValue(filter, value);

        Assert.True(MayContainValue(filter, value));
        Assert.True(GetEstimatedCountValue(filter, value) >= 2);

        RemoveValue(filter, value);
        Assert.True(MayContainValue(filter, value));
        Assert.True(GetEstimatedCountValue(filter, value) >= 1);

        RemoveValue(filter, value);
        Assert.False(MayContainValue(filter, value));
        Assert.Equal(0, GetEstimatedCountValue(filter, value));
    }

    [Fact]
    public void CountingBloomFilter_ReadOnlySpanOfByte_AddRemoveAndGetEstimatedCount_ForAllAlgorithms()
    {
        var size = CountingBloomFilterSize.CreateOptimalSize(1000, 0.01);
        ReadOnlySpan<byte> value = [1, 2, 3, 4];

        foreach (var method in typeof(CountingBloomFilter).GetMethods().Where(m => m.Name.StartsWith("Create", StringComparison.Ordinal) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(CountingBloomFilterSize)))
        {
            var filter = (ICountingBloomFilter)method.Invoke(null, [size])!;

            filter.Add(value);
            Assert.True(filter.MayContain(value));
            Assert.True(filter.GetEstimatedCount(value) >= 1);

            filter.Remove(value);
            Assert.False(filter.MayContain(value));
            Assert.Equal(0, filter.GetEstimatedCount(value));
        }
    }

    [Fact]
    public void GetEstimateCount_EmptyFilter_ReturnsZero()
    {
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(1000, 0.01));

        Assert.Equal(0, filter.GetEstimateCount());
    }

    [Fact]
    public void GetEstimateCount_FullFilter_ReturnsPositiveInfinity()
    {
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateExact(1, 1));
        filter.Add(0);

        Assert.Equal(double.PositiveInfinity, filter.GetEstimateCount());
    }

    [Fact]
    public void GetEstimateCount_PopulatedFilter_ReturnsApproximateCount()
    {
        const int ItemCount = 1000;
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(ItemCount, 0.01));
        for (var value = 0; value < ItemCount; value++)
        {
            filter.Add(value);
        }

        Assert.InRange(filter.GetEstimateCount(), ItemCount * 0.95, ItemCount * 1.05);
    }

    [Theory]
    [InlineData(nameof(BloomFilter.CreateXXHash128))]
    [InlineData(nameof(BloomFilter.CreateXXHash64))]
    [InlineData(nameof(BloomFilter.CreateXXHash32))]
    [InlineData(nameof(BloomFilter.CreateXXHash3))]
    [InlineData(nameof(BloomFilter.CreateCrc64))]
    [InlineData(nameof(BloomFilter.CreateCrc32))]
    public void BloomFilter_FalsePositiveRate_StaysNearTarget(string createMethodName)
    {
        const int ItemCount = 10_000;
        const int ProbeCount = 100_000;
        const double FalsePositiveProbability = 0.01;

        var size = BloomFilterSize.CreateOptimalSize(ItemCount, FalsePositiveProbability);
        var filter = (IBloomFilter)typeof(BloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [size])!;
        for (var value = 0; value < ItemCount; value++)
        {
            filter.Add(value);
        }

        var falsePositiveCount = 0;
        for (var value = ItemCount; value < ItemCount + ProbeCount; value++)
        {
            if (filter.MayContain(value))
            {
                falsePositiveCount++;
            }
        }

        Assert.InRange((double)falsePositiveCount / ProbeCount, 0, FalsePositiveProbability * 3);

        // Too few reachable positions also skews the set-bit count the estimate is derived from.
        Assert.InRange(filter.GetEstimateCount(), ItemCount * 0.9, ItemCount * 1.1);
    }

    [Theory]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash32))]
    [InlineData(nameof(CountingBloomFilter.CreateCrc32))]
    public void CountingBloomFilter32_FalsePositiveRate_StaysNearTarget(string createMethodName)
    {
        const int ItemCount = 10_000;
        const int ProbeCount = 100_000;
        const double FalsePositiveProbability = 0.01;

        var size = CountingBloomFilterSize.CreateOptimalSize(ItemCount, FalsePositiveProbability);
        var filter = (ICountingBloomFilter)typeof(CountingBloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [size])!;
        for (var value = 0; value < ItemCount; value++)
        {
            filter.Add(value);
        }

        var falsePositiveCount = 0;
        for (var value = ItemCount; value < ItemCount + ProbeCount; value++)
        {
            if (filter.MayContain(value))
            {
                falsePositiveCount++;
            }
        }

        Assert.InRange((double)falsePositiveCount / ProbeCount, 0, FalsePositiveProbability * 3);
        Assert.InRange(filter.GetEstimatedCount(0), 1, 5);
    }

    [Fact]
    public void XXHash32_AddRange_ThenMayContain_ReturnsTrue()
    {
        var filter = BloomFilter.CreateXXHash32(BloomFilterSize.CreateOptimalSize(1000, 0.01));
        var signedValues = Enumerable.Range(-50, 100).Append(int.MinValue).Append(int.MaxValue).ToArray();
        var unsignedValues = Enumerable.Range(0, 100).Select(value => (uint)value).Append(uint.MaxValue).ToArray();

        filter.AddRange(signedValues);
        filter.AddRange(unsignedValues);

        Assert.All(signedValues, value => Assert.True(filter.MayContain(value)));
        Assert.All(unsignedValues, value => Assert.True(filter.MayContain(value)));
    }

    [Fact]
    public void XXHash64_AddRange_ThenMayContain_ReturnsTrue()
    {
        var filter = BloomFilter.CreateXXHash64(BloomFilterSize.CreateOptimalSize(1000, 0.01));
        var signedValues = Enumerable.Range(-50, 100).Select(value => (long)value).Append(long.MinValue).Append(long.MaxValue).ToArray();
        var unsignedValues = Enumerable.Range(0, 100).Select(value => (ulong)value).Append(ulong.MaxValue).ToArray();

        filter.AddRange(signedValues);
        filter.AddRange(unsignedValues);

        Assert.All(signedValues, value => Assert.True(filter.MayContain(value)));
        Assert.All(unsignedValues, value => Assert.True(filter.MayContain(value)));
    }

    [Fact]
    public void XXHash128_AddRange_ThenMayContain_ReturnsTrue()
    {
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(1000, 0.01));
        var signedValues = Enumerable.Range(-50, 100).Select(value => (long)value).Append(long.MinValue).Append(long.MaxValue).ToArray();
        var unsignedValues = Enumerable.Range(0, 100).Select(value => (ulong)value).Append(ulong.MaxValue).ToArray();

        filter.AddRange(signedValues);
        filter.AddRange(unsignedValues);

        Assert.All(signedValues, value => Assert.True(filter.MayContain(value)));
        Assert.All(unsignedValues, value => Assert.True(filter.MayContain(value)));
    }

    [Fact]
    public void CountingBloomFilter_RemoveAtZero_DoesNotUnderflow()
    {
        var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateOptimalSize(1000, 0.01));

        filter.Remove("value");
        filter.Add("value");

        Assert.True(filter.MayContain("value"));
        Assert.True(filter.GetEstimatedCount("value") >= 1);
    }

    [Fact]
    public void CountingBloomFilter_RepeatedCounterPositions_PreserveUpdates()
    {
        var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateExact(counterCount: 1, hashCount: 3));

        filter.Add("value");
        filter.Add("value");

        Assert.Equal(6, filter.GetEstimatedCount("value"));

        filter.Remove("value");
        Assert.Equal(3, filter.GetEstimatedCount("value"));

        filter.Remove("value");
        Assert.Equal(0, filter.GetEstimatedCount("value"));
    }

    [Fact]
    public void CountingBloomFilter_ConcurrentAddAndRemove_UpdatesAtomically()
    {
        var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateExact(counterCount: 1, hashCount: 1));

        // Stay below the counter saturation value, which would make the counter sticky
        Parallel.For(0, 250, _ => filter.Add("value"));
        Assert.Equal(250, filter.GetEstimatedCount("value"));

        Parallel.For(0, 250, _ => filter.Remove("value"));
        Assert.Equal(0, filter.GetEstimatedCount("value"));
    }

    [Fact]
    public void CountingBloomFilter_SaturatedCounter_StaysSaturated()
    {
        var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateExact(counterCount: 1, hashCount: 1));

        for (var i = 0; i < 300; i++)
        {
            filter.Add("value");
        }

        Assert.Equal(byte.MaxValue, filter.GetEstimatedCount("value"));

        // The real count of a saturated counter is unknown, so decrementing it could create a false negative
        for (var i = 0; i < 300; i++)
        {
            filter.Remove("value");
        }

        Assert.Equal(byte.MaxValue, filter.GetEstimatedCount("value"));
        Assert.True(filter.MayContain("value"));
    }

    [Fact]
    public void CountingBloomFilter_RemoveAbsentValue_LeavesCountersUnchanged()
    {
        const int ItemCount = 1_000;

        var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateOptimalSize(ItemCount, 0.01));
        for (var value = 0; value < ItemCount; value++)
        {
            filter.Add(value);
        }

        var expectedCounts = Enumerable.Range(0, ItemCount).Select(filter.GetEstimatedCount).ToArray();

        // These values have at least one empty counter, so the filter knows they were never added. Their other
        // counters belong to the values above and must not be decremented.
        var absentValues = Enumerable.Range(ItemCount, 10 * ItemCount).Where(value => !filter.MayContain(value)).ToArray();
        Assert.NotEmpty(absentValues);
        foreach (var value in absentValues)
        {
            filter.Remove(value);
        }

        Assert.Equal(expectedCounts, Enumerable.Range(0, ItemCount).Select(filter.GetEstimatedCount).ToArray());
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(42u)]
    [InlineData(uint.MaxValue)]
    public void BloomFilterHash_FromUInt32_ReachesPositionsBeyond32Bits(uint seed)
    {
        AssertReachesPositionsBeyond32Bits(value => BloomFilterHash.FromUInt32(unchecked(seed * (uint)value)));
    }

    [Theory]
    [InlineData(1ul)]
    [InlineData(42ul)]
    [InlineData(ulong.MaxValue)]
    public void BloomFilterHash_FromUInt64_ReachesPositionsBeyond32Bits(ulong seed)
    {
        AssertReachesPositionsBeyond32Bits(value => BloomFilterHash.FromUInt64(unchecked(seed * 0x9E3779B97F4A7C15UL * (ulong)value)));
    }

    private static void AssertReachesPositionsBeyond32Bits(Func<int, BloomFilterHash> createHash)
    {
        // With 32-bit hash halves, a 2^40-bit filter can only reach multiples of 2^8, whatever the input
        const ulong Range = 1UL << 40;
        var positionsOffLattice = 0;
        for (var value = 1; value <= 1_000; value++)
        {
            var hash = createHash(value);
            var combined = hash.Hash1;
            for (var i = 0; i < 7; i++)
            {
                if (BloomFilterHash.Reduce(combined, Range) % 256 != 0)
                {
                    positionsOffLattice++;
                }

                combined += hash.Hash2;
            }
        }

        Assert.InRange(positionsOffLattice, 6_900, 7_000);
    }

    [Fact]
    public void CountingXXHash32_AddRangeAndRemoveRange_UpdatesValues()
    {
        var filter = CountingBloomFilter.CreateXXHash32(CountingBloomFilterSize.CreateOptimalSize(1000, 0.01));
        var signedValues = Enumerable.Range(-50, 100).Append(int.MinValue).Append(int.MaxValue).ToArray();
        var unsignedValues = Enumerable.Range(0, 100).Select(value => (uint)value).Append(uint.MaxValue).ToArray();

        filter.AddRange(signedValues);
        filter.AddRange(unsignedValues);
        Assert.All(signedValues, value => Assert.True(filter.MayContain(value)));
        Assert.All(unsignedValues, value => Assert.True(filter.MayContain(value)));

        filter.RemoveRange(signedValues);
        filter.RemoveRange(unsignedValues);
        Assert.All(signedValues, value => Assert.False(filter.MayContain(value)));
        Assert.All(unsignedValues, value => Assert.False(filter.MayContain(value)));
    }

    [Fact]
    public void CountingXXHash64_AddRangeAndRemoveRange_UpdatesValues()
    {
        var filter = CountingBloomFilter.CreateXXHash64(CountingBloomFilterSize.CreateOptimalSize(1000, 0.01));
        var signedValues = Enumerable.Range(-50, 100).Select(value => (long)value).Append(long.MinValue).Append(long.MaxValue).ToArray();
        var unsignedValues = Enumerable.Range(0, 100).Select(value => (ulong)value).Append(ulong.MaxValue).ToArray();

        filter.AddRange(signedValues);
        filter.AddRange(unsignedValues);
        Assert.All(signedValues, value => Assert.True(filter.MayContain(value)));
        Assert.All(unsignedValues, value => Assert.True(filter.MayContain(value)));

        filter.RemoveRange(signedValues);
        filter.RemoveRange(unsignedValues);
        Assert.All(signedValues, value => Assert.False(filter.MayContain(value)));
        Assert.All(unsignedValues, value => Assert.False(filter.MayContain(value)));
    }

    [Fact]
    public void CountingXXHash128_AddRangeAndRemoveRange_UpdatesValues()
    {
        var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateOptimalSize(1000, 0.01));
        var signedValues = Enumerable.Range(-50, 100).Select(value => (long)value).Append(long.MinValue).Append(long.MaxValue).ToArray();
        var unsignedValues = Enumerable.Range(0, 100).Select(value => (ulong)value).Append(ulong.MaxValue).ToArray();

        filter.AddRange(signedValues);
        filter.AddRange(unsignedValues);
        Assert.All(signedValues, value => Assert.True(filter.MayContain(value)));
        Assert.All(unsignedValues, value => Assert.True(filter.MayContain(value)));

        filter.RemoveRange(signedValues);
        filter.RemoveRange(unsignedValues);
        Assert.All(signedValues, value => Assert.False(filter.MayContain(value)));
        Assert.All(unsignedValues, value => Assert.False(filter.MayContain(value)));
    }

    [Theory]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash128))]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash64))]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash3))]
    [InlineData(nameof(CountingBloomFilter.CreateCrc64))]
    public void CountingBloomFilter_FalsePositiveRate_StaysNearTarget(string createMethodName)
    {
        const int ItemCount = 10_000;
        const int ProbeCount = 100_000;
        const double FalsePositiveProbability = 0.01;

        var size = CountingBloomFilterSize.CreateOptimalSize(ItemCount, FalsePositiveProbability);
        var filter = (ICountingBloomFilter)typeof(CountingBloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [size])!;
        for (var value = 0; value < ItemCount; value++)
        {
            filter.Add(value);
        }

        var falsePositiveCount = 0;
        for (var value = ItemCount; value < ItemCount + ProbeCount; value++)
        {
            if (filter.MayContain(value))
            {
                falsePositiveCount++;
            }
        }

        Assert.InRange((double)falsePositiveCount / ProbeCount, 0, FalsePositiveProbability * 3);

        // A value inserted once must not be reported as inserted thousands of times. When every hash
        // reduces to the same counter, this returns the total number of increments issued instead.
        Assert.InRange(filter.GetEstimatedCount(0), 1, 5);
    }

    [Theory]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash128))]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash64))]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash32))]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash3))]
    [InlineData(nameof(CountingBloomFilter.CreateCrc64))]
    [InlineData(nameof(CountingBloomFilter.CreateCrc32))]
    [InlineData(nameof(CountingBloomFilter.CreateAdler32))]
    public void CountingBloomFilter_MayContain_MatchesGetEstimatedCount(string createMethodName)
    {
        const int ItemCount = 1_000;

        var size = CountingBloomFilterSize.CreateOptimalSize(ItemCount, 0.01);
        var filter = (ICountingBloomFilter)typeof(CountingBloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [size])!;
        for (var value = 0; value < ItemCount; value++)
        {
            filter.Add(value);
        }

        for (var value = 0; value < ItemCount * 5; value++)
        {
            Assert.Equal(filter.GetEstimatedCount(value) > 0, filter.MayContain(value));
        }
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(0.1, 3)]
    [InlineData(0.01, 7)]
    [InlineData(0.001, 10)]
    public void CreateOptimalSize_PicksHashCountWithLowestFalsePositiveRate(double falsePositiveProbability, int expectedHashCount)
    {
        Assert.Equal(expectedHashCount, BloomFilterSize.CreateOptimalSize(1000, falsePositiveProbability).HashCount);
        Assert.Equal(expectedHashCount, CountingBloomFilterSize.CreateOptimalSize(1000, falsePositiveProbability).HashCount);
    }

    [Fact]
    public void CountingBloomFilterSize_CreateOptimalSize_MatchesBloomFilterSize()
    {
        var bloomFilterSize = BloomFilterSize.CreateOptimalSize(1000, 0.01);
        var countingBloomFilterSize = CountingBloomFilterSize.CreateOptimalSize(1000, 0.01);

        Assert.Equal(bloomFilterSize.BitCount, countingBloomFilterSize.CounterCount);
        Assert.Equal(bloomFilterSize.HashCount, countingBloomFilterSize.HashCount);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void CountingBloomFilterSize_CreateExact_ValidatesArguments(long counterCount, int hashCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CountingBloomFilterSize.CreateExact(counterCount, hashCount));
    }

    [Theory]
    [InlineData(0, 0.01)]
    [InlineData(-1, 0.01)]
    [InlineData(1, 0)]
    [InlineData(1, -0.01)]
    [InlineData(1, 1)]
    [InlineData(1, 1.01)]
    public void CountingBloomFilterSize_CreateOptimalSize_ValidatesArguments(long expectedItemCount, double falsePositiveProbability)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CountingBloomFilterSize.CreateOptimalSize(expectedItemCount, falsePositiveProbability));
    }

    [Theory]
    [InlineData(2_000_000_000_000_000_000L, 0.01)]
    [InlineData(long.MaxValue, 0.01)]
    [InlineData(long.MaxValue, 0.5)]
    public void CreateOptimalSize_SizeExceedingLongMaxValue_Throws(long expectedItemCount, double falsePositiveProbability)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BloomFilterSize.CreateOptimalSize(expectedItemCount, falsePositiveProbability));
        Assert.Throws<ArgumentOutOfRangeException>(() => CountingBloomFilterSize.CreateOptimalSize(expectedItemCount, falsePositiveProbability));
    }

    [Fact]
    public void CreateOptimalSize_LargestSupportedItemCount_KeepsHashCountCorrect()
    {
        const long ExpectedItemCount = 900_000_000_000_000_000L;

        var size = BloomFilterSize.CreateOptimalSize(ExpectedItemCount, 0.01);
        var countingSize = CountingBloomFilterSize.CreateOptimalSize(ExpectedItemCount, 0.01);

        Assert.Equal(7, size.HashCount);
        Assert.Equal(7, countingSize.HashCount);
        Assert.InRange(size.BitCount, ExpectedItemCount * 9, ExpectedItemCount * 10);
        Assert.Equal(size.BitCount, countingSize.CounterCount);
    }

    private static IEnumerable<object> GetValues()
    {
        return
        [
            0,
            1,
            -1,
            int.MinValue,
            int.MaxValue,
            (uint)0,
            (uint)1,
            uint.MaxValue,
            0x80000000U,
            1234567890U,
            0L,
            1L,
            -1L,
            long.MinValue,
            long.MaxValue,
            0UL,
            1UL,
            ulong.MaxValue,
            0x8000000000000000UL,
            12345678901234567890UL,
            Guid.Empty,
            Guid.Parse("8f14e45f-ea9d-4a6f-b5a6-6de7f5b91c3d"),
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            string.Empty,
            "a",
            "hello",
            "The quick brown fox jumps over the lazy dog",
            new string('x', 257),
            (UInt128)0,
            (UInt128)1,
            UInt128.MaxValue,
            ((UInt128)ulong.MaxValue << 64) | 0x1234567890ABCDEFUL,
            (Int128)0,
            (Int128)1,
            (Int128)(-1),
            Int128.MinValue,
            Int128.MaxValue,
            (Int128)1234567890123456789,
            (Int128)(-1234567890123456789),
        ];
    }

    private static BloomFilter CreateBloomFilter(string createMethodName, BloomFilterSize size)
    {
        return (BloomFilter)typeof(BloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [size])!;
    }

    private static void AddValue(IBloomFilter filter, object value)
    {
        switch (value)
        {
            case int v:
                filter.Add(v);
                break;
            case uint v:
                filter.Add(v);
                break;
            case long v:
                filter.Add(v);
                break;
            case ulong v:
                filter.Add(v);
                break;
            case Guid v:
                filter.Add(v);
                break;
            case string v:
                filter.Add(v);
                break;
            case UInt128 v:
                filter.Add(v);
                break;
            case Int128 v:
                filter.Add(v);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static bool MayContainValue(IBloomFilter filter, object value)
    {
        return value switch
        {
            int v => filter.MayContain(v),
            uint v => filter.MayContain(v),
            long v => filter.MayContain(v),
            ulong v => filter.MayContain(v),
            Guid v => filter.MayContain(v),
            string v => filter.MayContain(v),
            UInt128 v => filter.MayContain(v),
            Int128 v => filter.MayContain(v),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static void AddValue(ICountingBloomFilter filter, object value)
    {
        switch (value)
        {
            case int v:
                filter.Add(v);
                break;
            case uint v:
                filter.Add(v);
                break;
            case long v:
                filter.Add(v);
                break;
            case ulong v:
                filter.Add(v);
                break;
            case Guid v:
                filter.Add(v);
                break;
            case string v:
                filter.Add(v);
                break;
            case UInt128 v:
                filter.Add(v);
                break;
            case Int128 v:
                filter.Add(v);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static void RemoveValue(ICountingBloomFilter filter, object value)
    {
        switch (value)
        {
            case int v:
                filter.Remove(v);
                break;
            case uint v:
                filter.Remove(v);
                break;
            case long v:
                filter.Remove(v);
                break;
            case ulong v:
                filter.Remove(v);
                break;
            case Guid v:
                filter.Remove(v);
                break;
            case string v:
                filter.Remove(v);
                break;
            case UInt128 v:
                filter.Remove(v);
                break;
            case Int128 v:
                filter.Remove(v);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static bool MayContainValue(ICountingBloomFilter filter, object value)
    {
        return value switch
        {
            int v => filter.MayContain(v),
            uint v => filter.MayContain(v),
            long v => filter.MayContain(v),
            ulong v => filter.MayContain(v),
            Guid v => filter.MayContain(v),
            string v => filter.MayContain(v),
            UInt128 v => filter.MayContain(v),
            Int128 v => filter.MayContain(v),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    private static int GetEstimatedCountValue(ICountingBloomFilter filter, object value)
    {
        return value switch
        {
            int v => filter.GetEstimatedCount(v),
            uint v => filter.GetEstimatedCount(v),
            long v => filter.GetEstimatedCount(v),
            ulong v => filter.GetEstimatedCount(v),
            Guid v => filter.GetEstimatedCount(v),
            string v => filter.GetEstimatedCount(v),
            UInt128 v => filter.GetEstimatedCount(v),
            Int128 v => filter.GetEstimatedCount(v),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }
}
