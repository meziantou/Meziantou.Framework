namespace Meziantou.Framework.Imaging.TestHarness.Color;

/// <summary>
/// An independent reference of ICC color conversion, written from ICC.1:2001-04 and ICC.1:2022 with its own profile
/// reader and <see cref="decimal"/> arithmetic (about 26 significant digits). It shares no code with the library and
/// evaluates each color step by step, without tables or combined stages.
/// </summary>
/// <remarks>
/// The reference gives the exact (unrounded) normalized destination values. A stored sample must be the correctly rounded
/// value (nearest, ties upward), or either neighbor when the exact value is within <see cref="TieWindow"/> of the rounding
/// boundary: the library computes in double precision, so a value this close to a boundary may legitimately round either
/// way, while a wrong formula, encoding or tag changes samples by orders of magnitude more.
/// </remarks>
public sealed class ReferenceIccTransform
{
    /// <summary>The distance to the rounding boundary, in sample units, under which either neighbor is accepted.</summary>
    public const decimal TieWindow = 0.0000001m;

    /// <summary>The largest accepted difference of a floating-point sample: one unit in the last place of a single at 1, rounded up.</summary>
    public const decimal SingleTolerance = 0.00000012m;

    private readonly ReferenceIccProfile _source;
    private readonly ReferenceIccProfile _destination;
    private readonly bool _identical;

    private ReferenceIccTransform(ReferenceIccProfile source, ReferenceIccProfile destination, bool identical)
    {
        _source = source;
        _destination = destination;
        _identical = identical;
    }

    /// <summary>Gets the number of source device channels.</summary>
    public int SourceChannelCount => _source.ChannelCount;

    /// <summary>Gets the number of destination device channels.</summary>
    public int DestinationChannelCount => _destination.ChannelCount;

    /// <summary>Creates the reference conversion between two profiles.</summary>
    /// <param name="source">The bytes of the source profile.</param>
    /// <param name="destination">The bytes of the destination profile.</param>
    /// <returns>The reference.</returns>
    public static ReferenceIccTransform Create(ReadOnlySpan<byte> source, ReadOnlySpan<byte> destination)
        => new(new ReferenceIccProfile(source), new ReferenceIccProfile(destination), source.SequenceEqual(destination));

    /// <summary>Converts one color.</summary>
    /// <param name="device">The normalized source device values.</param>
    /// <returns>The exact normalized destination device values, in [0, 1].</returns>
    public decimal[] Convert(ReadOnlySpan<decimal> device)
    {
        var values = device.ToArray();
        if (_identical)
            return values;

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ReferenceIccMath.Clip(values[i]);
        }

        var result = _destination.FromConnectionSpace(_source.ToConnectionSpace(values));
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ReferenceIccMath.Clip(result[i]);
        }

        return result;
    }

    /// <summary>Compares converted 8-bit samples with the reference.</summary>
    /// <param name="source">The interleaved source samples.</param>
    /// <param name="actual">The interleaved samples converted by the code under test.</param>
    /// <returns>A description of each sample that is not an accepted rounding of the reference; empty when all match.</returns>
    public IReadOnlyList<string> Compare(ReadOnlySpan<byte> source, ReadOnlySpan<byte> actual)
    {
        var sourceValues = new decimal[source.Length];
        var actualValues = new decimal[actual.Length];
        for (var i = 0; i < source.Length; i++)
        {
            sourceValues[i] = source[i];
        }

        for (var i = 0; i < actual.Length; i++)
        {
            actualValues[i] = actual[i];
        }

        return CompareIntegers(sourceValues, actualValues, byte.MaxValue);
    }

    /// <summary>Compares converted 16-bit samples with the reference.</summary>
    /// <param name="source">The interleaved source samples.</param>
    /// <param name="actual">The interleaved samples converted by the code under test.</param>
    /// <returns>A description of each sample that is not an accepted rounding of the reference; empty when all match.</returns>
    public IReadOnlyList<string> Compare(ReadOnlySpan<ushort> source, ReadOnlySpan<ushort> actual)
    {
        var sourceValues = new decimal[source.Length];
        var actualValues = new decimal[actual.Length];
        for (var i = 0; i < source.Length; i++)
        {
            sourceValues[i] = source[i];
        }

        for (var i = 0; i < actual.Length; i++)
        {
            actualValues[i] = actual[i];
        }

        return CompareIntegers(sourceValues, actualValues, ushort.MaxValue);
    }

    /// <summary>Compares converted floating-point samples with the reference.</summary>
    /// <param name="source">The interleaved source samples, in [0, 1].</param>
    /// <param name="actual">The interleaved samples converted by the code under test.</param>
    /// <returns>A description of each sample farther than <see cref="SingleTolerance"/> from the reference; empty when all match.</returns>
    public IReadOnlyList<string> Compare(ReadOnlySpan<float> source, ReadOnlySpan<float> actual)
    {
        var mismatches = new List<string>();
        var count = CheckLengths(source.Length, actual.Length, mismatches);
        var color = new decimal[SourceChannelCount];
        for (var i = 0; i < count; i++)
        {
            for (var channel = 0; channel < color.Length; channel++)
            {
                // Through double: the conversion of a single to decimal keeps 7 significant digits only
                color[channel] = (decimal)(double)source[(i * SourceChannelCount) + channel];
            }

            var expected = Convert(color);
            for (var channel = 0; channel < expected.Length; channel++)
            {
                var value = actual[(i * DestinationChannelCount) + channel];
                if (float.IsNaN(value) || Math.Abs((decimal)(double)value - expected[channel]) > SingleTolerance)
                {
                    mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"Color {i} ({string.Join(", ", color)}), channel {channel}: expected {expected[channel]}, actual {value:R}."));
                }
            }
        }

        return mismatches;
    }

    private List<string> CompareIntegers(decimal[] source, decimal[] actual, int maximum)
    {
        var mismatches = new List<string>();
        var count = CheckLengths(source.Length, actual.Length, mismatches);
        var color = new decimal[SourceChannelCount];
        for (var i = 0; i < count; i++)
        {
            for (var channel = 0; channel < color.Length; channel++)
            {
                color[channel] = source[(i * SourceChannelCount) + channel] / maximum;
            }

            var expected = Convert(color);
            for (var channel = 0; channel < expected.Length; channel++)
            {
                // Identical profiles keep the samples; otherwise nearest with ties upward
                var exact = expected[channel] * maximum;
                var shifted = exact + 0.5m;
                var rounded = decimal.Floor(shifted);
                var distance = Math.Min(shifted - rounded, decimal.Ceiling(shifted) - shifted);
                var value = actual[(i * DestinationChannelCount) + channel];
                var accepted = value == rounded || (distance <= TieWindow && Math.Abs(value - exact) <= 0.5m + TieWindow);
                if (!accepted)
                {
                    mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"Color {i} ({string.Join(", ", color.Select(v => v * maximum))}), channel {channel}: expected {rounded} (exact {exact}), actual {value}."));
                }
            }
        }

        return mismatches;
    }

    private int CheckLengths(int sourceLength, int actualLength, List<string> mismatches)
    {
        var count = sourceLength / SourceChannelCount;
        if (sourceLength % SourceChannelCount != 0 || actualLength != count * DestinationChannelCount)
        {
            mismatches.Add(string.Create(CultureInfo.InvariantCulture, $"Length mismatch: {sourceLength} source samples ({SourceChannelCount} per color), {actualLength} converted samples ({DestinationChannelCount} per color)."));
            return 0;
        }

        return count;
    }
}
