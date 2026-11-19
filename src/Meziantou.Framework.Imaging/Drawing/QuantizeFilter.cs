using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// Rounds the colors multiplied by their alpha (premultiplied) and the alpha to a number of bits per sample, as a
/// texture of that depth holds them: the last filter of a chain gives the result of a filter a browser draws on an
/// 8-bit premultiplied texture.
/// </summary>
/// <remarks>
/// Each value <c>v</c> of a pixel, a fraction of one, becomes <c>floor(M v + 1/2) / M</c>, with <c>M = 2^n - 1</c>:
/// the colors are rounded multiplied by the alpha, so a color is kept with the precision its alpha leaves it. The
/// result is then written to the samples of the layer as any filter result is (<see cref="Filter"/>): a layer of 8 bits
/// per sample gets the straight color closest to the rounded value, and a layer of 16 bits per sample keeps it
/// nearly exactly.
/// </remarks>
public sealed class QuantizeFilter : Filter
{
    /// <summary>Initializes a new instance of the <see cref="QuantizeFilter"/> class.</summary>
    /// <param name="bitsPerSample">The number of bits per sample of the values. Must be between 1 and 16.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bitsPerSample"/> is not between 1 and 16.</exception>
    public QuantizeFilter(int bitsPerSample)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bitsPerSample, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitsPerSample, 16);
        BitsPerSample = bitsPerSample;
    }

    /// <summary>Gets the number of bits per sample of the values.</summary>
    public int BitsPerSample { get; }

    internal override void Apply(FilterProcessor processor, FilterSurface surface) => processor.Quantize(surface, (1 << BitsPerSample) - 1);

    internal override (double X, double Y) GetReach() => (0, 0);

    // A transparent pixel stays transparent, and the values left by the running sums of a blur beyond its reach round to zero
    internal override int GetLocalSteps(bool isAfterReach) => 1;

    // A transparent pixel stays transparent
    internal override bool TryGetBoundsCore(RectangleD bounds, out RectangleD result)
    {
        result = bounds;
        return true;
    }
}
