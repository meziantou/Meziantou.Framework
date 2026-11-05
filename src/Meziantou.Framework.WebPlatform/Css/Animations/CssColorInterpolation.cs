using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// Combines two colors (https://drafts.csswg.org/css-color-4/#interpolation): the legacy sRGB colors are interpolated in
/// sRGB, the other colors in Oklab, with premultiplied alpha (https://drafts.csswg.org/css-color-4/#interpolation-space).
/// </summary>
internal static class CssColorInterpolation
{
    public static string? Combine(CssInterpolationContext context, string property, CssComponentValue a, CssComponentValue b, CssCombineOperation operation, double progress)
    {
        var currentColorA = IsCurrentColor(a);
        var currentColorB = IsCurrentColor(b);
        if (currentColorA && currentColorB)
            return "currentcolor";

        if (Resolve(context, property, a, currentColorA) is not { } colorA || Resolve(context, property, b, currentColorB) is not { } colorB)
            return null;

        return Combine(colorA, colorB, operation, progress);
    }

    public static string? Combine(CssColor a, CssColor b, CssCombineOperation operation, double progress)
    {
        if (a.LegacyRgb && b.LegacyRgb)
            return CombineLegacy(a, b, operation, progress);

        if (CssColorConversion.FromColor(a) is not { } componentsA || CssColorConversion.FromColor(b) is not { } componentsB)
            return null;

        var oklabA = CssColorConversion.Convert(componentsA, "oklab");
        var oklabB = CssColorConversion.Convert(componentsB, "oklab");
        CssColorComponents result;
        if (operation == CssCombineOperation.Interpolate)
        {
            result = CssColorMix.Interpolate(oklabA, oklabB, progress, CssColorMix.HueMethod.Shorter);
        }
        else
        {
            // The premultiplied components are added
            var alphaA = oklabA.Alpha ?? 1;
            var alphaB = oklabB.Alpha ?? 1;
            var alpha = Math.Min(1, alphaA + alphaB);
            var channels = new double?[3];
            for (var i = 0; i < 3; i++)
            {
                var sum = ((oklabA.Channels[i] ?? 0) * alphaA) + ((oklabB.Channels[i] ?? 0) * alphaB);
                channels[i] = alpha == 0 ? 0 : sum / alpha;
            }

            result = new CssColorComponents("oklab", channels, alpha);
        }

        var lightness = result.Channels[0] is { } l ? Math.Clamp(l, 0, 1) : (double?)null;
        var resultAlpha = result.Alpha is { } x ? Math.Clamp(x, 0, 1) : (double?)null;
        return "oklab(" + Format(lightness) + " " + Format(result.Channels[1]) + " " + Format(result.Channels[2]) + (resultAlpha is 1 ? "" : " / " + Format(resultAlpha)) + ")";
    }

    private static string CombineLegacy(CssColor a, CssColor b, CssCombineOperation operation, double progress)
    {
        // As in the browsers, the alpha of the legacy colors is an 8-bit value: the alpha of the operands is quantized, and the
        // result is serialized as the shortest decimal that round-trips to its 8-bit value
        // (https://drafts.csswg.org/css-color-4/#serializing-alpha-values, e.g. 0.596 for 152 / 255)
        var alphaA = QuantizeAlpha(a.Alpha ?? 0);
        var alphaB = QuantizeAlpha(b.Alpha ?? 0);
        var alpha = operation == CssCombineOperation.Interpolate ? CssGenericInterpolation.Lerp(alphaA, alphaB, progress) : alphaA + alphaB;
        alpha = Math.Clamp(alpha, 0, 1);
        Span<double> channels = stackalloc double[3];
        for (var i = 0; i < 3; i++)
        {
            var premultipliedA = (a.Channels[i] ?? 0) * alphaA;
            var premultipliedB = (b.Channels[i] ?? 0) * alphaB;
            var value = operation == CssCombineOperation.Interpolate ? CssGenericInterpolation.Lerp(premultipliedA, premultipliedB, progress) : premultipliedA + premultipliedB;
            // The channels of an addition may be out of range: they are clamped once interpolated (by the computation of the value)
            channels[i] = alpha == 0 ? 0 : value / alpha;
        }

        return "rgba(" + CssGenericInterpolation.FormatNumber(channels[0]) + ", " + CssGenericInterpolation.FormatNumber(channels[1]) + ", " + CssGenericInterpolation.FormatNumber(channels[2]) + ", " + CssGenericInterpolation.FormatNumber(SerializableAlpha(alpha)) + ")";
    }

    private static double QuantizeAlpha(double alpha) => Math.Round(Math.Clamp(alpha, 0, 1) * 255, MidpointRounding.AwayFromZero) / 255;

    /// <summary>Returns the shortest decimal (2 or 3 digits) that round-trips to the 8-bit value of an alpha.</summary>
    private static double SerializableAlpha(double alpha)
    {
        var value = (int)Math.Round(alpha * 255, MidpointRounding.AwayFromZero);
        var rounded = Math.Round(value / 255.0, 2, MidpointRounding.AwayFromZero);
        return (int)Math.Round(rounded * 255, MidpointRounding.AwayFromZero) == value ? rounded : Math.Round(value / 255.0, 3, MidpointRounding.AwayFromZero);
    }

    private static CssColor? Resolve(CssInterpolationContext context, string property, CssComponentValue value, bool isCurrentColor)
    {
        if (!isCurrentColor)
        {
            if (CssColor.Parse(value) is { } color)
                return color;

            // A color that depends on currentcolor (color-mix(), a relative color) is computed with the color that currentcolor
            // stands for (https://drafts.csswg.org/css-color-5/#color-mix-result)
            if (value is not CssFunction || context.Style is not { } style || context.GetCurrentColor(property) is not { } currentColorValue)
                return null;

            var serialized = CssSerializer.SerializeValue([value], property: null);
            if (!serialized.Contains("currentcolor", StringComparison.Ordinal))
                return null;

            return CssGenericInterpolation.Parse(CssValueComputer.ResolveCurrentColor(style, property, serialized, currentColorValue)) is [var resolved] ? CssColor.Parse(resolved) : null;
        }

        if (context.GetCurrentColor(property) is not { } currentColor)
            return null;

        return CssGenericInterpolation.Parse(currentColor) is [var component] ? CssColor.Parse(component) : null;
    }

    private static bool IsCurrentColor(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "currentcolor");

    private static string Format(double? value) => value is { } v ? CssGenericInterpolation.FormatNumber(v) : "none";
}
