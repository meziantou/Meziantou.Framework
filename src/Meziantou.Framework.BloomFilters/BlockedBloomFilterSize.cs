using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

/// <summary>
/// The size of a <see cref="BlockedBloomFilter"/>: a number of 512-bit blocks and a number of bits set per value.
/// </summary>
/// <remarks>
/// The values do not spread evenly over the blocks: the number of values per block follows a Poisson distribution, and
/// the crowded blocks answer "maybe" more often than the average. <see cref="CreateOptimalSize"/> accounts for this, so
/// it returns more bits than <see cref="BloomFilterSize.CreateOptimalSize"/> for the same false positive probability.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct BlockedBloomFilterSize
{
    internal const int BitsPerBlock = 512;

    // The filter stores a block index in a long, and BitCount must not overflow.
    private const long MaxBlockCount = long.MaxValue / BitsPerBlock;

    // A position inside a block is drawn from 9 bits; more bits per value than the block holds is pointless.
    private const int MaxHashCount = BitsPerBlock;

    private BlockedBloomFilterSize(long blockCount, int hashCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(blockCount, MaxBlockCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hashCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hashCount, MaxHashCount);

        BlockCount = blockCount;
        HashCount = hashCount;
    }

    /// <summary>Gets the number of 512-bit (64-byte) blocks.</summary>
    public long BlockCount { get; }

    /// <summary>Gets the number of bits set in the block of a value when it is added.</summary>
    public int HashCount { get; }

    /// <summary>Gets the total number of bits: <see cref="BlockCount"/> × 512.</summary>
    public long BitCount => BlockCount * BitsPerBlock;

    /// <summary>
    /// Computes the smallest size whose false positive probability, once <paramref name="expectedItemCount"/> values
    /// are added, is at most <paramref name="falsePositiveProbability"/>.
    /// </summary>
    public static BlockedBloomFilterSize CreateOptimalSize(long expectedItemCount, double falsePositiveProbability)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedItemCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(falsePositiveProbability);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(falsePositiveProbability, 1);

        // The false positive probability decreases with the number of blocks: find an upper bound, then the smallest
        // block count that reaches the target.
        long low = 1;
        var high = Math.Max(1, (long)Math.Ceiling(BloomFilterSize.CreateOptimalSize(expectedItemCount, falsePositiveProbability).BitCount / (double)BitsPerBlock));
        while (true)
        {
            GetOptimalHashCount(expectedItemCount, high, out var highRate);
            if (highRate <= falsePositiveProbability)
                break;

            low = high + 1;
            if (high > MaxBlockCount / 2)
                throw new ArgumentOutOfRangeException(nameof(expectedItemCount), expectedItemCount, "The optimal size for these parameters exceeds the maximum supported size. Lower expectedItemCount or raise falsePositiveProbability.");

            high *= 2;
        }

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            GetOptimalHashCount(expectedItemCount, middle, out var rate);
            if (rate <= falsePositiveProbability)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return new BlockedBloomFilterSize(high, GetOptimalHashCount(expectedItemCount, high, out _));
    }

    public static BlockedBloomFilterSize CreateExact(long blockCount, int hashCount) => new(blockCount, hashCount);

    /// <summary>
    /// Computes the expected false positive probability of a filter of this size once <paramref name="itemCount"/>
    /// distinct values are added.
    /// </summary>
    public double GetFalsePositiveProbability(long itemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        return GetFalsePositiveProbability((double)itemCount / BlockCount, HashCount);
    }

    // The false positive probability is unimodal in the hash count: walk up while it decreases.
    private static int GetOptimalHashCount(long itemCount, long blockCount, out double rate)
    {
        var itemsPerBlock = (double)itemCount / blockCount;
        var hashCount = 1;
        rate = GetFalsePositiveProbability(itemsPerBlock, hashCount);
        while (hashCount < MaxHashCount)
        {
            var next = GetFalsePositiveProbability(itemsPerBlock, hashCount + 1);
            if (next >= rate)
                break;

            rate = next;
            hashCount++;
        }

        return hashCount;
    }

    /// <summary>
    /// Sums, over the number of values j that land in a block (Poisson with mean <paramref name="itemsPerBlock"/>),
    /// the probability that the <paramref name="hashCount"/> bits of an absent value are among the bits set by j values.
    /// </summary>
    internal static double GetFalsePositiveProbability(double itemsPerBlock, int hashCount)
    {
        if (itemsPerBlock == 0)
            return 0;

        // Every bit of the block is almost surely set.
        if (itemsPerBlock * hashCount > BitsPerBlock * 40d)
            return 1;

        var logBitClear = Math.Log(1 - (1d / BitsPerBlock));
        var mode = Math.Floor(itemsPerBlock);
        var modeProbability = Math.Exp((mode * Math.Log(itemsPerBlock)) - itemsPerBlock - LogFactorial(mode));

        var result = 0d;
        var probability = modeProbability;
        for (var items = mode; probability > 1e-20 || items == mode; items++)
        {
            result += probability * BlockFalsePositiveProbability(items);
            probability *= itemsPerBlock / (items + 1);
        }

        probability = modeProbability;
        for (var items = mode - 1; items >= 0; items--)
        {
            probability *= (items + 1) / itemsPerBlock;
            if (probability < 1e-20)
                break;

            result += probability * BlockFalsePositiveProbability(items);
        }

        return Math.Min(1, result);

        double BlockFalsePositiveProbability(double items) => Math.Pow(1 - Math.Exp(items * hashCount * logBitClear), hashCount);
    }

    private static double LogFactorial(double value)
    {
        if (value < 20)
        {
            var result = 0d;
            for (var i = 2; i <= value; i++)
            {
                result += Math.Log(i);
            }

            return result;
        }

        // Stirling's series, accurate to ~1e-12 from 20 on.
        return (value * Math.Log(value)) - value + (0.5 * Math.Log(2 * Math.PI * value)) + (1 / (12 * value)) - (1 / (360 * value * value * value));
    }
}
