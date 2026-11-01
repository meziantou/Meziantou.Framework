using System.Collections.Concurrent;


namespace Meziantou.Framework.BloomFilters.Benchmarks;

/// <summary>
/// A filter sized for a memory budget and filled to its design capacity (about half of the bits set) with random keys.
/// </summary>
internal sealed class LayoutScenario
{
    public const double TargetFalsePositiveRate = 0.01;

    private LayoutScenario(FilterMemory memory, FilterLayout layout, long itemCount, BloomFilterXXHash128? classic, BlockedBloomFilterXXHash128? blocked)
    {
        Memory = memory;
        Layout = layout;
        ItemCount = itemCount;
        Classic = classic;
        Blocked = blocked;
    }

    public FilterMemory Memory { get; }
    public FilterLayout Layout { get; }
    public long ItemCount { get; }
    public BloomFilterXXHash128? Classic { get; }
    public BlockedBloomFilterXXHash128? Blocked { get; }
    public BlockedBloomFilterSize? BlockedSize { get; private init; }

    public long BitCount => BlockedSize?.BitCount ?? GetClassicSize(ItemCount).BitCount;
    public int HashCount => BlockedSize?.HashCount ?? GetClassicSize(ItemCount).HashCount;

    public double ExpectedFalsePositiveRate => BlockedSize?.GetFalsePositiveProbability(ItemCount)
        ?? Math.Pow(1 - Math.Exp(-(double)HashCount * ItemCount / BitCount), HashCount);

    // Keys are a bijection of their index, so present (index < ItemCount) and absent (index >= ItemCount) keys never
    // collide, and no key array is needed for filters holding ~100 million items.
    public static ulong Key(long index)
    {
        var result = (ulong)index + 0x9E3779B97F4A7C15UL;
        result = (result ^ (result >> 30)) * 0xBF58476D1CE4E5B9UL;
        result = (result ^ (result >> 27)) * 0x94D049BB133111EBUL;
        return result ^ (result >> 31);
    }

    public static long GetItemCount(FilterMemory memory)
    {
        var bitCount = memory switch
        {
            FilterMemory.Small512KiB => 512L * 1024 * 8,
            FilterMemory.Large128MiB => 128L * 1024 * 1024 * 8,
            _ => throw new ArgumentOutOfRangeException(nameof(memory)),
        };

        return (long)(bitCount * Math.Log(2) * Math.Log(2) / -Math.Log(TargetFalsePositiveRate));
    }

    public static LayoutScenario CreateEmpty(FilterMemory memory, FilterLayout layout)
    {
        var itemCount = GetItemCount(memory);
        var classicSize = GetClassicSize(itemCount);
        if (layout is FilterLayout.Classic)
            return new LayoutScenario(memory, layout, itemCount, BloomFilter.CreateXXHash128(classicSize), blocked: null);

        // The sizing is cached so that the Construct benchmark measures the allocation only, like Classic.
        var size = BlockedSizes.GetOrAdd((memory, layout), key => key.Layout is FilterLayout.BlockedEqualFpr
            ? BlockedBloomFilterSize.CreateOptimalSize(itemCount, TargetFalsePositiveRate)
            : GetBestSizeForBlockCount(itemCount, (classicSize.BitCount + 511) / 512));
        return new LayoutScenario(memory, layout, itemCount, classic: null, BlockedBloomFilter.CreateXXHash128(size)) { BlockedSize = size };
    }

    private static readonly ConcurrentDictionary<(FilterMemory Memory, FilterLayout Layout), BlockedBloomFilterSize> BlockedSizes = new();

    private static BlockedBloomFilterSize GetBestSizeForBlockCount(long itemCount, long blockCount)
    {
        var best = BlockedBloomFilterSize.CreateExact(blockCount, 1);
        for (var hashCount = 2; hashCount <= 32; hashCount++)
        {
            var size = BlockedBloomFilterSize.CreateExact(blockCount, hashCount);
            if (size.GetFalsePositiveProbability(itemCount) < best.GetFalsePositiveProbability(itemCount))
            {
                best = size;
            }
        }

        return best;
    }

    public static LayoutScenario CreateFilled(FilterMemory memory, FilterLayout layout)
    {
        var scenario = CreateEmpty(memory, layout);
        const int ChunkSize = 1 << 16;
        Parallel.For(0, (scenario.ItemCount + ChunkSize - 1) / ChunkSize, chunk =>
        {
            var end = Math.Min(scenario.ItemCount, (chunk + 1) * ChunkSize);
            for (var index = chunk * ChunkSize; index < end; index++)
            {
                scenario.Add(Key(index));
            }
        });

        return scenario;
    }

    public void Add(ulong value)
    {
        if (Classic is not null)
        {
            Classic.Add(value);
        }
        else
        {
            Blocked!.Add(value);
        }
    }

    public bool MayContain(ulong value) => Classic?.MayContain(value) ?? Blocked!.MayContain(value);

    private static BloomFilterSize GetClassicSize(long itemCount) => BloomFilterSize.CreateOptimalSize(itemCount, TargetFalsePositiveRate);
}
