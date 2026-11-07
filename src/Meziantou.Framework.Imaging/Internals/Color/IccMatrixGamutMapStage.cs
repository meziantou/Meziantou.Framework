namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The gamut mapping of a destination whose device values are tone curves of a linear combination of CIEXYZ (a
/// matrix-based RGB profile, a CICP color space): a color is in the gamut when its linear values are within the range of
/// the curves, and clipping clamps them to it. The search is the one of CSS Color 4 section 13.2.2, which returns the
/// clipped color once it is less than a just noticeable difference from the reduced one.
/// </summary>
internal sealed class IccMatrixGamutMapStage : IccGamutMapStage
{
    private readonly double[] _toLinear;
    private readonly double[] _fromLinear;
    private readonly double[] _low;
    private readonly double[] _high;

    private IccMatrixGamutMapStage(double[] toLinear, double[] fromLinear, double[] low, double[] high)
    {
        _toLinear = toLinear;
        _fromLinear = fromLinear;
        _low = low;
        _high = high;
    }

    /// <summary>Creates the stage, or returns <see langword="null"/> when the matrix has no inverse.</summary>
    /// <param name="toConnectionSpace">The matrix converting the linear values to CIEXYZ of the connection space, row by row.</param>
    /// <param name="low">The smallest linear value of each channel.</param>
    /// <param name="high">The largest linear value of each channel.</param>
    public static IccMatrixGamutMapStage? Create(ReadOnlySpan<double> toConnectionSpace, ReadOnlySpan<double> low, ReadOnlySpan<double> high)
    {
        var inverse = new double[9];
        if (!IccMatrixStage.TryInvert(toConnectionSpace, inverse))
            return null;

        return new IccMatrixGamutMapStage(inverse, toConnectionSpace.ToArray(), low.ToArray(), high.ToArray());
    }

    protected override void Map(Span<double> values, double lightness, double a, double b)
    {
        Span<double> linear = stackalloc double[3];
        ToLinear(values[0], values[1], values[2], linear);
        if (IsInside(linear))
            return;

        // The clipped origin, kept when it is close enough
        var (clippedX, clippedY, clippedZ) = Clip(linear);
        if (DeltaEOK(lightness, a, b, clippedX, clippedY, clippedZ) >= JustNoticeableDifference)
        {
            var chroma = Math.Sqrt((a * a) + (b * b));
            var min = 0.0;
            var max = chroma;
            var minInGamut = true;
            while (max - min > Epsilon)
            {
                var candidate = (min + max) / 2;
                var scale = candidate / chroma;
                var currentA = a * scale;
                var currentB = b * scale;
                var (x, y, z) = FromOklab(lightness, currentA, currentB);
                ToLinear(x, y, z, linear);
                if (minInGamut && IsInside(linear))
                {
                    min = candidate;
                    continue;
                }

                (clippedX, clippedY, clippedZ) = Clip(linear);
                var difference = DeltaEOK(lightness, currentA, currentB, clippedX, clippedY, clippedZ);
                if (difference < JustNoticeableDifference)
                {
                    if (JustNoticeableDifference - difference < Epsilon)
                        break;

                    minInGamut = false;
                    min = candidate;
                }
                else
                {
                    max = candidate;
                }
            }
        }

        values[0] = clippedX;
        values[1] = clippedY;
        values[2] = clippedZ;
    }

    private void ToLinear(double x, double y, double z, Span<double> linear)
    {
        var m = _toLinear;
        linear[0] = (m[0] * x) + (m[1] * y) + (m[2] * z);
        linear[1] = (m[3] * x) + (m[4] * y) + (m[5] * z);
        linear[2] = (m[6] * x) + (m[7] * y) + (m[8] * z);
    }

    private bool IsInside(ReadOnlySpan<double> linear)
    {
        for (var i = 0; i < 3; i++)
        {
            if (!(linear[i] >= _low[i] && linear[i] <= _high[i]))
                return false;
        }

        return true;
    }

    private (double X, double Y, double Z) Clip(ReadOnlySpan<double> linear)
    {
        var r = Math.Clamp(linear[0], _low[0], _high[0]);
        var g = Math.Clamp(linear[1], _low[1], _high[1]);
        var b = Math.Clamp(linear[2], _low[2], _high[2]);
        var m = _fromLinear;
        return ((m[0] * r) + (m[1] * g) + (m[2] * b), (m[3] * r) + (m[4] * g) + (m[5] * b), (m[6] * r) + (m[7] * g) + (m[8] * b));
    }
}
