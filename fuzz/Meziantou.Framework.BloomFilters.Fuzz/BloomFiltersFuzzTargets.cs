using System.Buffers.Binary;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.BloomFilters.Fuzz;

/// <summary>
/// The batched methods of the XXHash filters (<c>AddRange</c>, <c>RemoveRange</c>) hash several values at once, with
/// vector or scalar code depending on the hardware. They are compared with the methods that hash one value at a time
/// (<c>Add</c>, <c>Remove</c>), which call <see cref="System.IO.Hashing"/>: both must set the same bits and counters.
/// </summary>
/// <remarks>
/// The input starts with two option bytes: the first selects the filter (classic or counting, XXHash32, XXHash64 or
/// XXHash128) and whether the values are signed, the second where the batch is split in two calls. The rest of the input
/// is read as little-endian values (4 bytes for XXHash32, 8 bytes for XXHash64 and XXHash128).
/// </remarks>
public static class BloomFiltersFuzzTargets
{
    private const int FilterCount = 6;
    private const byte SignedFlag = 0x80;

    private static readonly BloomFilterSize Size = BloomFilterSize.CreateOptimalSize(1000, 0.01);
    private static readonly CountingBloomFilterSize CountingSize = CountingBloomFilterSize.CreateOptimalSize(1000, 0.01);

    // The extreme values of the lanes, and the patterns of the multiplications
    private static readonly string[] Tokens = ["\0\0\0\0", "\0\0\0\0\0\0\0\0", "\u007F\u007F\u007F\u007F", "\u007F\u007F\u007F\u007F\u007F\u007F\u007F\u007F", "\u0001\0\0\0", "\u0001\0\0\0\0\0\0\0"];

    private delegate void RangeAction<in TFilter, T>(TFilter filter, ReadOnlySpan<T> values);

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("bloomfilters-addrange", AddRange) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    private static void AddRange(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var split = FuzzInput.ReadByte(ref data);
        var signed = (options & SignedFlag) != 0;
        switch ((options & ~SignedFlag) % FilterCount)
        {
            case 0 when signed:
                Check(() => BloomFilter.CreateXXHash32(Size), ReadInt32(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.MayContain(v));
                break;
            case 0:
                Check(() => BloomFilter.CreateXXHash32(Size), ReadUInt32(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.MayContain(v));
                break;
            case 1 when signed:
                Check(() => BloomFilter.CreateXXHash64(Size), ReadInt64(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.MayContain(v));
                break;
            case 1:
                Check(() => BloomFilter.CreateXXHash64(Size), ReadUInt64(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.MayContain(v));
                break;
            case 2 when signed:
                Check(() => BloomFilter.CreateXXHash128(Size), ReadInt64(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.MayContain(v));
                break;
            case 2:
                Check(() => BloomFilter.CreateXXHash128(Size), ReadUInt64(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.MayContain(v));
                break;
            case 3 when signed:
                CheckCounting(() => CountingBloomFilter.CreateXXHash32(CountingSize), ReadInt32(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.Remove(v), (f, v) => f.RemoveRange(v), (f, v) => f.MayContain(v));
                break;
            case 3:
                CheckCounting(() => CountingBloomFilter.CreateXXHash32(CountingSize), ReadUInt32(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.Remove(v), (f, v) => f.RemoveRange(v), (f, v) => f.MayContain(v));
                break;
            case 4 when signed:
                CheckCounting(() => CountingBloomFilter.CreateXXHash64(CountingSize), ReadInt64(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.Remove(v), (f, v) => f.RemoveRange(v), (f, v) => f.MayContain(v));
                break;
            case 4:
                CheckCounting(() => CountingBloomFilter.CreateXXHash64(CountingSize), ReadUInt64(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.Remove(v), (f, v) => f.RemoveRange(v), (f, v) => f.MayContain(v));
                break;
            case 5 when signed:
                CheckCounting(() => CountingBloomFilter.CreateXXHash128(CountingSize), ReadInt64(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.Remove(v), (f, v) => f.RemoveRange(v), (f, v) => f.MayContain(v));
                break;
            default:
                CheckCounting(() => CountingBloomFilter.CreateXXHash128(CountingSize), ReadUInt64(data), split, (f, v) => f.Add(v), (f, v) => f.AddRange(v), (f, v) => f.Remove(v), (f, v) => f.RemoveRange(v), (f, v) => f.MayContain(v));
                break;
        }
    }

    /// <summary>
    /// AddRange over the whole batch and over two parts set the same bits as Add, and every value is then reported as
    /// possibly present.
    /// </summary>
    private static void Check<TFilter, T>(Func<TFilter> create, T[] values, byte split, Action<TFilter, T> add, RangeAction<TFilter, T> addRange, Func<TFilter, T, bool> mayContain)
        where TFilter : BloomFilter
    {
        var expected = create();
        foreach (var value in values)
        {
            add(expected, value);
        }

        var expectedBytes = Save(expected.Save);
        var name = typeof(TFilter).Name;

        var actual = create();
        addRange(actual, values);
        FuzzAssert.True(expectedBytes.AsSpan().SequenceEqual(Save(actual.Save)), $"{name}: AddRange of {values.Length} values differs from Add");

        var index = GetSplitIndex(values.Length, split);
        var splitActual = create();
        addRange(splitActual, values.AsSpan(0, index));
        addRange(splitActual, values.AsSpan(index));
        FuzzAssert.True(expectedBytes.AsSpan().SequenceEqual(Save(splitActual.Save)), $"{name}: AddRange of {values.Length} values split at {index} differs from Add");

        foreach (var value in values)
        {
            FuzzAssert.True(mayContain(actual, value), $"{name}: MayContain({value}) is false after AddRange");
        }
    }

    /// <summary>
    /// AddRange sets the same counters as Add, then RemoveRange over two parts sets the same counters as Remove.
    /// </summary>
    private static void CheckCounting<TFilter, T>(Func<TFilter> create, T[] values, byte split, Action<TFilter, T> add, RangeAction<TFilter, T> addRange, Action<TFilter, T> remove, RangeAction<TFilter, T> removeRange, Func<TFilter, T, bool> mayContain)
        where TFilter : CountingBloomFilter
    {
        var expected = create();
        foreach (var value in values)
        {
            add(expected, value);
        }

        var name = typeof(TFilter).Name;
        var actual = create();
        addRange(actual, values);
        FuzzAssert.True(Save(expected.Save).AsSpan().SequenceEqual(Save(actual.Save)), $"{name}: AddRange of {values.Length} values differs from Add");
        foreach (var value in values)
        {
            FuzzAssert.True(mayContain(actual, value), $"{name}: MayContain({value}) is false after AddRange");
        }

        // Remove the values after the split index, so that some of the counters are still set
        var index = GetSplitIndex(values.Length, split);
        foreach (var value in values.AsSpan(index))
        {
            remove(expected, value);
        }

        removeRange(actual, values.AsSpan(index));
        FuzzAssert.True(Save(expected.Save).AsSpan().SequenceEqual(Save(actual.Save)), $"{name}: RemoveRange of {values.Length - index} values differs from Remove");
    }

    private static int GetSplitIndex(int length, byte split) => (int)((long)length * split / byte.MaxValue);

    private static byte[] Save(Action<Stream> save)
    {
        using var stream = new MemoryStream();
        save(stream);
        return stream.ToArray();
    }

    private static int[] ReadInt32(ReadOnlySpan<byte> data)
    {
        var values = new int[data.Length / sizeof(int)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadInt32LittleEndian(data[(i * sizeof(int))..]);
        }

        return values;
    }

    private static uint[] ReadUInt32(ReadOnlySpan<byte> data)
    {
        var values = new uint[data.Length / sizeof(uint)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt32LittleEndian(data[(i * sizeof(uint))..]);
        }

        return values;
    }

    private static long[] ReadInt64(ReadOnlySpan<byte> data)
    {
        var values = new long[data.Length / sizeof(long)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadInt64LittleEndian(data[(i * sizeof(long))..]);
        }

        return values;
    }

    private static ulong[] ReadUInt64(ReadOnlySpan<byte> data)
    {
        var values = new ulong[data.Length / sizeof(ulong)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt64LittleEndian(data[(i * sizeof(ulong))..]);
        }

        return values;
    }

    /// <summary>
    /// The values of the unit tests (BloomFilterTests: -50 to 49 with the minimum and maximum, 0 to 99 with the
    /// maximum), for every filter, and a batch shorter than a vector.
    /// </summary>
    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        for (var filter = 0; filter < FilterCount; filter++)
        {
            var is32 = filter % 3 == 0;
            var signedValues = Enumerable.Range(-50, 100).Select(value => (long)value).Append(is32 ? int.MinValue : long.MinValue).Append(is32 ? int.MaxValue : long.MaxValue).ToArray();
            var unsignedValues = Enumerable.Range(0, 100).Select(value => (ulong)value).Append(is32 ? uint.MaxValue : ulong.MaxValue).ToArray();

            yield return new FuzzSeed($"filter{filter}-signed", Encode((byte)(filter | SignedFlag), 128, signedValues.Select(value => (ulong)value), is32));
            yield return new FuzzSeed($"filter{filter}-unsigned", Encode((byte)filter, 37, unsignedValues, is32));
            yield return new FuzzSeed($"filter{filter}-short", Encode((byte)filter, 255, [1, 2, 3], is32));
        }
    }

    private static byte[] Encode(byte options, byte split, IEnumerable<ulong> values, bool is32)
    {
        var result = new List<byte> { options, split };
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
            result.AddRange(buffer[..(is32 ? sizeof(uint) : sizeof(ulong))]);
        }

        return [.. result];
    }
}
