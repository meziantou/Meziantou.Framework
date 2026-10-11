using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>
/// Source-over compositing of a paint weighted by a coverage, written from its definition with rational numbers (no
/// integer rearrangement, no fast path).
/// </summary>
/// <remarks>
/// With all alphas as fractions of one, the source alpha is <c>as = paint alpha * coverage</c>, the destination alpha is
/// <c>ad</c> (1 for a format without alpha), the output alpha is <c>ao = as + ad (1 - as)</c> and each output color is
/// <c>(sc as + dc ad (1 - as)) / ao</c>. Alpha and colors are rounded to nearest, ties upward. A pixel is not written
/// when <c>as</c> is zero or when the output alpha rounds to zero.
/// </remarks>
public static class ReferenceCompositor
{
    /// <summary>Composites a paint over a pixel.</summary>
    /// <param name="destination">The samples of the pixel, in storage order.</param>
    /// <param name="paint">The paint.</param>
    /// <param name="coverage">The coverage, from 0 to <see cref="ReferenceCoverage.FullCoverage"/>.</param>
    /// <param name="layout">The layout of the pixel.</param>
    /// <returns>The samples of the result.</returns>
    public static int[] Blend(IReadOnlyList<int> destination, ReferencePaint paint, int coverage, ReferencePixelLayout layout)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(layout);
        var result = destination.ToArray();
        var max = layout.MaxValue;
        var sourceAlpha = new Fraction(paint.Alpha, max) * new Fraction(coverage, ReferenceCoverage.FullCoverage);
        if (sourceAlpha == 0)
            return result;

        var destinationAlpha = layout.HasAlpha ? new Fraction(destination[layout.ColorChannels], max) : 1;
        var keep = destinationAlpha * ((Fraction)1 - sourceAlpha);
        var outputAlpha = sourceAlpha + keep;
        var roundedAlpha = RoundHalfUp(outputAlpha * max);
        if (roundedAlpha == 0)
            return result;

        for (var c = 0; c < layout.ColorChannels; c++)
        {
            result[c] = RoundHalfUp((((Fraction)paint.Color[c] * sourceAlpha) + ((Fraction)destination[c] * keep)) / outputAlpha);
        }

        if (layout.HasAlpha)
        {
            result[layout.ColorChannels] = roundedAlpha;
        }

        return result;
    }

    /// <summary>Checks that a pixel is the compositing of one of the accepted paints for one of the accepted coverages.</summary>
    /// <param name="destination">The samples of the pixel before, in storage order.</param>
    /// <param name="actual">The samples of the pixel after.</param>
    /// <param name="paints">The accepted paints.</param>
    /// <param name="lowCoverage">The lowest accepted coverage.</param>
    /// <param name="highCoverage">The highest accepted coverage.</param>
    /// <param name="layout">The layout of the pixel.</param>
    /// <returns><see langword="true"/> if the pixel is accepted.</returns>
    public static bool IsAccepted(IReadOnlyList<int> destination, IReadOnlyList<int> actual, IEnumerable<ReferencePaint> paints, int lowCoverage, int highCoverage, ReferencePixelLayout layout)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(paints);
        foreach (var paint in paints)
        {
            for (var coverage = lowCoverage; coverage <= highCoverage; coverage++)
            {
                if (Blend(destination, paint, coverage, layout).SequenceEqual(actual))
                    return true;
            }
        }

        return false;
    }

    private static int RoundHalfUp(Fraction value) => (int)(value + new Fraction(1, 2)).Floor();
}
