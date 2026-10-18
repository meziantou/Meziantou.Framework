namespace Meziantou.Framework.WebPlatform.Css.Cascade;

internal static partial class CssColorConversion
{
    /// <summary>
    /// CSS gamut maps a color to an RGB color space (https://drafts.csswg.org/css-color-4/#css-gamut-mapping) with the Ray Trace
    /// algorithm (https://drafts.csswg.org/css-color-4/#raytrace): a color inside of the gamut is converted as is, the others have
    /// their OkLCh chroma reduced at constant lightness and hue to the gamut boundary; a lightness of at least 100% is white and
    /// of at most 0% is black. The channels of the result are in [0, 1].
    /// </summary>
    /// <param name="space">The color space of the color.</param>
    /// <param name="channels">The channels of the color (the missing components are zero).</param>
    /// <param name="destination">The RGB color space of the result: <c>srgb</c> or <c>display-p3</c>.</param>
    public static double[] GamutMap(string space, double[] channels, string destination)
    {
        if (space == destination && IsInGamut(channels, 0, 1))
            return channels;

        var linearSpace = destination switch
        {
            "srgb" => "srgb-linear",
            "display-p3" => "display-p3-linear",
            _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, message: null),
        };

        var rgb = IsSrgbSpace(space) && destination == "srgb" ? ToSrgb(space, channels) : FromXyz(destination, ToXyz(space, channels));
        if (IsInGamut(rgb, 0, 1))
            return rgb;

        // The OkLab and OkLCh colors are not converted through XYZ: the round trip can move a lightness of 100% just below it
        // (by an ulp, depending on the cube root of the platform), and the anchor of the ray would then be outside of the gamut
        var origin = space switch
        {
            "oklch" => channels,
            "oklab" => LabToLch(channels, 0.000004),
            _ => FromXyz("oklch", ToXyz(space, channels)),
        };
        var lightness = origin[0];
        if (lightness >= 1)
            return Clip(FromXyz(destination, OklabToXyz([1, 0, 0])));

        if (lightness <= 0)
            return Clip(FromXyz(destination, OklabToXyz([0, 0, 0])));

        var hue = double.IsNaN(origin[2]) ? 0 : origin[2];
        var anchor = FromXyz(linearSpace, OklabToXyz(LchToLab([lightness, 0, hue])));

        // The achromatic color of a lightness very close to 0% or 100% can be just outside of the gamut (the white points of
        // OkLab and of the RGB space differ by the rounding of their matrices): it is the closest color of the gamut
        if (!IsInGamut(anchor, 0, 1))
            return Clip(Map(Clip(anchor), LinearToSrgb));

        var current = FromXyz(linearSpace, OklabToXyz(LchToLab([lightness, origin[1], hue])));
        if (!IsInGamut(current, 0, 1))
        {
            const double Low = 1e-12;
            const double High = 1 - 1e-12;
            var last = current;
            for (var i = 0; i < 4; i++)
            {
                if (i > 0)
                {
                    // Projects the color back onto the chroma reduction path (constant OkLCh lightness and hue)
                    var lch = FromXyz("oklch", ToXyz(linearSpace, current));
                    current = FromXyz(linearSpace, OklabToXyz(LchToLab([lightness, lch[1], hue])));
                }

                if (CastRay(anchor, current) is not { } intersection)
                {
                    current = last;
                    break;
                }

                // A color below the gamut surface is a closer anchor
                if (i > 0 && IsInGamut(current, Low, High))
                {
                    anchor = current;
                }

                current = intersection;
                last = intersection;
            }
        }

        return Clip(Map(Clip(current), LinearToSrgb));
    }

    private static bool IsInGamut(double[] rgb, double min, double max) => rgb[0] >= min && rgb[0] <= max && rgb[1] >= min && rgb[1] <= max && rgb[2] >= min && rgb[2] <= max;

    private static double[] Clip(double[] rgb) => [Math.Clamp(rgb[0], 0, 1), Math.Clamp(rgb[1], 0, 1), Math.Clamp(rgb[2], 0, 1)];

    /// <summary>
    /// Casts a ray through a linear-light RGB space from a point inside of the gamut to a point outside of it, and returns its
    /// intersection with the gamut boundary (the unit cube), or null when there is none (https://drafts.csswg.org/css-color-4/#raytrace).
    /// </summary>
    private static double[]? CastRay(double[] start, double[] end)
    {
        var far = double.PositiveInfinity;
        var near = double.NegativeInfinity;
        var direction = new double[3];
        for (var i = 0; i < 3; i++)
        {
            var a = start[i];
            var d = end[i] - a;
            direction[i] = d;
            if (Math.Abs(d) > 1e-12)
            {
                var t1 = (0 - a) / d;
                var t2 = (1 - a) / d;
                near = Math.Max(Math.Min(t1, t2), near);
                far = Math.Min(Math.Max(t1, t2), far);
            }
            else if (a < 0 || a > 1)
            {
                return null;
            }
        }

        if (near > far || far < 0)
            return null;

        if (near < 0)
        {
            near = far;
        }

        if (double.IsInfinity(near))
            return null;

        return [start[0] + (direction[0] * near), start[1] + (direction[1] * near), start[2] + (direction[2] * near)];
    }
}
