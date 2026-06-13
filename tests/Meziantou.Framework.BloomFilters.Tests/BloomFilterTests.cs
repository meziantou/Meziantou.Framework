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

        Parallel.For(0, 10_000, _ => filter.Add("value"));
        Assert.Equal(10_000, filter.GetEstimatedCount("value"));

        Parallel.For(0, 10_000, _ => filter.Remove("value"));
        Assert.Equal(0, filter.GetEstimatedCount("value"));
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
