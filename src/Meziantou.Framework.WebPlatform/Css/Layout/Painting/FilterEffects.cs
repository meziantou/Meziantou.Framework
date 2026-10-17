using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The filters of <c>filter</c> (https://drafts.fxtf.org/filter-effects/#FilterProperty) and <c>backdrop-filter</c>
/// (https://drafts.fxtf.org/filter-effects-2/#BackdropFilterProperty) as filters of the display list: <c>blur()</c> and
/// <c>drop-shadow()</c>, the color matrices of the other functions (https://drafts.fxtf.org/filter-effects/#ShorthandEquivalents),
/// and the graphs of the SVG filter elements referenced by <c>url()</c> (<see cref="SvgFilterBuilder"/>).
/// </summary>
internal static class FilterEffects
{
    /// <summary>
    /// Returns the filters of the computed value of a filter property of a style, or null when it is none or when it references
    /// something that is not a filter element ("the whole filter chain is ignored"). <paramref name="space"/> is the resource
    /// space of the element for the SVG filters.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <param name="space">The resource space of the element.</param>
    /// <param name="property">The filter property.</param>
    /// <param name="renderElement">Paints an element referenced by a feImage primitive in the coordinates of the painting given by a transform of its user space, or null.</param>
    public static DisplayFilter[]? Get(ComputedStyle style, SvgResourceSpace space, string property = "filter", Func<Element, Svg.SvgAffine, DisplayList?>? renderElement = null)
    {
        var value = style.GetComputedValue(property);
        if (value is "none" or "")
            return null;

        return Get(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)), style, space, renderElement);
    }

    /// <summary>
    /// Returns the filters of a &lt;filter-value-list&gt; (e.g. the filters of the <c>filter()</c> image function,
    /// https://drafts.fxtf.org/filter-effects/#FilterCSSImageValue), or null when it is invalid or references something that is not
    /// a filter element. Without <paramref name="space"/>, the references to SVG filter elements are not supported.
    /// </summary>
    public static DisplayFilter[]? Get(List<CssComponentValue> components, ComputedStyle style, SvgResourceSpace? space, Func<Element, Svg.SvgAffine, DisplayList?>? renderElement = null)
    {
        var zoom = style.Layout.EffectiveZoom;
        var filters = new List<DisplayFilter>();
        var tainted = false;
        foreach (var component in components)
        {
            if (component is CssPreservedToken { Type: CssTokenType.Url } || (component is CssFunction urlFunction && Infra.InfraAscii.ToAsciiLowercase(urlFunction.Name) is "url" or "src"))
            {
                // https://drafts.fxtf.org/filter-effects/#funcdef-filter-url
                if (space is not { } resourceSpace || style.Element is not { } element || GetReferencedFilter(element, component) is not { } filterElement)
                    return null;

                filters.Add(SvgFilterBuilder.Build(filterElement, resourceSpace, tainted, renderElement));
                continue;
            }

            if (component is not CssFunction function || GetFilter(function, zoom, style) is not { } filter)
                return null;

            // https://drafts.fxtf.org/filter-effects/#tainted-filter-primitives: a drop shadow of currentcolor
            tainted |= filter is DropShadowFilter && UsesCurrentColor(function);
            filters.Add(filter);
        }

        return filters.Count > 0 ? [.. filters] : null;
    }

    /// <summary>Whether the color of a <c>drop-shadow()</c> function depends on currentcolor (also in a color function such as <c>color-mix()</c>).</summary>
    private static bool UsesCurrentColor(CssFunction function)
    {
        var hasColor = false;
        foreach (var argument in ValueMatcher.RemoveWhitespace(function.Value))
        {
            if (argument is CssPreservedToken { Type: CssTokenType.Dimension or CssTokenType.Number })
                continue;

            hasColor = true;
            if (ContainsCurrentColor(argument))
                return true;
        }

        // The default color is currentcolor
        return !hasColor;

        static bool ContainsCurrentColor(CssComponentValue value) => value switch
        {
            CssPreservedToken { Type: CssTokenType.Ident } ident => Infra.InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "currentcolor"),
            CssFunction nested => nested.Value.Exists(ContainsCurrentColor),
            CssSimpleBlock block => block.Value.Exists(ContainsCurrentColor),
            _ => false,
        };
    }

    /// <summary>Whether the computed value of <c>filter</c> of a style is not none.</summary>
    public static bool HasFilter(ComputedStyle style, string property = "filter") => style.GetComputedValue(property) is not ("none" or "");

    /// <summary>Returns the filter element referenced by a url() of a document (a reference to another document is not supported).</summary>
    private static SvgFilterElement? GetReferencedFilter(Element element, CssComponentValue url)
        => SvgResources.GetReferencedElement(element, url) is SvgFilterElement filter ? filter : null;

    private static DisplayFilter? GetFilter(CssFunction function, double zoom, ComputedStyle style)
    {
        var arguments = ValueMatcher.RemoveWhitespace(function.Value);
        switch (Infra.InfraAscii.ToAsciiLowercase(function.Name))
        {
            case "blur":
            {
                var deviation = arguments is [CssPreservedToken { Type: CssTokenType.Dimension } length] ? ToPixels(length.Token, zoom) : 0;
                return new BlurFilter(deviation, deviation);
            }

            case "drop-shadow":
            {
                // https://drafts.fxtf.org/filter-effects/#funcdef-filter-drop-shadow: [<color>? && <length>{2,3}], the standard
                // deviation is half the blur radius
                var lengths = new List<double>(3);
                var color = DisplayColor.Parse(style.GetUsedValue("color")) ?? DisplayColor.Black;
                foreach (var argument in arguments)
                {
                    if (argument is CssPreservedToken { Type: CssTokenType.Dimension or CssTokenType.Number } token)
                    {
                        lengths.Add(ToPixels(token.Token, zoom));
                    }
                    else if (Painter.ResolveColor(style, argument) is { } parsed)
                    {
                        color = parsed;
                    }
                }

                if (lengths.Count < 2)
                    return null;

                return new DropShadowFilter(new PhysicalOffset(lengths[0], lengths[1]), lengths.Count > 2 ? lengths[2] / 2 : 0, color);
            }

            case "hue-rotate":
            {
                var angle = arguments is [CssPreservedToken angleToken] ? ToDegrees(angleToken.Token) : 0;
                return new ColorMatrixFilter(HueRotate(angle));
            }

            default:
            {
                var amount = arguments is [CssPreservedToken amountToken] ? ToAmount(amountToken.Token) : 1;
                return GetAmountFilter(Infra.InfraAscii.ToAsciiLowercase(function.Name), amount);
            }
        }
    }

    /// <summary>The color matrices of the functions that take an amount (https://drafts.fxtf.org/filter-effects/#ShorthandEquivalents).</summary>
    private static ColorMatrixFilter? GetAmountFilter(string name, double amount)
    {
        var a = (float)amount;
        switch (name)
        {
            case "grayscale":
            {
                var s = 1 - Math.Clamp(a, 0, 1);
                return new ColorMatrixFilter(
                [
                    0.2126f + (0.7874f * s), 0.7152f - (0.7152f * s), 0.0722f - (0.0722f * s), 0, 0,
                    0.2126f - (0.2126f * s), 0.7152f + (0.2848f * s), 0.0722f - (0.0722f * s), 0, 0,
                    0.2126f - (0.2126f * s), 0.7152f - (0.7152f * s), 0.0722f + (0.9278f * s), 0, 0,
                    0, 0, 0, 1, 0,
                ]);
            }

            case "sepia":
            {
                var s = 1 - Math.Clamp(a, 0, 1);
                return new ColorMatrixFilter(
                [
                    0.393f + (0.607f * s), 0.769f - (0.769f * s), 0.189f - (0.189f * s), 0, 0,
                    0.349f - (0.349f * s), 0.686f + (0.314f * s), 0.168f - (0.168f * s), 0, 0,
                    0.272f - (0.272f * s), 0.534f - (0.534f * s), 0.131f + (0.869f * s), 0, 0,
                    0, 0, 0, 1, 0,
                ]);
            }

            case "saturate":
                return new ColorMatrixFilter(
                [
                    0.213f + (0.787f * a), 0.715f - (0.715f * a), 0.072f - (0.072f * a), 0, 0,
                    0.213f - (0.213f * a), 0.715f + (0.285f * a), 0.072f - (0.072f * a), 0, 0,
                    0.213f - (0.213f * a), 0.715f - (0.715f * a), 0.072f + (0.928f * a), 0, 0,
                    0, 0, 0, 1, 0,
                ]);

            case "invert":
            {
                var i = Math.Clamp(a, 0, 1);
                return Linear(1 - (2 * i), i, alphaSlope: 1);
            }

            case "opacity":
                return new ColorMatrixFilter([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, Math.Clamp(a, 0, 1), 0]);

            case "brightness":
                return Linear(a, 0, alphaSlope: 1);

            case "contrast":
                return Linear(a, 0.5f - (0.5f * a), alphaSlope: 1);

            default:
                return null;
        }

        static ColorMatrixFilter Linear(float slope, float intercept, float alphaSlope)
            => new([slope, 0, 0, 0, intercept, 0, slope, 0, 0, intercept, 0, 0, slope, 0, intercept, 0, 0, 0, alphaSlope, 0]);
    }

    /// <summary>The color matrix of <c>hue-rotate()</c> (https://drafts.fxtf.org/filter-effects/#feColorMatrixElement, type hueRotate).</summary>
    private static float[] HueRotate(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var cos = (float)Math.Cos(radians);
        var sin = (float)Math.Sin(radians);
        return
        [
            0.213f + (cos * 0.787f) - (sin * 0.213f), 0.715f - (cos * 0.715f) - (sin * 0.715f), 0.072f - (cos * 0.072f) + (sin * 0.928f), 0, 0,
            0.213f - (cos * 0.213f) + (sin * 0.143f), 0.715f + (cos * 0.285f) + (sin * 0.140f), 0.072f - (cos * 0.072f) - (sin * 0.283f), 0, 0,
            0.213f - (cos * 0.213f) - (sin * 0.787f), 0.715f - (cos * 0.715f) + (sin * 0.715f), 0.072f + (cos * 0.928f) + (sin * 0.072f), 0, 0,
            0, 0, 0, 1, 0,
        ];
    }

    private static double ToPixels(CssToken token, double zoom)
        => token.Type == CssTokenType.Dimension && CssCalc.TryConvertToCanonicalUnit(token.NumericValue, token.Value, out var px, out var unit) && unit == "px" ? px * zoom : token.NumericValue * zoom;

    private static double ToDegrees(CssToken token)
        => token.Type == CssTokenType.Dimension && CssCalc.TryConvertToCanonicalUnit(token.NumericValue, token.Value, out var degrees, out var unit) && unit == "deg" ? degrees : token.NumericValue;

    private static double ToAmount(CssToken token) => token.Type == CssTokenType.Percentage ? token.NumericValue / 100 : token.NumericValue;
}
