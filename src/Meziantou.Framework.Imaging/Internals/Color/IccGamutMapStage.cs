namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Brings a color of the profile connection space (CIEXYZ relative to D50 and to the media white of the destination)
/// into the gamut of the destination by reducing its chroma toward the neutral axis, at constant lightness and hue in
/// Oklab (<see cref="GamutMapping.ReduceChroma"/>). Written from CSS Color Module Level 4, section 13.2 "CSS Gamut
/// Mapping to an RGB Destination": the color space conversions are those of section 18 (<see cref="ColorValueMath"/>).
/// </summary>
/// <remarks>
/// A color whose Oklab lightness is at least 1 becomes the white of the connection space, one whose lightness is at most
/// 0 becomes black, and an in-gamut color is unchanged. The chroma is reduced by scaling a and b together, which keeps
/// the hue exactly. The stage allocates nothing.
/// </remarks>
internal abstract class IccGamutMapStage : IccStage
{
    /// <summary>The just noticeable difference of CSS Color 4: a distance in Oklab (<c>deltaEOK</c>).</summary>
    protected const double JustNoticeableDifference = 0.02;

    /// <summary>The precision of the binary search on the chroma (CSS Color 4: <c>epsilon</c>).</summary>
    protected const double Epsilon = 0.0001;

    public sealed override void Apply(Span<double> values)
    {
        var x = values[0];
        var y = values[1];
        var z = values[2];
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            return;

        var (lightness, a, b) = ToOklab(x, y, z);
        if (lightness >= 1)
        {
            values[0] = IccColorimetry.D50X;
            values[1] = IccColorimetry.D50Y;
            values[2] = IccColorimetry.D50Z;
            return;
        }

        if (lightness <= 0)
        {
            values[0] = 0;
            values[1] = 0;
            values[2] = 0;
            return;
        }

        Map(values, lightness, a, b);
    }

    /// <summary>Maps a color whose lightness is in (0, 1): <paramref name="values"/> holds its CIEXYZ on input, the mapped CIEXYZ on output.</summary>
    protected abstract void Map(Span<double> values, double lightness, double a, double b);

    /// <summary>Converts CIEXYZ of the connection space to Oklab, through the Bradford adaptation to D65 (CSS Color 4, <c>D50_to_D65</c>, <c>XYZ_to_OKLab</c>).</summary>
    protected static (double L, double A, double B) ToOklab(double x, double y, double z)
    {
        var (x65, y65, z65) = ColorValueMath.D50ToD65.Transform(x, y, z);
        return ColorValueMath.XyzD65ToOklab(x65, y65, z65);
    }

    /// <summary>Converts Oklab to CIEXYZ of the connection space (CSS Color 4, <c>OKLab_to_XYZ</c>, <c>D65_to_D50</c>).</summary>
    protected static (double X, double Y, double Z) FromOklab(double lightness, double a, double b)
    {
        var (x65, y65, z65) = ColorValueMath.OklabToXyzD65(lightness, a, b);
        return ColorValueMath.D65ToD50.Transform(x65, y65, z65);
    }

    /// <summary>Computes the distance in Oklab between a color given in Oklab and a color given in CIEXYZ (CSS Color 4, <c>deltaEOK</c>).</summary>
    protected static double DeltaEOK(double lightness, double a, double b, double x, double y, double z)
    {
        var (otherLightness, otherA, otherB) = ToOklab(x, y, z);
        var dl = lightness - otherLightness;
        var da = a - otherA;
        var db = b - otherB;
        return Math.Sqrt((dl * dl) + (da * da) + (db * db));
    }
}
