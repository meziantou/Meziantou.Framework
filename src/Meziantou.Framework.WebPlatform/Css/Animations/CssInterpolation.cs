using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The element an interpolation is computed for: the values that are not in the computed values being combined, e.g. the
/// color that currentcolor stands for.
/// </summary>
internal sealed class CssInterpolationContext
{
    /// <summary>A context without element: currentcolor cannot be resolved.</summary>
    public static CssInterpolationContext Empty { get; } = new(style: null);

    public CssInterpolationContext(ComputedStyle? style)
    {
        Style = style;
    }

    /// <summary>The computed style of the animated element.</summary>
    public ComputedStyle? Style { get; }

    /// <summary>
    /// Returns the color that currentcolor stands for in the value of a property: the (animated) color of the element, or the
    /// color of its parent for the color property. Returns null when it is not known.
    /// </summary>
    public string? GetCurrentColor(string property)
    {
        var style = property == "color" ? Style?.Parent : Style;
        if (style is null)
            return property == "color" && Style is not null ? "canvastext" : null;

        // The color is computed with the clamped channels (it is cached by the style)
        using var scope = new CssColor.OutOfRangeRgbScope(enable: false);
        return style.GetComputedValue("color");
    }
}

/// <summary>
/// Two computed values of a property, prepared to be interpolated (https://drafts.csswg.org/css-values-4/#interpolation).
/// When they cannot be interpolated, the interpolation is discrete: the result is the first value when the progress is less
/// than 0.5, and the second value otherwise.
/// </summary>
internal sealed class CssInterpolationPair
{
    private readonly Func<double, string?>? _interpolate;

    internal CssInterpolationPair(string from, string to, Func<double, string?>? interpolate, bool? isDiscrete = null)
    {
        From = from;
        To = to;
        _interpolate = interpolate;
        IsDiscrete = isDiscrete ?? interpolate is null;
    }

    public string From { get; }

    public string To { get; }

    /// <summary>
    /// Whether the values are interpolated discretely (https://drafts.csswg.org/web-animations-1/#discrete): they are not
    /// transitionable, unless the transition allows discrete animations. The interpolation of display and content-visibility
    /// is discrete, but none and hidden are only used at the ends of the interval.
    /// </summary>
    public bool IsDiscrete { get; }

    /// <summary>Returns the value at the progress (which may be outside of [0, 1]).</summary>
    public string Interpolate(double progress)
    {
        // The values out of range (e.g. the channels of the accumulated colors) are clamped by the computation of the result
        using var scope = CssColor.KeepOutOfRangeRgb();
        if (_interpolate is not null && _interpolate(progress) is { } result)
            return result;

        return progress < 0.5 ? From : To;
    }
}

/// <summary>
/// Interpolates, adds and accumulates the computed values of the properties (https://drafts.csswg.org/css-values-4/#combining-values,
/// https://drafts.csswg.org/web-animations-1/#animation-types). The values are the computed values (strings); the results
/// are values that compute to themselves, so they can be computed again (e.g. a calc() is simplified).
/// </summary>
internal static class CssInterpolation
{
    /// <summary>The animatable longhands (https://drafts.csswg.org/web-animations-1/#animatable), used by transition-property: all.</summary>
    public static IReadOnlyList<string> AnimatableLonghands { get; } = [.. CssPropertyRegistry.Properties
        .Where(property => property.AliasOf is null && !property.IsShorthand && CssAnimationTypes.Get(property.Name) != CssInterpolationKind.NotAnimatable)
        .Select(property => property.Name)
        .Order(StringComparer.Ordinal)];

    /// <summary>Returns the interpolation kind of a longhand or a custom property.</summary>
    public static CssInterpolationKind GetKind(string property, ComputedStyle? style)
    {
        if (CssPropertyRegistry.IsCustomProperty(property))
            return CssCustomPropertyInterpolation.GetKind(property, style);

        return CssAnimationTypes.Get(property);
    }

    /// <summary>Returns whether the property is animatable (https://drafts.csswg.org/web-animations-1/#animatable).</summary>
    public static bool IsAnimatable(string property, ComputedStyle? style = null) => GetKind(property, style) != CssInterpolationKind.NotAnimatable;

    /// <summary>Prepares the interpolation of two computed values of a property.</summary>
    public static CssInterpolationPair Prepare(CssInterpolationContext context, string property, string from, string to)
    {
        using var scope = CssColor.KeepOutOfRangeRgb();
        var kind = GetKind(property, context.Style);
        if (kind is CssInterpolationKind.NotAnimatable or CssInterpolationKind.Discrete)
            return new CssInterpolationPair(from, to, interpolate: null);

        if (string.Equals(from, to, StringComparison.Ordinal) && !ContainsCurrentColor(from))
            return new CssInterpolationPair(from, to, _ => from, isDiscrete: kind is CssInterpolationKind.Display or CssInterpolationKind.ContentVisibility);

        var interpolate = kind switch
        {
            CssInterpolationKind.Visibility => CssKeywordInterpolation.PrepareVisibility(from, to),
            CssInterpolationKind.Display => CssKeywordInterpolation.PrepareDisplay(from, to, "none"),
            CssInterpolationKind.ContentVisibility => CssKeywordInterpolation.PrepareDisplay(from, to, "hidden"),
            CssInterpolationKind.Path => CssPathInterpolation.Prepare(from, to),
            CssInterpolationKind.TransformList => CssTransformInterpolation.Prepare(context, from, to),
            CssInterpolationKind.Translate or CssInterpolationKind.Rotate or CssInterpolationKind.Scale => CssIndividualTransformInterpolation.Prepare(kind, from, to),
            CssInterpolationKind.Image => progress => progress < 0.5 ? from : to,
            CssInterpolationKind.FontPalette => CssFontPaletteInterpolation.Prepare(from, to),
            CssInterpolationKind.ShadowList => CssShadowInterpolation.Prepare(context, property, from, to),
            CssInterpolationKind.FilterList => CssFilterInterpolation.Prepare(context, property, from, to),
            CssInterpolationKind.BasicShape when IsPath(from) && IsPath(to) => CssPathInterpolation.Prepare(from, to),
            CssInterpolationKind.BasicShape when UsesRadialExtent(from) || UsesRadialExtent(to) => null,
            _ => PrepareValues(context, property, kind, from, to),
        };

        return new CssInterpolationPair(from, to, interpolate, isDiscrete: kind is CssInterpolationKind.Display or CssInterpolationKind.ContentVisibility || interpolate is null);
    }

    /// <summary>
    /// Adds a value to an underlying value (https://drafts.csswg.org/css-values-4/#combining-values, composite: add). When the
    /// values cannot be added, the result is the value (https://drafts.csswg.org/web-animations-1/#discrete).
    /// </summary>
    public static string Add(CssInterpolationContext context, string property, string underlying, string value)
        => Combine(context, property, underlying, value, CssCombineOperation.Add);

    /// <summary>Accumulates a value onto an underlying value (https://drafts.csswg.org/css-values-4/#combining-values, composite: accumulate).</summary>
    public static string Accumulate(CssInterpolationContext context, string property, string underlying, string value)
        => Combine(context, property, underlying, value, CssCombineOperation.Accumulate);

    /// <summary>
    /// Returns the value of an iteration with the iteration composite operation accumulate
    /// (https://drafts.csswg.org/web-animations-1/#effect-accumulation-section): the final value of the keyframes accumulated
    /// <paramref name="iterations"/> times onto the value.
    /// </summary>
    public static string AccumulateIterations(CssInterpolationContext context, string property, string finalValue, double iterations, string value)
    {
        if (iterations <= 0 || GetKind(property, context.Style) is CssInterpolationKind.NotAnimatable or CssInterpolationKind.Discrete
            or CssInterpolationKind.Visibility or CssInterpolationKind.Display or CssInterpolationKind.ContentVisibility)
            return value;

        // Binary exponentiation of the accumulation of the final value (the accumulation is associative)
        var count = (long)Math.Min(Math.Floor(iterations), long.MaxValue / 2);
        var result = value;
        var power = finalValue;
        while (count > 0)
        {
            if ((count & 1) == 1)
            {
                result = Accumulate(context, property, result, power);
            }

            count >>= 1;
            if (count > 0)
            {
                power = Accumulate(context, property, power, power);
            }
        }

        return result;
    }

    private static string Combine(CssInterpolationContext context, string property, string underlying, string value, CssCombineOperation operation)
    {
        // https://svgwg.org/svg2-draft/painting.html#StrokeDashing: stroke-dasharray is not additive
        if (property == "stroke-dasharray")
            return value;

        using var scope = CssColor.KeepOutOfRangeRgb();
        var kind = GetKind(property, context.Style);
        var result = kind switch
        {
            CssInterpolationKind.NotAnimatable or CssInterpolationKind.Discrete or CssInterpolationKind.Visibility
                or CssInterpolationKind.Display or CssInterpolationKind.ContentVisibility => null,
            CssInterpolationKind.Path => CssPathInterpolation.Combine(underlying, value),
            CssInterpolationKind.TransformList => operation == CssCombineOperation.Add ? CssTransformInterpolation.Add(underlying, value) : CssTransformInterpolation.Accumulate(context, underlying, value),
            CssInterpolationKind.Translate or CssInterpolationKind.Rotate or CssInterpolationKind.Scale => CssIndividualTransformInterpolation.Combine(kind, underlying, value, operation, 1),
            CssInterpolationKind.Image or CssInterpolationKind.FontPalette => null,
            CssInterpolationKind.ShadowList => CssShadowInterpolation.Combine(context, property, underlying, value, operation),
            CssInterpolationKind.FilterList => CssFilterInterpolation.Combine(context, property, underlying, value, operation),
            CssInterpolationKind.BasicShape when IsPath(underlying) && IsPath(value) => CssPathInterpolation.Combine(underlying, value),
            CssInterpolationKind.BasicShape when UsesRadialExtent(underlying) || UsesRadialExtent(value) => null,
            _ => CombineValues(context, property, kind, underlying, value, operation),
        };

        return result ?? value;
    }

    private static Func<double, string?>? PrepareValues(CssInterpolationContext context, string property, CssInterpolationKind kind, string from, string to)
    {
        if (CssPropertyRegistry.IsCustomProperty(property))
            return CssCustomPropertyInterpolation.Prepare(context, property, from, to);

        if (kind == CssInterpolationKind.AspectRatio)
            return CssAspectRatioInterpolation.Prepare(from, to);

        if (kind == CssInterpolationKind.CornerShape)
        {
            // https://drafts.csswg.org/css-borders-4/#corner-shape-interpolation: "interpolating between them linearly would make
            // the transition from a bevel to a straight corner shape appear to start very quickly and slow down toward the end":
            // the interpolation value of a superellipse parameter is its normalized superellipse half corner (from 0 to 1)
            if (!Layout.CornerShapes.TryParse(from, out var fromParameter) || !Layout.CornerShapes.TryParse(to, out var toParameter))
                return null;

            var fromValue = Layout.CornerShapes.GetHalfCorner(fromParameter);
            var toValue = Layout.CornerShapes.GetHalfCorner(toParameter);
            return progress => Layout.CornerShapes.Format(Layout.CornerShapes.FromHalfCorner(fromValue + ((toValue - fromValue) * progress)));
        }

        // https://drafts.csswg.org/css-values-5/#interpolate-calc-size: calc-size() and the sizing keywords (interpolate-size)
        if (CssCalcSizeInterpolation.IsSizingProperty(property))
        {
            var calcSize = CssCalcSizeInterpolation.Prepare(context, from, to, out var handled);
            if (handled)
                return calcSize;
        }

        if (property == "text-decoration-inset")
        {
            (from, to) = ResolveAutoDecorationInset(context, from, to);
        }

        var traits = CssNumericTraits.Get(property);
        var a = CssGenericInterpolation.Parse(CssValueNormalizer.Normalize(property, from));
        var b = CssGenericInterpolation.Parse(CssValueNormalizer.Normalize(property, to));
        Func<List<CssComponentValue>, List<CssComponentValue>, double, string?> combine = kind switch
        {
            CssInterpolationKind.RepeatableList => (x, y, p) => CssGenericInterpolation.CombineRepeatableLists(context, property, traits, x, y, CssCombineOperation.Interpolate, p),
            CssInterpolationKind.TrackList => (x, y, p) => CssGenericInterpolation.CombineItems(context, property, traits, x, y, CssCombineOperation.Interpolate, p),
            _ => (x, y, p) => CssGenericInterpolation.CombineLists(context, property, traits, x, y, CssCombineOperation.Interpolate, p),
        };

        // The values are interpolable when they can be combined (the structure does not depend on the progress)
        if (combine(a, b, 0.5) is null)
            return null;

        return progress => combine(a, b, progress) is { } result ? CssValueNormalizer.Shorten(property, result) : null;
    }

    private static string? CombineValues(CssInterpolationContext context, string property, CssInterpolationKind kind, string underlying, string value, CssCombineOperation operation)
    {
        if (CssPropertyRegistry.IsCustomProperty(property))
            return CssCustomPropertyInterpolation.Combine(context, property, underlying, value, operation);

        // The ratios are not additive (https://drafts.csswg.org/css-values-4/#combine-ratio), nor the shapes of the corners
        if (kind is CssInterpolationKind.AspectRatio or CssInterpolationKind.CornerShape)
            return null;

        if (CssCalcSizeInterpolation.IsSizingProperty(property) && CssCalcSizeInterpolation.Combine(context, underlying, value) is { } calcSize)
            return calcSize;

        if (property == "text-decoration-inset")
        {
            (underlying, value) = ResolveAutoDecorationInset(context, underlying, value);
        }

        var traits = CssNumericTraits.Get(property);
        var a = CssGenericInterpolation.Parse(CssValueNormalizer.Normalize(property, underlying));
        var b = CssGenericInterpolation.Parse(CssValueNormalizer.Normalize(property, value));
        var result = kind switch
        {
            CssInterpolationKind.RepeatableList => CssGenericInterpolation.CombineRepeatableLists(context, property, traits, a, b, operation, 1),
            CssInterpolationKind.TrackList => CssGenericInterpolation.CombineItems(context, property, traits, a, b, operation, 1),
            _ => CssGenericInterpolation.CombineLists(context, property, traits, a, b, operation, 1),
        };

        return result is null ? null : CssValueNormalizer.Shorten(property, result);
    }

    /// <summary>
    /// Replaces the <c>auto</c> of a <c>text-decoration-inset</c> that is combined with lengths by the length it stands for
    /// (https://github.com/w3c/csswg-drafts/issues/13036: auto animates as a length when it is interpolated with a length;
    /// auto combined with auto stays auto): the inset of the painting (<see cref="Layout.Painter.GetAutoDecorationInset"/>) for
    /// the thickness of the decorations of the style of the element, without the metrics of the font.
    /// </summary>
    private static (string First, string Second) ResolveAutoDecorationInset(CssInterpolationContext context, string first, string second)
    {
        if ((first == "auto") == (second == "auto"))
            return (first, second);

        var inset = 1d;
        if (context.Style is { } style)
        {
            var fontSize = style.FontSize;
            var thickness = Layout.LengthPercentage.TryParse(style.GetComputedValue("text-decoration-thickness"), zoom: 1, nonNegative: true, out var parsed) ? parsed.Resolve(fontSize) : fontSize / 10;
            inset = Layout.Painter.GetAutoDecorationInset(thickness, zoom: 1);
        }

        var length = CssGenericInterpolation.FormatNumber(inset) + "px";
        return (first == "auto" ? length : first, second == "auto" ? length : second);
    }

    private static bool IsPath(string value) => value.StartsWith("path(", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a computed basic shape is a circle() or an ellipse() with a radius that is a keyword (the omitted radius is
    /// closest-side): https://drafts.csswg.org/css-shapes/#basic-shape-interpolation, "If both shapes are of type ellipse() or
    /// circle(), and neither uses the closest-side or farthest-side keywords, interpolate between each value": they are not
    /// interpolated nor added otherwise.
    /// </summary>
    private static bool UsesRadialExtent(string value)
    {
        var isCircle = value.StartsWith("circle(", StringComparison.Ordinal);
        if (!isCircle && !value.StartsWith("ellipse(", StringComparison.Ordinal))
            return false;

        if (CssGenericInterpolation.Parse(value) is not [Syntax.CssFunction function, ..])
            return false;

        var radii = 0;
        foreach (var argument in ValueMatcher.RemoveWhitespace(function.Value))
        {
            if (argument is Syntax.CssPreservedToken { Type: Syntax.CssTokenType.Ident } ident)
            {
                // (the position follows `at`)
                if (ident.Token.IsIdent("at"))
                    break;

                return true;
            }

            radii++;
        }

        return radii < (isCircle ? 1 : 2);
    }

    private static bool ContainsCurrentColor(string value) => value.Contains("currentcolor", StringComparison.OrdinalIgnoreCase);
}

/// <summary>How two values are combined (https://drafts.csswg.org/css-values-4/#combining-values).</summary>
internal enum CssCombineOperation
{
    /// <summary>https://drafts.csswg.org/css-values-4/#interpolation</summary>
    Interpolate,

    /// <summary>https://drafts.csswg.org/css-values-4/#addition</summary>
    Add,

    /// <summary>https://drafts.csswg.org/css-values-4/#accumulation</summary>
    Accumulate,
}
