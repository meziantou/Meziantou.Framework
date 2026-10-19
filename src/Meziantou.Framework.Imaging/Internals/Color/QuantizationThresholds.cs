using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Replaces a function that encodes a value and rounds the result to an 8-bit sample (a transfer function and its
/// rounding, which cost one or two <see cref="Math.Pow(double, double)"/> per sample) by a search in the thresholds of
/// the function: for each sample <c>k</c>, the smallest input whose sample is at least <c>k</c>. The sample of an input is
/// the number of thresholds up to it.
/// </summary>
/// <remarks>
/// <para>
/// The thresholds are found by bisection over the bit patterns of the non-negative doubles (ordered like their values),
/// with the function itself, so the search gives the samples of the function whenever the function is non-decreasing.
/// The functions are, but they are compositions of operations of the platform that are not correctly rounded
/// (<see cref="Math.Pow(double, double)"/> may be one unit in the last place off) and of segments that may not meet
/// exactly: <see cref="Create"/> compares the search with the function on 64 consecutive inputs around every threshold,
/// at the start of every coarse interval below, and on the special values (not a number, infinities, negative values),
/// and gives no table when any of them differs, so that the caller keeps the function.
/// </para>
/// <para>
/// The search starts from a coarse index: the sample at the start of each of 128 intervals per power of two, for inputs
/// from 2^-24 to 2^8 (the exponent and the 7 high bits of the significand of the input), then moves up one threshold at
/// a time: for the functions of the transfer characteristics, an interval holds at most one or two thresholds. Building a
/// table evaluates the function about 40,000 times (about a millisecond); 16-bit samples would need 65,535 thresholds,
/// about 4 million evaluations, which is more than the samples of most images, so they keep the function.
/// </para>
/// </remarks>
internal sealed class QuantizationThresholds
{
    /// <summary>The largest sample a table supports.</summary>
    public const int MaxSample = byte.MaxValue;

    // The coarse index is the bit pattern of the input shifted right by the significand bits it ignores, less the bit
    // pattern of 2^-24 shifted the same way: 32 powers of two of 128 intervals
    private const int IndexShift = 52 - 7;
    private const int IndexBase = (1023 - 24) << 7;
    private const int IndexCount = 32 << 7;

    // The inputs compared with the function on each side of every threshold
    private const int CheckedNeighbors = 32;

    // A table that cannot be used: the value of a cache entry whose function does not match its thresholds
    private static readonly QuantizationThresholds Unavailable = new([], []);

    // The threshold of sample k is at k; entry 0 is not used, and entry max + 1 is not a number, which no input reaches.
    // A sample that no input reaches has a threshold that is not a number too.
    private readonly double[] _thresholds;

    // The sample at the start of each interval of the coarse index (at 0 for the first one)
    private readonly byte[] _starts;

    private QuantizationThresholds(double[] thresholds, byte[] starts)
    {
        _thresholds = thresholds;
        _starts = starts;
    }

    /// <summary>
    /// Gets the table of a function from a cache, creating it the first time: threads that need it at the same time may
    /// each create it, and the first one that is published is the table of every caller.
    /// </summary>
    /// <param name="tables">The cache.</param>
    /// <param name="index">The entry of the function in the cache.</param>
    /// <param name="quantize">The function: its value for every input, from 0 to <paramref name="max"/>.</param>
    /// <param name="max">The largest sample, at most <see cref="MaxSample"/>.</param>
    /// <returns>The table, or <see langword="null"/> when it does not give the samples of the function.</returns>
    public static QuantizationThresholds? GetOrCreate(QuantizationThresholds?[] tables, int index, Func<double, int> quantize, int max)
    {
        var table = Volatile.Read(ref tables[index]);
        if (table is null)
        {
            var created = Create(quantize, max) ?? Unavailable;
            table = Interlocked.CompareExchange(ref tables[index], created, null) ?? created;
        }

        return ReferenceEquals(table, Unavailable) ? null : table;
    }

    /// <summary>Creates the table of a function.</summary>
    /// <param name="quantize">The function: its value for every input, from 0 to <paramref name="max"/>.</param>
    /// <param name="max">The largest sample, at most <see cref="MaxSample"/>.</param>
    /// <returns>The table, or <see langword="null"/> when it does not give the samples of the function.</returns>
    internal static QuantizationThresholds? Create(Func<double, int> quantize, int max)
    {
        Debug.Assert(max is > 0 and <= MaxSample);

        // Every value up to 0 (and not a number) must be the sample of 0, which is what the search gives them
        var zero = quantize(0);
        if (zero is < 0 || zero > max || quantize(-0.0) != zero || quantize(double.NaN) != zero || quantize(-1) != zero || quantize(double.NegativeInfinity) != zero)
            return null;

        var infinity = quantize(double.PositiveInfinity);
        if (infinity < zero || infinity > max)
            return null;

        var thresholds = new double[max + 2];
        thresholds[0] = double.NaN;
        thresholds[max + 1] = double.NaN;
        var lowest = 0L;
        for (var k = 1; k <= max; k++)
        {
            if (k <= zero)
            {
                thresholds[k] = 0;
                continue;
            }

            if (k > infinity)
            {
                thresholds[k] = double.NaN;
                continue;
            }

            // The sample of the bit pattern 'low' is less than k, the one of 'high' is at least k; for a non-decreasing
            // function, the threshold of k is above the one of k - 1
            var low = lowest;
            var high = BitConverter.DoubleToInt64Bits(double.PositiveInfinity);
            if (quantize(BitConverter.Int64BitsToDouble(low)) >= k)
                return null;

            while (high - low > 1)
            {
                var middle = low + ((high - low) / 2);
                if (quantize(BitConverter.Int64BitsToDouble(middle)) >= k)
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }

            thresholds[k] = BitConverter.Int64BitsToDouble(high);
            lowest = low;
        }

        var starts = new byte[IndexCount];
        for (var i = 0; i < starts.Length; i++)
        {
            starts[i] = (byte)Count(thresholds, i == 0 ? 0 : GetIntervalStart(i), max);
        }

        var table = new QuantizationThresholds(thresholds, starts);
        return table.Matches(quantize, max) ? table : null;
    }

    /// <summary>Gets the sample of an input: the sample of the function the table was created from.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Quantize(double value)
    {
        // Values up to 0 and values that are not a number have the sample of 0
        if (!(value > 0))
        {
            value = 0;
        }

        var index = (int)(BitConverter.DoubleToInt64Bits(value) >> IndexShift) - IndexBase;
        index = index < 0 ? 0 : index >= IndexCount ? IndexCount - 1 : index;
        int sample = _starts[index];
        var thresholds = _thresholds;
        while (value >= thresholds[sample + 1])
        {
            sample++;
        }

        return sample;
    }

    /// <summary>Gets the smallest input whose sample is at least <paramref name="sample"/> (from 1 to the largest sample), or a value that is not a number when no input reaches it.</summary>
    internal double GetThreshold(int sample) => _thresholds[sample];

    private static double GetIntervalStart(int index) => BitConverter.Int64BitsToDouble((long)(index + IndexBase) << IndexShift);

    // The number of thresholds up to a value, without the coarse index
    private static int Count(double[] thresholds, double value, int max)
    {
        var count = 0;
        while (count < max && value >= thresholds[count + 1])
        {
            count++;
        }

        return count;
    }

    // Compares the table with the function around every threshold, at the start of every interval and on special values
    private bool Matches(Func<double, int> quantize, int max)
    {
        foreach (var value in (ReadOnlySpan<double>)[0, -0.0, double.NaN, -1, double.NegativeInfinity, double.PositiveInfinity, double.MaxValue, double.Epsilon, 1])
        {
            if (Quantize(value) != quantize(value))
                return false;
        }

        for (var k = 1; k <= max; k++)
        {
            var threshold = _thresholds[k];
            if (threshold < _thresholds[k - 1])
                return false;

            if (double.IsNaN(threshold) || (k > 1 && threshold == _thresholds[k - 1]))
                continue;

            var bits = BitConverter.DoubleToInt64Bits(threshold);
            var first = Math.Max(bits - CheckedNeighbors, 0);
            var last = Math.Min(bits + CheckedNeighbors, BitConverter.DoubleToInt64Bits(double.PositiveInfinity));
            for (var neighbor = first; neighbor < last; neighbor++)
            {
                var value = BitConverter.Int64BitsToDouble(neighbor);
                if (Quantize(value) != quantize(value))
                    return false;
            }
        }

        for (var i = 1; i < IndexCount; i++)
        {
            var start = GetIntervalStart(i);
            if (Quantize(start) != quantize(start) || Quantize(Math.BitDecrement(start)) != quantize(Math.BitDecrement(start)))
                return false;
        }

        return true;
    }
}
