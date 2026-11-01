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
        Assert.Equal(9_592_955, size.BitCount); // about 1.2 MB of memory
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

        // A value is identified by its bytes, not by its type. 42 as an int (4 bytes) and as a long (8 bytes)
        // are different values, but 42 as an int and as a uint have the same bytes, so they are the same value
        Assert.False(filter.MayContain(42L));
        Assert.True(filter.MayContain(42u));

        // A string is hashed from its UTF-16 characters, so it is a different value from its UTF-8 bytes
        Assert.False(filter.MayContain("alice@example.com"));
        #endregion
    }

    [Fact]
    public void Documentation_KeyPrefix()
    {
        #region BloomFilter_KeyPrefix
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));

        // Prefix the value with its kind, and write numbers with an explicit byte order,
        // so user 42 and order 42 are different values on every machine
        filter.Add(CreateKey(kind: 1, id: 42)); // user 42

        Assert.True(filter.MayContain(CreateKey(kind: 1, id: 42)));
        Assert.False(filter.MayContain(CreateKey(kind: 2, id: 42))); // order 42

        static byte[] CreateKey(byte kind, int id)
        {
            var key = new byte[5];
            key[0] = kind;
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(1), id);
            return key;
        }
        #endregion
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void BloomFilter_ValuesWithSameBytes_AreTheSameValue(string createMethodName)
    {
        var filter = (IBloomFilter)typeof(BloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [BloomFilterSize.CreateOptimalSize(1000, 0.01)])!;

        filter.Add(42);
        Assert.True(filter.MayContain(42u));
        Assert.True(filter.MayContain(BitConverter.GetBytes(42)));

        filter.Add(-1L);
        Assert.True(filter.MayContain(ulong.MaxValue));

        filter.Add("ab");
        Assert.True(filter.MayContain((BitConverter.IsLittleEndian ? System.Text.Encoding.Unicode : System.Text.Encoding.BigEndianUnicode).GetBytes("ab")));
    }

    [Theory]
    [MemberData(nameof(AllCountingAlgorithms))]
    public void CountingBloomFilter_ValuesWithSameBytes_AreTheSameValue(string createMethodName)
    {
        var filter = (ICountingBloomFilter)typeof(CountingBloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [CountingBloomFilterSize.CreateOptimalSize(1000, 0.01)])!;

        filter.Add(42);
        Assert.True(filter.MayContain(42u));
        Assert.True(filter.GetEstimatedCount(BitConverter.GetBytes(42)) >= 1);

        // Removing the uint removes the int: they are the same value
        filter.Remove(42u);
        Assert.False(filter.MayContain(42));

        filter.Add(-1);
        filter.Remove(BitConverter.GetBytes(uint.MaxValue));
        Assert.False(filter.MayContain(-1));
    }

    public static TheoryData<string> AllAlgorithms() => [.. typeof(BloomFilter).GetMethods().Where(m => m.Name.StartsWith("Create", StringComparison.Ordinal) && m.GetParameters() is [{ ParameterType: var type }] && type == typeof(BloomFilterSize)).Select(m => m.Name)];

    public static TheoryData<string> AllCountingAlgorithms() => [.. typeof(CountingBloomFilter).GetMethods().Where(m => m.Name.StartsWith("Create", StringComparison.Ordinal) && m.GetParameters() is [{ ParameterType: var type }] && type == typeof(CountingBloomFilterSize)).Select(m => m.Name)];

    [Fact]
    public void Documentation_CharSpan()
    {
        #region BloomFilter_CharSpan
        var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));

        // Add a part of a larger string without allocating a substring
        var line = "user=alice;role=admin";
        filter.Add(line.AsSpan(5, 5));

        // A span of chars and a string with the same characters are the same value
        Assert.True(filter.MayContain("alice"));

        // Characters from a stack buffer
        Span<char> buffer = stackalloc char[5];
        "alice".CopyTo(buffer);
        Assert.True(filter.MayContain(buffer));
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
    public void BloomFilter_ReadOnlySpanOfChar_IsInterchangeableWithString_ForAllAlgorithms()
    {
        var size = BloomFilterSize.CreateOptimalSize(1000, 0.01);
        foreach (var method in GetBloomFilterCreateMethods())
        {
            foreach (var key in GetCharKeys())
            {
                var filterFromString = (IBloomFilter)method.Invoke(null, [size])!;
                filterFromString.Add(key);
                Assert.True(filterFromString.MayContain(key.AsSpan()));

                var filterFromSpan = (IBloomFilter)method.Invoke(null, [size])!;
                filterFromSpan.Add(key.AsSpan());
                Assert.True(filterFromSpan.MayContain(key));
                Assert.True(filterFromSpan.MayContain(key.AsSpan()));

                Assert.Equal(filterFromString.GetEstimateCount(), filterFromSpan.GetEstimateCount());
            }
        }
    }

    [Fact]
    public void CountingBloomFilter_ReadOnlySpanOfChar_IsInterchangeableWithString_ForAllAlgorithms()
    {
        var size = CountingBloomFilterSize.CreateOptimalSize(1000, 0.01);
        foreach (var method in GetCountingBloomFilterCreateMethods())
        {
            foreach (var key in GetCharKeys())
            {
                var filter = (ICountingBloomFilter)method.Invoke(null, [size])!;

                filter.Add(key);
                filter.Add(key.AsSpan());
                Assert.True(filter.MayContain(key));
                Assert.True(filter.MayContain(key.AsSpan()));
                Assert.Equal(filter.GetEstimatedCount(key), filter.GetEstimatedCount(key.AsSpan()));
                Assert.True(filter.GetEstimatedCount(key.AsSpan()) >= 2);

                // Removing with the other representation clears the counters set by the first one
                filter.Remove(key.AsSpan());
                Assert.True(filter.GetEstimatedCount(key) >= 1);
                filter.Remove(key);
                Assert.False(filter.MayContain(key.AsSpan()));
                Assert.Equal(0, filter.GetEstimatedCount(key.AsSpan()));
                Assert.Equal(0, filter.GetEstimatedCount(key));

                filter.Add(key.AsSpan());
                filter.Remove(key);
                Assert.Equal(0, filter.GetEstimatedCount(key.AsSpan()));
            }
        }
    }

    [Fact]
    public void BloomFilter_ReadOnlySpanOfChar_Slice_MatchesSubstring()
    {
        const string Text = "user=alice;role=admin";
        var surrogatePair = new string([(char)0xD83D, (char)0xDE00]);
        var textWithSurrogatePair = "a" + surrogatePair + "b";

        foreach (var method in GetBloomFilterCreateMethods())
        {
            var filter = (IBloomFilter)method.Invoke(null, [BloomFilterSize.CreateOptimalSize(1000, 0.01)])!;
            filter.Add(Text.AsSpan(5, 5));
            Assert.True(filter.MayContain("alice"));
            Assert.False(filter.MayContain(Text));

            // A slice that splits a surrogate pair is the unpaired surrogate, not a replacement character
            filter.Add(textWithSurrogatePair.AsSpan(0, 2));
            Assert.True(filter.MayContain("a" + (char)0xD83D));
            Assert.False(filter.MayContain("a" + (char)0xFFFD));
        }

        foreach (var method in GetCountingBloomFilterCreateMethods())
        {
            var filter = (ICountingBloomFilter)method.Invoke(null, [CountingBloomFilterSize.CreateOptimalSize(1000, 0.01)])!;
            filter.Add(Text.AsSpan(16));
            Assert.Equal(1, filter.GetEstimatedCount("admin"));
            filter.Remove("admin");
            Assert.False(filter.MayContain(Text.AsSpan(16)));
        }
    }

    [Fact]
    public void BloomFilter_ReadOnlySpanOfChar_IsOrdinal()
    {
        var composed = new string([(char)0x00E9]);
        var decomposed = new string(['e', (char)0x0301]);

        foreach (var method in GetBloomFilterCreateMethods())
        {
            var filter = (IBloomFilter)method.Invoke(null, [BloomFilterSize.CreateOptimalSize(1000, 0.01)])!;
            filter.Add("Alice".AsSpan());
            filter.Add(composed.AsSpan());

            Assert.True(filter.MayContain("Alice".AsSpan()));
            Assert.False(filter.MayContain("alice".AsSpan()));
            Assert.True(filter.MayContain(composed.AsSpan()));
            Assert.False(filter.MayContain(decomposed.AsSpan()));
        }
    }

    [Fact]
    public void BloomFilter_ReadOnlySpanOfChar_DoesNotAllocate()
    {
        Span<char> buffer = stackalloc char[16];
        "key-0000".AsSpan().CopyTo(buffer);
        var key = buffer[..8];

        var filters = GetBloomFilterCreateMethods().Select(method => (IBloomFilter)method.Invoke(null, [BloomFilterSize.CreateOptimalSize(1000, 0.01)])!).ToArray();
        var countingFilters = GetCountingBloomFilterCreateMethods().Select(method => (ICountingBloomFilter)method.Invoke(null, [CountingBloomFilterSize.CreateOptimalSize(1000, 0.01)])!).ToArray();

        // Warm up so that JIT compilation is not measured
        Exercise(filters, countingFilters, key);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Exercise(filters, countingFilters, key);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(0L, allocated);

        static void Exercise(IBloomFilter[] filters, ICountingBloomFilter[] countingFilters, ReadOnlySpan<char> key)
        {
            foreach (var filter in filters)
            {
                filter.Add(key);
                _ = filter.MayContain(key);
            }

            foreach (var filter in countingFilters)
            {
                filter.Add(key);
                _ = filter.MayContain(key);
                _ = filter.GetEstimatedCount(key);
                filter.Remove(key);
            }
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
    [InlineData(nameof(CountingBloomFilter.CreateXXHash128))]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash64))]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash32))]
    [InlineData(nameof(CountingBloomFilter.CreateXXHash3))]
    [InlineData(nameof(CountingBloomFilter.CreateCrc64))]
    [InlineData(nameof(CountingBloomFilter.CreateCrc32))]
    [InlineData(nameof(CountingBloomFilter.CreateAdler32))]
    public void CountingBloomFilter_GetEstimatedCount_EmptyFilterReturnsZero(string createMethodName)
    {
        var size = CountingBloomFilterSize.CreateOptimalSize(1000, 0.01);
        var filter = (ICountingBloomFilter)typeof(CountingBloomFilter).GetMethod(createMethodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.Invoke(null, [size])!;
        for (var value = 0; value < 1000; value++)
        {
            Assert.Equal(0, filter.GetEstimatedCount(value));
            Assert.False(filter.MayContain(value));
        }
    }

    [Fact]
    public void CountingBloomFilter_GetEstimatedCount_RepeatedPositions()
    {
        // With a single counter, every hash function reads the same counter
        var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateExact(counterCount: 1, hashCount: 7));
        Assert.Equal(0, filter.GetEstimatedCount("value"));

        filter.Add("value");
        Assert.Equal(7, filter.GetEstimatedCount("value"));
        Assert.Equal(7, filter.GetEstimatedCount("other"));

        filter.Remove("value");
        Assert.Equal(0, filter.GetEstimatedCount("value"));
        Assert.False(filter.MayContain("other"));
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

    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(1000, 0.000001)]
    [InlineData(1000, 0.001)]
    [InlineData(1000, 0.01)]
    [InlineData(1000, 0.1)]
    [InlineData(1000, 0.3)]
    [InlineData(1000, 0.5)]
    [InlineData(1000, 0.6)]
    [InlineData(1000, 0.9)]
    [InlineData(1000, 0.99)]
    [InlineData(1_000_000_000, 0.01)]
    public void CreateOptimalSize_ModelFalsePositiveProbability_DoesNotExceedTarget(long expectedItemCount, double falsePositiveProbability)
    {
        var size = BloomFilterSize.CreateOptimalSize(expectedItemCount, falsePositiveProbability);
        var countingSize = CountingBloomFilterSize.CreateOptimalSize(expectedItemCount, falsePositiveProbability);

        Assert.Equal(size.BitCount, countingSize.CounterCount);
        Assert.Equal(size.HashCount, countingSize.HashCount);

        // The returned size meets the target, allowing for floating-point rounding only
        Assert.True(GetModelFalsePositiveProbability(size.BitCount, size.HashCount) <= falsePositiveProbability * (1 + 1e-12));

        // One position less would miss the target: the size is the smallest for its hash count
        if (size.BitCount > 1)
        {
            Assert.True(GetModelFalsePositiveProbability(size.BitCount - 1, size.HashCount) > falsePositiveProbability * (1 - 1e-12));
        }

        // No other hash count reaches the target with fewer positions
        for (var hashCount = 1; hashCount <= size.HashCount + 5; hashCount++)
        {
            if (hashCount != size.HashCount)
            {
                Assert.True(GetModelFalsePositiveProbability(size.BitCount - 1, hashCount) > falsePositiveProbability * (1 - 1e-12));
            }
        }

        double GetModelFalsePositiveProbability(long positionCount, int hashCount) => Math.Pow(1 - Math.Exp(-hashCount * (double)expectedItemCount / positionCount), hashCount);
    }

    [Theory]
    [InlineData(0.9, 435)]
    [InlineData(0.99, 218)]
    public void CreateOptimalSize_ProbabilityAboveHalf_GrowsStorageForSingleHash(double falsePositiveProbability, long expectedPositionCount)
    {
        var size = BloomFilterSize.CreateOptimalSize(1000, falsePositiveProbability);
        Assert.Equal(expectedPositionCount, size.BitCount);
        Assert.Equal(1, size.HashCount);

        var countingSize = CountingBloomFilterSize.CreateOptimalSize(1000, falsePositiveProbability);
        Assert.Equal(expectedPositionCount, countingSize.CounterCount);
        Assert.Equal(1, countingSize.HashCount);
    }

    [Fact]
    public void CreateOptimalSize_MeasuredFalsePositiveRate_StaysNearTarget()
    {
        const int ItemCount = 1000;
        const int ProbeCount = 100_000;

        foreach (var falsePositiveProbability in new[] { 0.01, 0.1, 0.9 })
        {
            var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(ItemCount, falsePositiveProbability));
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

            Assert.InRange((double)falsePositiveCount / ProbeCount, 0, falsePositiveProbability * 1.1);
        }
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

    private static IEnumerable<System.Reflection.MethodInfo> GetBloomFilterCreateMethods()
    {
        return typeof(BloomFilter).GetMethods().Where(m => m.Name.StartsWith("Create", StringComparison.Ordinal) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(BloomFilterSize));
    }

    private static IEnumerable<System.Reflection.MethodInfo> GetCountingBloomFilterCreateMethods()
    {
        return typeof(CountingBloomFilter).GetMethods().Where(m => m.Name.StartsWith("Create", StringComparison.Ordinal) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(CountingBloomFilterSize));
    }

    private static string[] GetCharKeys()
    {
        return
        [
            string.Empty,
            "a",
            "hello",
            new string('x', 257),
            "caf" + (char)0x00E9, // non-ASCII
            new string([(char)0x65E5, (char)0x672C, (char)0x8A9E]), // CJK
            new string([(char)0xD83D, (char)0xDE00]), // surrogate pair
            "a" + (char)0xD800 + "b", // unpaired high surrogate
            "a" + (char)0xDC00, // unpaired low surrogate
            new string([(char)0xDE00, (char)0xD83D]), // reversed surrogate pair
            new string([(char)0x0000, (char)0xFFFF]),
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
}
