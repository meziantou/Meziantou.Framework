namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The gamut mapping of a destination based on lookup tables, whose gamut has no closed form: a color is in the gamut
/// when its round trip through the colorimetric tables of the destination (from the connection space to the device
/// values, clipped, then back) moves it by less than the just noticeable difference of CSS Color 4. An in-gamut color is
/// unchanged; otherwise the binary search of CSS Color 4 finds the largest chroma that is in the gamut, and the color
/// of that chroma is the result (the conversion to the device values follows).
/// </summary>
internal sealed class IccRoundTripGamutMapStage(IccStage[] toDevice, IccStage[] toConnectionSpace, int deviceChannels) : IccGamutMapStage
{
    protected override void Map(Span<double> values, double lightness, double a, double b)
    {
        Span<double> scratch = stackalloc double[IccPipeline.MaxChannels];
        if (IsInside(values[0], values[1], values[2], lightness, a, b, scratch))
            return;

        var chroma = Math.Sqrt((a * a) + (b * b));
        if (chroma == 0)
            return;

        var min = 0.0;
        var max = chroma;
        while (max - min > Epsilon)
        {
            var candidate = (min + max) / 2;
            var scale = candidate / chroma;
            var (x, y, z) = FromOklab(lightness, a * scale, b * scale);
            if (IsInside(x, y, z, lightness, a * scale, b * scale, scratch))
            {
                min = candidate;
            }
            else
            {
                max = candidate;
            }
        }

        var minScale = min / chroma;
        (values[0], values[1], values[2]) = FromOklab(lightness, a * minScale, b * minScale);
    }

    private bool IsInside(double x, double y, double z, double lightness, double a, double b, Span<double> scratch)
    {
        scratch.Clear();
        scratch[0] = x;
        scratch[1] = y;
        scratch[2] = z;
        foreach (var stage in toDevice)
        {
            stage.Apply(scratch);
        }

        for (var i = 0; i < deviceChannels; i++)
        {
            scratch[i] = IccCurve.Clip(scratch[i]);
        }

        foreach (var stage in toConnectionSpace)
        {
            stage.Apply(scratch);
        }

        return DeltaEOK(lightness, a, b, scratch[0], scratch[1], scratch[2]) < JustNoticeableDifference;
    }
}
