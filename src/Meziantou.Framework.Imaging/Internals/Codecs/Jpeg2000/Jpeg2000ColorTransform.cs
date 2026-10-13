namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The multiple component transformations of T.800, Annex G, and the DC level shifting of G.1, which turn the output of
/// the inverse wavelet transformation into unsigned samples.
/// </summary>
/// <remarks>
/// The numerical contract of the irreversible path: single precision, the constants of equation G-4 rounded once, each
/// expression evaluated in the written order; a sample is clamped to the range of its depth, then rounded to the nearest
/// integer, ties to even.
/// </remarks>
internal static class Jpeg2000ColorTransform
{
    /// <summary>The inverse reversible component transformation (equation G-2), in place.</summary>
    public static void InverseReversible(Span<int> y0, Span<int> y1, Span<int> y2)
    {
        for (var i = 0; i < y0.Length; i++)
        {
            var green = y0[i] - ((y2[i] + y1[i]) >> 2);
            var red = y2[i] + green;
            var blue = y1[i] + green;
            y0[i] = red;
            y1[i] = green;
            y2[i] = blue;
        }
    }

    /// <summary>The inverse irreversible component transformation (equation G-4), in place.</summary>
    public static void InverseIrreversible(Span<float> y0, Span<float> y1, Span<float> y2)
    {
        for (var i = 0; i < y0.Length; i++)
        {
            var luminance = y0[i];
            var blueDifference = y1[i];
            var redDifference = y2[i];
            var red = luminance + (1.402f * redDifference);
            var green = (luminance - (0.34413f * blueDifference)) - (0.71414f * redDifference);
            var blue = luminance + (1.772f * blueDifference);
            y0[i] = red;
            y1[i] = green;
            y2[i] = blue;
        }
    }

    /// <summary>Shifts integer samples by half of their range (equation G-1) and clamps them to the range of their depth, in place.</summary>
    /// <param name="samples">The samples.</param>
    /// <param name="precision">The number of bits of a sample.</param>
    public static void ShiftLevel(Span<int> samples, int precision)
    {
        var offset = 1 << (precision - 1);
        var maximum = (1 << precision) - 1;
        foreach (ref var sample in samples)
        {
            sample = Math.Clamp(sample + offset, 0, maximum);
        }
    }

    /// <summary>Rounds floating-point samples, shifts them by half of their range and clamps them to the range of their depth.</summary>
    /// <param name="source">The samples.</param>
    /// <param name="destination">Receives the samples; it may be the memory of <paramref name="source"/>.</param>
    /// <param name="precision">The number of bits of a sample.</param>
    public static void ShiftLevel(ReadOnlySpan<float> source, Span<int> destination, int precision)
    {
        var offset = 1 << (precision - 1);
        float minimum = -offset;
        float maximum = offset - 1;
        for (var i = 0; i < source.Length; i++)
        {
            // A value that is not a number cannot be produced by finite coefficients; it would become the middle of the range
            var value = MathF.Round(Math.Clamp(source[i], minimum, maximum), MidpointRounding.ToEven);
            destination[i] = (int)value + offset;
        }
    }
}
