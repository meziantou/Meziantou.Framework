using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// Computes color-layers() (https://drafts.csswg.org/css-color-6/#color-layers): the colors are composited with the Source
/// Over operator and the blend mode (https://drafts.fxtf.org/compositing-1/#generalformula), the first color on top, as the
/// layers of a background. The specification does not give the color space of the compositing: as the layers of a
/// background, each color is CSS gamut mapped to sRGB (https://drafts.csswg.org/css-color-4/#css-gamut-mapping), then the
/// colors are composited in sRGB (the gamma-encoded components).
/// </summary>
internal static class CssColorLayers
{
    /// <summary>
    /// Computes a color-layers() function; <paramref name="resolveColor"/> resolves its colors. Returns null when the function
    /// cannot be computed (e.g. a color is currentcolor).
    /// </summary>
    public static CssColor? Compute(CssFunction function, Func<CssComponentValue, CssColor?> resolveColor)
    {
        var arguments = CssValueComputer.SplitArguments(function.Value);
        var blendMode = "normal";
        var index = 0;
        if (arguments.Count > 1 && arguments[0] is [CssPreservedToken { Type: CssTokenType.Ident } ident] && IsBlendMode(InfraAscii.ToAsciiLowercase(ident.Token.Value)))
        {
            blendMode = InfraAscii.ToAsciiLowercase(ident.Token.Value);
            index = 1;
        }

        if (arguments.Count - index < 1)
            return null;

        var layers = new (double R, double G, double B, double A)[arguments.Count - index];
        for (var i = index; i < arguments.Count; i++)
        {
            if (arguments[i] is not [var value] || resolveColor(value) is not { } color || CssColorConversion.FromColor(color) is not { } components)
                return null;

            var srgb = CssColorConversion.GamutMap(components.Space, [.. components.Channels.Select(c => c ?? 0)], "srgb");
            layers[i - index] = (srgb[0], srgb[1], srgb[2], Math.Clamp(components.Alpha ?? 0, 0, 1));
        }

        // From the bottom layer (the last color) to the top one
        double r = 0, g = 0, b = 0, alpha = 0;
        for (var i = layers.Length - 1; i >= 0; i--)
        {
            var source = layers[i];
            var (blendedR, blendedG, blendedB) = Blend(blendMode, (r, g, b), (source.R, source.G, source.B));

            // Cs' = (1 - ab) Cs + ab B(Cb, Cs), then co = as Cs' + (1 - as) ab Cb and ao = as + ab (1 - as)
            var mixedR = ((1 - alpha) * source.R) + (alpha * blendedR);
            var mixedG = ((1 - alpha) * source.G) + (alpha * blendedG);
            var mixedB = ((1 - alpha) * source.B) + (alpha * blendedB);
            var resultAlpha = source.A + (alpha * (1 - source.A));
            if (resultAlpha <= 0)
            {
                (r, g, b, alpha) = (0, 0, 0, 0);
                continue;
            }

            r = ((source.A * mixedR) + ((1 - source.A) * alpha * r)) / resultAlpha;
            g = ((source.A * mixedG) + ((1 - source.A) * alpha * g)) / resultAlpha;
            b = ((source.A * mixedB) + ((1 - source.A) * alpha * b)) / resultAlpha;
            alpha = resultAlpha;
        }

        return CssColorConversion.ToColor(new CssColorComponents("srgb", [r, g, b], alpha));
    }

    private static bool IsBlendMode(string name) => name is "normal" or "multiply" or "screen" or "overlay" or "darken" or "lighten" or "color-dodge" or "color-burn" or "hard-light" or "soft-light" or "difference" or "exclusion" or "hue" or "saturation" or "color" or "luminosity";

    /// <summary>Returns the blending of a source color with a backdrop color (https://drafts.fxtf.org/compositing-1/#blending).</summary>
    private static (double R, double G, double B) Blend(string mode, (double R, double G, double B) backdrop, (double R, double G, double B) source)
    {
        switch (mode)
        {
            case "hue":
                return SetLuminosity(SetSaturation(source, Saturation(backdrop)), Luminosity(backdrop));
            case "saturation":
                return SetLuminosity(SetSaturation(backdrop, Saturation(source)), Luminosity(backdrop));
            case "color":
                return SetLuminosity(source, Luminosity(backdrop));
            case "luminosity":
                return SetLuminosity(backdrop, Luminosity(source));
            default:
                return (BlendSeparable(mode, backdrop.R, source.R), BlendSeparable(mode, backdrop.G, source.G), BlendSeparable(mode, backdrop.B, source.B));
        }
    }

    private static double BlendSeparable(string mode, double backdrop, double source) => mode switch
    {
        "multiply" => backdrop * source,
        "screen" => Screen(backdrop, source),
        "overlay" => HardLight(source, backdrop),
        "darken" => Math.Min(backdrop, source),
        "lighten" => Math.Max(backdrop, source),
        "color-dodge" => backdrop == 0 ? 0 : source >= 1 ? 1 : Math.Min(1, backdrop / (1 - source)),
        "color-burn" => backdrop >= 1 ? 1 : source <= 0 ? 0 : 1 - Math.Min(1, (1 - backdrop) / source),
        "hard-light" => HardLight(backdrop, source),
        "soft-light" => SoftLight(backdrop, source),
        "difference" => Math.Abs(backdrop - source),
        "exclusion" => backdrop + source - (2 * backdrop * source),
        _ => source,
    };

    private static double Screen(double backdrop, double source) => backdrop + source - (backdrop * source);

    private static double HardLight(double backdrop, double source) => source <= 0.5 ? backdrop * 2 * source : Screen(backdrop, (2 * source) - 1);

    private static double SoftLight(double backdrop, double source)
    {
        if (source <= 0.5)
            return backdrop - ((1 - (2 * source)) * backdrop * (1 - backdrop));

        var d = backdrop <= 0.25 ? ((((16 * backdrop) - 12) * backdrop) + 4) * backdrop : Math.Sqrt(backdrop);
        return backdrop + (((2 * source) - 1) * (d - backdrop));
    }

    private static double Luminosity((double R, double G, double B) color) => (0.3 * color.R) + (0.59 * color.G) + (0.11 * color.B);

    private static (double R, double G, double B) SetLuminosity((double R, double G, double B) color, double luminosity)
    {
        var d = luminosity - Luminosity(color);
        return ClipColor((color.R + d, color.G + d, color.B + d));
    }

    private static (double R, double G, double B) ClipColor((double R, double G, double B) color)
    {
        var l = Luminosity(color);
        var n = Math.Min(color.R, Math.Min(color.G, color.B));
        var x = Math.Max(color.R, Math.Max(color.G, color.B));
        if (n < 0 && l != n)
        {
            color = (l + ((color.R - l) * l / (l - n)), l + ((color.G - l) * l / (l - n)), l + ((color.B - l) * l / (l - n)));
        }

        if (x > 1 && x != l)
        {
            color = (l + ((color.R - l) * (1 - l) / (x - l)), l + ((color.G - l) * (1 - l) / (x - l)), l + ((color.B - l) * (1 - l) / (x - l)));
        }

        return color;
    }

    private static double Saturation((double R, double G, double B) color) => Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));

    private static (double R, double G, double B) SetSaturation((double R, double G, double B) color, double saturation)
    {
        // The largest component becomes the saturation, the smallest 0, and the middle one is scaled between them
        double[] components = [color.R, color.G, color.B];
        var max = Array.IndexOf(components, components.Max());
        var min = Array.IndexOf(components, components.Min());
        if (min == max)
        {
            min = (max + 1) % 3;
        }

        var mid = 3 - max - min;
        var result = new double[3];
        if (components[max] > components[min])
        {
            result[mid] = (components[mid] - components[min]) * saturation / (components[max] - components[min]);
            result[max] = saturation;
        }

        return (result[0], result[1], result[2]);
    }
}
