using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The exact mapping of one axis of a resize: output pixel <c>d</c> (0-based) has its center
/// at the continuous source coordinate <c>u(d) = (Offset + (2d + 1) * Step) / Denominator</c>, where the continuous
/// coordinate of source pixel <c>i</c> spans <c>[i, i + 1)</c> and its center is <c>i + 0.5</c>. The scale factor
/// (output pixels per source pixel) is <c>s = Denominator / (2 * Step)</c>; consecutive output centers are <c>1 / s</c>
/// source pixels apart.
/// </summary>
/// <remarks>
/// Every quantity is an exact integer, so pixel-center positions are rational and nearest-neighbor indices are computed
/// without rounding error (128-bit intermediates; no overflow for any <see cref="int"/> sizes).
/// </remarks>
/// <param name="SourceLength">The number of source pixels on this axis.</param>
/// <param name="OutputLength">The number of output pixels on this axis.</param>
/// <param name="Offset">The non-negative numerator offset (the Cover crop position); zero for Stretch and Contain.</param>
/// <param name="Step">The positive half-step numerator.</param>
/// <param name="Denominator">The positive denominator.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ResampleAxis(int SourceLength, int OutputLength, Int128 Offset, long Step, long Denominator)
{
    /// <summary>Creates the axis that maps the whole source extent onto the whole output extent (Stretch, Contain, and the limiting axis of Cover).</summary>
    public static ResampleAxis FullExtent(int sourceLength, int outputLength)
        => new(sourceLength, outputLength, Offset: 0, Step: sourceLength, Denominator: 2L * outputLength);

    /// <summary>
    /// Creates a Cover axis: the source is scaled by the exact factor <c>scaleNumerator / scaleDenominator</c> (the same on
    /// both axes) and the output window is placed at <c>anchor2 / 2</c> of the overflow (0: start, 1: center, 2: end).
    /// </summary>
    public static ResampleAxis Cover(int sourceLength, int outputLength, long scaleNumerator, long scaleDenominator, int anchor2)
    {
        Debug.Assert(anchor2 is >= 0 and <= 2);
        var overflow = ((Int128)sourceLength * scaleNumerator) - ((Int128)outputLength * scaleDenominator);
        Debug.Assert(overflow >= 0);
        return new(sourceLength, outputLength, anchor2 * overflow, scaleDenominator, 2 * scaleNumerator);
    }

    /// <summary>Gets a value indicating whether the axis maps every output pixel center exactly onto the same source pixel center.</summary>
    public bool IsIdentity => SourceLength == OutputLength && Offset == 0 && Denominator == 2 * Step;

    /// <summary>
    /// Gets the kernel stretch factor: <c>max(1, 1 / s)</c>. When downsampling, the kernel support is widened by this factor
    /// so that every source pixel contributes.
    /// </summary>
    public double FilterScale => 2 * Step > Denominator ? (double)(2 * Step) / Denominator : 1.0;

    /// <summary>
    /// Gets the source pixel that contains the center of output pixel <paramref name="index"/>: <c>floor(u(d))</c>, clamped
    /// to the source. A center that falls exactly on the boundary between two source pixels selects the higher index.
    /// </summary>
    public int GetNearestIndex(int index)
    {
        var numerator = Offset + ((2 * (Int128)index) + 1) * Step;
        var result = numerator / Denominator;
        return result >= SourceLength ? SourceLength - 1 : (int)result;
    }

    /// <summary>Gets the center of output pixel <paramref name="index"/> in source index coordinates: <c>u(d) - 0.5</c> (exact integer part, rounded fraction).</summary>
    public double GetCenter(int index)
    {
        var numerator = Offset + ((2 * (Int128)index) + 1) * Step;
        var (quotient, remainder) = Int128.DivRem(numerator, Denominator);
        return (double)quotient + ((double)remainder / Denominator) - 0.5;
    }
}
