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

    /// <summary>Combines a paint with a pixel with an operator of Porter and Duff, from its definition.</summary>
    /// <remarks>
    /// With all alphas as fractions of one and the factors <c>Fa</c> and <c>Fb</c> of the operator, the operator gives a
    /// result of alpha <c>Fa as + Fb ad</c> whose color multiplied by its alpha is <c>Fa as sc + Fb ad dc</c>. The pixel
    /// is that result for the part of it the shape covers, and what it was for the rest. Alpha and colors are rounded
    /// to nearest, ties upward. A pixel whose alpha rounds to zero becomes transparent black, or stays as it is when
    /// it was fully transparent. A layout without alpha is opaque before and after: it gets the color multiplied by
    /// the alpha the result would have.
    /// </remarks>
    /// <param name="destination">The samples of the pixel, in storage order.</param>
    /// <param name="paint">The paint.</param>
    /// <param name="coverage">The coverage, from 0 to <see cref="ReferenceCoverage.FullCoverage"/>.</param>
    /// <param name="layout">The layout of the pixel.</param>
    /// <param name="mode">The operator.</param>
    /// <returns>The samples of the result.</returns>
    public static int[] Blend(IReadOnlyList<int> destination, ReferencePaint paint, int coverage, ReferencePixelLayout layout, ReferenceCompositeMode mode)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(paint);
        ArgumentNullException.ThrowIfNull(layout);
        var result = destination.ToArray();
        var max = layout.MaxValue;
        var covered = new Fraction(coverage, ReferenceCoverage.FullCoverage);
        if (covered == 0)
            return result;

        Fraction one = 1;
        Fraction zero = 0;
        var sourceAlpha = new Fraction(paint.Alpha, max);
        var destinationAlpha = layout.HasAlpha ? new Fraction(destination[layout.ColorChannels], max) : one;
        var (sourceFactor, destinationFactor) = mode switch
        {
            ReferenceCompositeMode.SourceOver => (one, one - sourceAlpha),
            ReferenceCompositeMode.Source => (one, zero),
            ReferenceCompositeMode.DestinationOver => (one - destinationAlpha, one),
            ReferenceCompositeMode.SourceIn => (destinationAlpha, zero),
            ReferenceCompositeMode.DestinationIn => (zero, sourceAlpha),
            ReferenceCompositeMode.SourceOut => (one - destinationAlpha, zero),
            ReferenceCompositeMode.DestinationOut => (zero, one - sourceAlpha),
            ReferenceCompositeMode.SourceAtop => (destinationAlpha, one - sourceAlpha),
            ReferenceCompositeMode.DestinationAtop => (one - destinationAlpha, sourceAlpha),
            ReferenceCompositeMode.Xor => (one - destinationAlpha, one - sourceAlpha),
            ReferenceCompositeMode.Clear => (zero, zero),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        // What the pixel keeps of what it was, and what the operator gives where the shape is
        var kept = (one - covered) * destinationAlpha;
        var fromSource = covered * sourceFactor * sourceAlpha;
        var fromDestination = covered * destinationFactor * destinationAlpha;
        var outputAlpha = kept + fromSource + fromDestination;
        var premultiplied = new Fraction[layout.ColorChannels];
        for (var c = 0; c < layout.ColorChannels; c++)
        {
            premultiplied[c] = ((kept + fromDestination) * destination[c]) + (fromSource * paint.Color[c]);
        }

        if (!layout.HasAlpha)
        {
            for (var c = 0; c < layout.ColorChannels; c++)
            {
                result[c] = RoundHalfUp(premultiplied[c]);
            }

            return result;
        }

        var roundedAlpha = RoundHalfUp(outputAlpha * max);
        if (roundedAlpha == 0)
        {
            if (destination[layout.ColorChannels] != 0)
            {
                Array.Clear(result);
            }

            return result;
        }

        for (var c = 0; c < layout.ColorChannels; c++)
        {
            result[c] = RoundHalfUp(premultiplied[c] / outputAlpha);
        }

        result[layout.ColorChannels] = roundedAlpha;
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
