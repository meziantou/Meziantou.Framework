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

    [Fact]
    public void Documentation_Snapshot()
    {
        #region BloomFilter_Snapshot
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));
        filter.Add("alice@example.com");

        // Save the filter to a stream, such as a FileStream, and load it after a restart
        using var stream = new MemoryStream();
        filter.Save(stream);

        stream.Position = 0;
        var restored = BloomFilter.Load<BloomFilterXXHash128>(stream);

        Assert.True(restored.MayContain("alice@example.com"));
        Assert.False(restored.MayContain("bob@example.com"));
        #endregion
    }

    [Fact]
    public async Task Documentation_CountingSnapshot()
    {
        #region CountingBloomFilter_Snapshot
        var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));
        filter.Add("alice@example.com");
        filter.Add("alice@example.com");

        using var stream = new MemoryStream();
        await filter.SaveAsync(stream);

        stream.Position = 0;
        var restored = await CountingBloomFilter.LoadAsync<CountingBloomFilterXXHash128>(stream);

        Assert.Equal(2, restored.GetEstimatedCount("alice@example.com"));
        #endregion
    }

    public static TheoryData<string, long, bool> SnapshotRoundTripData()
    {
        var data = new TheoryData<string, long, bool>();
        foreach (var method in GetCreateMethodNames())
        {
            foreach (var positionCount in (long[])[1, 63, 64, 65, 1000, 70_001])
            {
                data.Add(method, positionCount, false);
                data.Add(method, positionCount, true);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SnapshotRoundTripData))]
    public async Task Snapshot_BloomFilter_RoundTrip(string createMethodName, long bitCount, bool populated)
    {
        var filter = (BloomFilter)typeof(BloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [BloomFilterSize.CreateExact(bitCount, 3)])!;
        var values = (IBloomFilter)filter;
        if (populated)
        {
            foreach (var value in GetValues())
            {
                AddValue(values, value);
            }

            for (var i = 0; i < 500; i++)
            {
                values.Add(i);
            }
        }

        using var stream = new MemoryStream();
        filter.Save(stream);
        var snapshot = stream.ToArray();
        Assert.Equal(32 + ((((bitCount - 1) / 64) + 1) * 8) + 8, snapshot.LongLength);

        stream.Position = 0;
        var restored = BloomFilter.Load(stream);
        Assert.Equal(stream.Length, stream.Position);
        AssertSameBloomFilter(filter, restored, snapshot);

        using var asyncStream = new NonSeekableStream(snapshot);
        var restoredAsync = await BloomFilter.LoadAsync(asyncStream);
        AssertSameBloomFilter(filter, restoredAsync, snapshot);

        using var asyncSaveStream = new MemoryStream();
        await filter.SaveAsync(asyncSaveStream);
        Assert.Equal(snapshot, asyncSaveStream.ToArray());

        static void AssertSameBloomFilter(BloomFilter expected, BloomFilter actual, byte[] snapshot)
        {
            Assert.Equal(expected.GetType(), actual.GetType());
            Assert.Equal(expected.GetEstimateCount(), actual.GetEstimateCount());

            var expectedValues = (IBloomFilter)expected;
            var actualValues = (IBloomFilter)actual;
            foreach (var value in GetValues())
            {
                Assert.Equal(MayContainValue(expectedValues, value), MayContainValue(actualValues, value));
            }

            for (var i = 0; i < 2000; i++)
            {
                Assert.Equal(expectedValues.MayContain(i), actualValues.MayContain(i));
            }

            using var stream = new MemoryStream();
            actual.Save(stream);
            Assert.Equal(snapshot, stream.ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(SnapshotRoundTripData))]
    public async Task Snapshot_CountingBloomFilter_RoundTrip(string createMethodName, long counterCount, bool populated)
    {
        var filter = (CountingBloomFilter)typeof(CountingBloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [CountingBloomFilterSize.CreateExact(counterCount, 3)])!;
        var values = (ICountingBloomFilter)filter;
        if (populated)
        {
            foreach (var value in GetValues())
            {
                AddValue(values, value);
                AddValue(values, value);
            }

            for (var i = 0; i < 500; i++)
            {
                values.Add(i);
            }

            // Saturate the counters of a value
            for (var i = 0; i < 300; i++)
            {
                values.Add("saturated");
            }

            values.Remove(10);
        }

        using var stream = new MemoryStream();
        filter.Save(stream);
        var snapshot = stream.ToArray();
        Assert.Equal(32 + counterCount + 8, snapshot.LongLength);

        stream.Position = 0;
        var restored = CountingBloomFilter.Load(stream);
        Assert.Equal(stream.Length, stream.Position);
        AssertSameCountingBloomFilter(filter, restored, snapshot);

        using var asyncStream = new NonSeekableStream(snapshot);
        var restoredAsync = await CountingBloomFilter.LoadAsync(asyncStream);
        AssertSameCountingBloomFilter(filter, restoredAsync, snapshot);

        using var asyncSaveStream = new MemoryStream();
        await filter.SaveAsync(asyncSaveStream);
        Assert.Equal(snapshot, asyncSaveStream.ToArray());

        if (populated)
        {
            var restoredValues = (ICountingBloomFilter)restored;
            Assert.Equal(255, restoredValues.GetEstimatedCount("saturated"));
            restoredValues.Remove("saturated");
            Assert.Equal(255, restoredValues.GetEstimatedCount("saturated"));
        }

        static void AssertSameCountingBloomFilter(CountingBloomFilter expected, CountingBloomFilter actual, byte[] snapshot)
        {
            Assert.Equal(expected.GetType(), actual.GetType());

            var expectedValues = (ICountingBloomFilter)expected;
            var actualValues = (ICountingBloomFilter)actual;
            foreach (var value in GetValues().Append("saturated"))
            {
                Assert.Equal(MayContainValue(expectedValues, value), MayContainValue(actualValues, value));
                Assert.Equal(GetEstimatedCountValue(expectedValues, value), GetEstimatedCountValue(actualValues, value));
            }

            for (var i = 0; i < 2000; i++)
            {
                Assert.Equal(expectedValues.MayContain(i), actualValues.MayContain(i));
                Assert.Equal(expectedValues.GetEstimatedCount(i), actualValues.GetEstimatedCount(i));
            }

            using var stream = new MemoryStream();
            actual.Save(stream);
            Assert.Equal(snapshot, stream.ToArray());
        }
    }

    [Fact]
    public void Snapshot_LoadTyped_ReturnsConcreteType()
    {
        var filter = BloomFilter.CreateCrc64(BloomFilterSize.CreateExact(100, 2));
        filter.Add(42);
        var snapshot = SaveSnapshot(filter);

        var restored = BloomFilter.Load<BloomFilterCrc64>(new MemoryStream(snapshot));

        Assert.True(restored.MayContain(42));
    }

    [Fact]
    public async Task Snapshot_LoadTyped_OtherAlgorithm_Throws()
    {
        var snapshot = SaveSnapshot(BloomFilter.CreateXXHash128(BloomFilterSize.CreateExact(100, 2)));

        var exception = Assert.Throws<InvalidDataException>(() => BloomFilter.Load<BloomFilterCrc32>(new MemoryStream(snapshot)));
        Assert.Contains("BloomFilterXXHash128", exception.Message, StringComparison.Ordinal);

        await Assert.Throws<InvalidDataException>(async () => await BloomFilter.LoadAsync<BloomFilterCrc32>(new MemoryStream(snapshot)));

        using var countingStream = new MemoryStream();
        CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateExact(100, 2)).Save(countingStream);
        Assert.Throws<InvalidDataException>(() => CountingBloomFilter.Load<CountingBloomFilterCrc32>(new MemoryStream(countingStream.ToArray())));
    }

    [Fact]
    public void Snapshot_KindMismatch_Throws()
    {
        var bloomSnapshot = SaveSnapshot(BloomFilter.CreateXXHash128(BloomFilterSize.CreateExact(100, 2)));
        using var countingStream = new MemoryStream();
        CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateExact(100, 2)).Save(countingStream);

        var exception = Assert.Throws<InvalidDataException>(() => BloomFilter.Load(new MemoryStream(countingStream.ToArray())));
        Assert.Contains("CountingBloomFilter.Load", exception.Message, StringComparison.Ordinal);

        exception = Assert.Throws<InvalidDataException>(() => CountingBloomFilter.Load(new MemoryStream(bloomSnapshot)));
        Assert.Contains("BloomFilter.Load", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_NullStream_Throws()
    {
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateExact(100, 2));
        var countingFilter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateExact(100, 2));

        Assert.Throws<ArgumentNullException>("stream", () => filter.Save(null!));
        Assert.Throws<ArgumentNullException>("stream", () => countingFilter.Save(null!));
        Assert.Throws<ArgumentNullException>("stream", () => BloomFilter.Load(null!));
        Assert.Throws<ArgumentNullException>("stream", () => CountingBloomFilter.Load(null!));
    }

    [Theory]
    [InlineData("Magic", "magic number")]
    [InlineData("FormatVersion", "format version 2")]
    [InlineData("UnknownKind", "unknown filter kind")]
    [InlineData("AlgorithmZero", "unknown hash algorithm")]
    [InlineData("AlgorithmTooLarge", "unknown hash algorithm")]
    [InlineData("HashDerivationVersion", "hash derivation version 2")]
    [InlineData("UnknownByteOrder", "unknown key byte order")]
    [InlineData("OtherByteOrder", "endian machine")]
    [InlineData("Reserved", "reserved byte")]
    [InlineData("ZeroHashCount", "invalid hash count")]
    [InlineData("NegativeHashCount", "invalid hash count")]
    [InlineData("ZeroSize", "invalid size")]
    [InlineData("NegativeSize", "invalid size")]
    [InlineData("PayloadLength", "payload length")]
    [InlineData("HugeSize", "truncated")]
    [InlineData("TruncatedHeader", "truncated")]
    [InlineData("TruncatedPayload", "truncated")]
    [InlineData("TruncatedChecksum", "truncated")]
    [InlineData("Checksum", "checksum")]
    [InlineData("PaddingBit", "past the size of the filter")]
    public async Task Snapshot_InvalidData_Throws(string corruption, string expectedMessage)
    {
        const long BitCount = 1000; // Not a multiple of 64: the last word has padding bits
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateExact(BitCount, 3));
        for (var i = 0; i < 100; i++)
        {
            filter.Add(i);
        }

        var snapshot = SaveSnapshot(filter);
        switch (corruption)
        {
            case "Magic":
                snapshot[0] = (byte)'X';
                break;
            case "FormatVersion":
                snapshot[4] = 2;
                break;
            case "UnknownKind":
                snapshot[6] = 3;
                break;
            case "AlgorithmZero":
                snapshot[7] = 0;
                break;
            case "AlgorithmTooLarge":
                snapshot[7] = 8;
                break;
            case "HashDerivationVersion":
                snapshot[8] = 2;
                break;
            case "UnknownByteOrder":
                snapshot[10] = 3;
                break;
            case "OtherByteOrder":
                snapshot[10] = BitConverter.IsLittleEndian ? (byte)2 : (byte)1;
                break;
            case "Reserved":
                snapshot[11] = 1;
                break;
            case "ZeroHashCount":
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(snapshot.AsSpan(12), 0);
                break;
            case "NegativeHashCount":
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(snapshot.AsSpan(12), -1);
                break;
            case "ZeroSize":
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(snapshot.AsSpan(16), 0);
                break;
            case "NegativeSize":
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(snapshot.AsSpan(16), -1);
                break;
            case "PayloadLength":
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(snapshot.AsSpan(24), 136);
                break;
            case "HugeSize":
                // A consistent header for a 1 EiB filter: the stream is too short, so nothing is allocated
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(snapshot.AsSpan(16), long.MaxValue);
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(snapshot.AsSpan(24), 1L << 60);
                break;
            case "TruncatedHeader":
                snapshot = snapshot[..10];
                break;
            case "TruncatedPayload":
                snapshot = snapshot[..^20];
                break;
            case "TruncatedChecksum":
                snapshot = snapshot[..^1];
                break;
            case "Checksum":
                snapshot[40] ^= 0x01;
                break;
            case "PaddingBit":
                snapshot[^9] |= 0x80; // The last bit of the last word
                System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(snapshot.AsSpan(snapshot.Length - 8), System.IO.Hashing.XxHash3.HashToUInt64(snapshot.AsSpan(0, snapshot.Length - 8)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }

        var exception = Assert.Throws<InvalidDataException>(() => BloomFilter.Load(new MemoryStream(snapshot)));
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);

        // A non-seekable stream detects a truncated payload only when it reaches its end
        if (corruption is not "HugeSize")
        {
            exception = await Assert.Throws<InvalidDataException>(async () => await BloomFilter.LoadAsync(new NonSeekableStream(snapshot)));
            Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Snapshot_CountingBloomFilter_Checksum_Throws()
    {
        var filter = CountingBloomFilter.CreateXXHash64(CountingBloomFilterSize.CreateExact(100, 3));
        filter.Add(1);
        using var stream = new MemoryStream();
        filter.Save(stream);
        var snapshot = stream.ToArray();
        snapshot[50] ^= 0x01;

        var exception = Assert.Throws<InvalidDataException>(() => CountingBloomFilter.Load(new MemoryStream(snapshot)));
        Assert.Contains("checksum", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] SaveSnapshot(BloomFilter filter)
    {
        using var stream = new MemoryStream();
        filter.Save(stream);
        return stream.ToArray();
    }

    private static string[] GetCreateMethodNames()
    {
        return
        [
            nameof(BloomFilter.CreateXXHash128),
            nameof(BloomFilter.CreateXXHash64),
            nameof(BloomFilter.CreateXXHash32),
            nameof(BloomFilter.CreateXXHash3),
            nameof(BloomFilter.CreateCrc64),
            nameof(BloomFilter.CreateCrc32),
            nameof(BloomFilter.CreateAdler32),
        ];
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

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
