using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The weights of one resize axis in fixed point for <see cref="FixedPointResampler"/>: signed 16-bit integers with
/// <see cref="FractionBits"/> fractional bits, each window adding up to exactly <see cref="One"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every weight is the double weight of <see cref="ResampleWeights"/> times 2^14, rounded to nearest (ties upward). The
/// residue between 2^14 and their sum is then given one unit at a time to the weight whose rounding lost the most (the
/// largest <c>scaled - rounded</c> for a positive residue, the smallest for a negative one; ties to the lowest index), so
/// that a constant row stays exactly that constant: an opaque alpha stays at its maximum.
/// </para>
/// <para>
/// The windows of a horizontal table are padded with zero weights to the same number of taps (<see cref="Taps"/>, a
/// multiple of the requested one), like <see cref="ResampleWeights.CreatePadded"/>: a padded window starts with its window,
/// or earlier when it would otherwise end after the last source index, and stays within
/// [<see cref="First"/>, <see cref="First"/> + <see cref="SourceLength"/> - 1].
/// </para>
/// </remarks>
internal sealed class FixedPointWeights : IDisposable
{
    /// <summary>The number of fractional bits of a weight.</summary>
    public const int FractionBits = 14;

    /// <summary>The sum of the weights of every window: 1 in fixed point.</summary>
    public const int One = 1 << FractionBits;

    private readonly PooledBuffer _starts;
    private readonly PooledBuffer _weights;

    private FixedPointWeights(int outputLength, int first, int sourceLength, int taps, PooledBuffer starts, PooledBuffer weights)
    {
        OutputLength = outputLength;
        First = first;
        SourceLength = sourceLength;
        Taps = taps;
        _starts = starts;
        _weights = weights;
    }

    /// <summary>Gets the number of output pixels.</summary>
    public int OutputLength { get; }

    /// <summary>Gets the first source index used by any output pixel.</summary>
    public int First { get; }

    /// <summary>Gets the number of source indexes that the windows read from <see cref="First"/>.</summary>
    public int SourceLength { get; }

    /// <summary>Gets the number of taps of every padded window, or 0 when the windows have their own lengths (vertical tables).</summary>
    public int Taps { get; }

    /// <summary>Gets the largest sum of the positive weights of a window.</summary>
    public long MaxPositiveSum { get; private set; }

    /// <summary>Gets the smallest sum of the negative weights of a window.</summary>
    public long MinNegativeSum { get; private set; }

    /// <summary>Gets the largest sum of the absolute values of the weights of a window.</summary>
    public long MaxAbsoluteSum { get; private set; }

    /// <summary>Gets the starts of the windows: from <see cref="First"/> for a padded table, absolute otherwise.</summary>
    public ReadOnlySpan<int> Starts => unsafe(MemoryMarshal.Cast<byte, int>(_starts.Span))[..OutputLength];

    /// <summary>Gets every weight: <see cref="Taps"/> per output pixel for a padded table, the windows one after the other otherwise.</summary>
    public ReadOnlySpan<short> AllWeights => unsafe(MemoryMarshal.Cast<byte, short>(_weights.Span));

    /// <summary>Gets the weights of the padded window of output pixel <paramref name="index"/> (padded tables).</summary>
    public ReadOnlySpan<short> GetPaddedWeights(int index) => AllWeights.Slice(index * Taps, Taps);

    /// <summary>
    /// Quantizes the weights of an axis. <paramref name="tapMultiple"/> 0 keeps the windows as they are (the vertical pass);
    /// otherwise the windows are padded to the same number of taps, a multiple of it.
    /// </summary>
    /// <returns>The table, or <see langword="null"/> when a weight does not fit 16 bits.</returns>
    /// <exception cref="ImageResourceLimitException">The tables exceed the allocation limit; nothing stays charged.</exception>
    public static FixedPointWeights? Create(AllocationScope scope, ResampleWeights weights, int tapMultiple)
    {
        var outputLength = weights.OutputLength;
        var first = weights.First;
        var last = weights.Last;
        var taps = 0;
        long total = 0;
        if (tapMultiple is 0)
        {
            for (var d = 0; d < outputLength; d++)
            {
                total += weights.GetCount(d);
            }
        }
        else
        {
            var maxCount = 0;
            for (var d = 0; d < outputLength; d++)
            {
                maxCount = Math.Max(maxCount, weights.GetCount(d));
            }

            taps = (maxCount + tapMultiple - 1) / tapMultiple * tapMultiple;
            total = (long)outputLength * taps;
        }

        var starts = ResampleWeights.Rent<int>(scope, outputLength);
        PooledBuffer? values = null;
        try
        {
            values = ResampleWeights.Rent<short>(scope, total);
            var result = new FixedPointWeights(outputLength, first, Math.Max(last - first + 1, taps), taps, starts, values);
            if (!result.Fill(weights, last))
            {
                result.Dispose();
                return null;
            }

            return result;
        }
        catch
        {
            values?.Dispose();
            starts.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Quantizes one window of weights into <paramref name="destination"/> (see the remarks of the class).
    /// </summary>
    /// <returns><see langword="false"/> when a weight does not fit 16 bits.</returns>
    internal static bool Quantize(ReadOnlySpan<double> weights, Span<short> destination)
    {
        var sum = 0L;
        for (var i = 0; i < weights.Length; i++)
        {
            var rounded = Math.Floor((weights[i] * One) + 0.5);
            if (rounded is > short.MaxValue or < short.MinValue + 1)
                return false;

            destination[i] = (short)rounded;
            sum += (long)rounded;
        }

        for (var residue = One - sum; residue != 0; residue -= Math.Sign(residue))
        {
            // The weight whose rounding lost the most in the direction of the residue (ties: the lowest index)
            var best = -1;
            var bestRemainder = 0d;
            for (var i = 0; i < weights.Length; i++)
            {
                var remainder = (weights[i] * One) - destination[i];
                if (best < 0 || (residue > 0 ? remainder > bestRemainder : remainder < bestRemainder))
                {
                    best = i;
                    bestRemainder = remainder;
                }
            }

            var adjusted = destination[best] + Math.Sign(residue);
            if (adjusted is > short.MaxValue or < short.MinValue + 1)
                return false;

            destination[best] = (short)adjusted;
        }

        return true;
    }

    public void Dispose()
    {
        _weights.Dispose();
        _starts.Dispose();
    }

    private bool Fill(ResampleWeights weights, int last)
    {
        var starts = unsafe(MemoryMarshal.Cast<byte, int>(_starts.Span));
        var all = unsafe(MemoryMarshal.Cast<byte, short>(_weights.Span));
        var offset = 0;
        long maxPositive = 0, minNegative = 0, maxAbsolute = 0;
        for (var d = 0; d < OutputLength; d++)
        {
            var start = weights.GetStart(d);
            var window = weights.GetWeights(d);
            Span<short> target;
            if (Taps is 0)
            {
                starts[d] = start;
                target = all.Slice(offset, window.Length);
                offset += window.Length;
            }
            else
            {
                var paddedStart = Math.Max(First, Math.Min(start, last + 1 - Taps));
                starts[d] = paddedStart - First;
                target = all.Slice((d * Taps) + start - paddedStart, window.Length);
            }

            if (!Quantize(window, target))
                return false;

            long positive = 0, negative = 0;
            foreach (var weight in target)
            {
                if (weight > 0)
                {
                    positive += weight;
                }
                else
                {
                    negative += weight;
                }
            }

            maxPositive = Math.Max(maxPositive, positive);
            minNegative = Math.Min(minNegative, negative);
            maxAbsolute = Math.Max(maxAbsolute, positive - negative);
        }

        MaxPositiveSum = maxPositive;
        MinNegativeSum = minNegative;
        MaxAbsoluteSum = maxAbsolute;
        return true;
    }
}
