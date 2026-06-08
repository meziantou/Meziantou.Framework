using Meziantou.Framework.BloomFilters;

namespace Meziantou.Framework.Tests;

public sealed class BloomFilterTests
{
    private static readonly BloomFilterSize FilterSize = BloomFilterSize.CreateExact(bitCount: 257, hashCount: 7);

    public static TheoryData<BloomFilterAlgorithm, object> AllAlgorithmsAndValues()
    {
        var data = new TheoryData<BloomFilterAlgorithm, object>();
        foreach (var algorithm in Enum.GetValues<BloomFilterAlgorithm>())
        {
            foreach (var value in GetValues())
            {
                data.Add(algorithm, value);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllAlgorithmsAndValues))]
    public void Add_ThenMayContain_ReturnsTrue_ForAllAlgorithmsAndTypes(BloomFilterAlgorithm algorithm, object value)
    {
        var filter = CreateFilter(algorithm);
        AddValue(filter, value);
        Assert.True(MayContainValue(filter, value));
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

    private static IBloomFilter CreateFilter(BloomFilterAlgorithm algorithm)
    {
        return algorithm switch
        {
            BloomFilterAlgorithm.XXHash128 => BloomFilter.CreateXXHash128(FilterSize),
            BloomFilterAlgorithm.XXHash64 => BloomFilter.CreateXXHash64(FilterSize),
            BloomFilterAlgorithm.XXHash32 => BloomFilter.CreateXXHash32(FilterSize),
            BloomFilterAlgorithm.XXHash3 => BloomFilter.CreateXXHash3(FilterSize),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };
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

    public enum BloomFilterAlgorithm
    {
        XXHash128,
        XXHash64,
        XXHash32,
        XXHash3,
    }
}
