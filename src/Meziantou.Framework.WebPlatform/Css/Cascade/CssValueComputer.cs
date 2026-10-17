using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// Computes the values of the properties (https://drafts.csswg.org/css-cascade-5/#computed): the lengths are absolute
/// (in px), the angles, times, frequencies and resolutions use their canonical unit, the math functions are simplified,
/// the colors are computed (currentcolor is kept, it is resolved in the resolved value), the URLs are absolute, and some
/// properties have specific rules (font-size, font-weight, line-height, display, border widths...).
/// </summary>
internal static partial class CssValueComputer
{
    /// <summary>The initial font size (medium), in px.</summary>
    public const double MediumFontSize = 16;

    private static readonly ConcurrentDictionary<string, bool> ColorProperties = new(StringComparer.Ordinal);

    /// <summary>The initial values that are defined in prose by the specifications.</summary>
    private static readonly FrozenDictionary<string, string> SupplementalInitialValues = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["font-family"] = "serif",

        // https://drafts.csswg.org/css-tables-3/#propdef-border-spacing: 0px 0px, in its shortest form (see ComputeBorderSpacing)
        ["border-spacing"] = "0px",
        ["-webkit-user-select"] = "auto",
        ["stop-color"] = "black",
        ["stop-opacity"] = "1",
        ["quotes"] = "auto",
        ["font-language-override"] = "normal",
        ["font-palette"] = "normal",
        ["text-size-adjust"] = "auto",
        ["-webkit-text-size-adjust"] = "auto",
        ["glyph-orientation-vertical"] = "auto",
        ["background-tbd"] = "none",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // https://drafts.csswg.org/css-fonts/#absolute-size-mapping (the sizes of the browsers)
    private static readonly FrozenDictionary<string, double> AbsoluteFontSizes = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["xx-small"] = 9,
        ["x-small"] = 10,
        ["small"] = 13,
        ["medium"] = 16,
        ["large"] = 18,
        ["x-large"] = 24,
        ["xx-large"] = 32,
        ["xxx-large"] = 48,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The default font size of the monospace generic family in the browsers (see <see cref="GetAbsoluteFontSize"/>).</summary>
    public const double MonospaceMediumFontSize = 13;

    // The sizes of the keywords for a default font size of 13px (the table of Blink and WebKit), from xx-small to xxx-large
    private static readonly double[] MonospaceAbsoluteFontSizes = [9, 10, 12, 13, 16, 20, 26, 39];
    private static readonly string[] AbsoluteFontSizeKeywords = ["xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large"];

    /// <summary>Returns the size of an absolute size keyword, for the default font size of the monospace generic family or of the other families.</summary>
    public static double GetAbsoluteFontSize(FontSizeOrigin keyword, bool monospace)
    {
        var index = keyword - FontSizeOrigin.XxSmall;
        return monospace ? MonospaceAbsoluteFontSizes[index] : AbsoluteFontSizes[AbsoluteFontSizeKeywords[index]];
    }

    /// <summary>
    /// Returns how a specified font size depends on the default font size (see <see cref="FontSizeOrigin"/>): a size keyword, a
    /// size relative to the font size of the parent (a percentage, a length relative to the font of the element, larger,
    /// smaller, math), or an absolute length.
    /// </summary>
    public static FontSizeOrigin GetFontSizeOrigin(string value)
    {
        if (value.Length == 0)
            return FontSizeOrigin.AbsoluteLength;

        var index = Array.IndexOf(AbsoluteFontSizeKeywords, InfraAscii.ToAsciiLowercase(value));
        if (index >= 0)
            return (FontSizeOrigin)((int)FontSizeOrigin.XxSmall + index);

        if (value.Contains('%', StringComparison.Ordinal) || (!char.IsAsciiDigit(value[0]) && value[0] is not ('.' or '-' or '+') && !value.Contains('(', StringComparison.Ordinal)))
            return FontSizeOrigin.Relative;

        // The units of the dimensions: a font-relative unit makes the size relative
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsAsciiDigit(value[i]))
                continue;

            while (i < value.Length && (char.IsAsciiDigit(value[i]) || value[i] == '.'))
            {
                i++;
            }

            var start = i;
            while (i < value.Length && char.IsAsciiLetter(value[i]))
            {
                i++;
            }

            // The units relative to the font of the root element do not depend on the parent (WPT css/css-fonts/rem-in-monospace.html)
            if (InfraAscii.ToAsciiLowercase(value[start..i]) is "em" or "ex" or "ch" or "cap" or "ic" or "lh")
                return FontSizeOrigin.Relative;
        }

        return FontSizeOrigin.AbsoluteLength;
    }

    // https://drafts.csswg.org/css-fonts/#font-stretch-prop
    private static readonly FrozenDictionary<string, string> FontWidths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ultra-condensed"] = "50%",
        ["extra-condensed"] = "62.5%",
        ["condensed"] = "75%",
        ["semi-condensed"] = "87.5%",
        ["normal"] = "100%",
        ["semi-expanded"] = "112.5%",
        ["expanded"] = "125%",
        ["extra-expanded"] = "150%",
        ["ultra-expanded"] = "200%",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The properties whose value is an &lt;integer&gt;: the math functions are rounded.</summary>
    private static readonly FrozenSet<string> IntegerProperties = FrozenSet.Create(StringComparer.Ordinal, "z-index", "order", "-webkit-box-ordinal-group", "reading-order", "orphans", "widows", "column-count", "-webkit-line-clamp", "hyphenate-limit-lines", "initial-letter-align");

    /// <summary>The properties whose value is an &lt;opacity-value&gt; (a number clamped to [0, 1]; a percentage is converted to a number).</summary>
    private static readonly FrozenSet<string> OpacityProperties = FrozenSet.Create(StringComparer.Ordinal, "opacity", "fill-opacity", "stroke-opacity", "flood-opacity", "stop-opacity", "shape-image-threshold");

    public static string GetInitialValue(CssPropertyDefinition property) => SupplementalInitialValues.GetValueOrDefault(property.Name) ?? CssShorthands.GetInitialValue(property.Name) ?? "";

    /// <summary>Computes the value of a longhand from its specified value (without CSS-wide keywords and substitution functions).</summary>
    public static string Compute(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var name = property.Name;

        // https://drafts.csswg.org/css-anchor-position-1/#anchor-resolution: the anchor functions of a box that is not absolutely
        // positioned are unresolvable: they compute to their fallback value, else the declaration is invalid at computed-value
        // time (the anchor functions of the absolutely positioned boxes are resolved by the layout, see AnchorPositioning)
        if (value.Contains("anchor", StringComparison.Ordinal) && IsAnchorFunctionProperty(name) && style.GetComputedValue("position") is not ("absolute" or "fixed"))
        {
            if (CssAnchorFunction.Substitute(CssParser.ParseListOfComponentValues(value), static _ => null) is not { } substituted)
                return GetInitialValue(property);

            value = CssSerializer.SerializeValue(substituted, property);
        }
        switch (name)
        {
            case "font-size":
                return ComputeFontSize(style, value);
            case "math-depth":
                return ComputeMathDepth(style, property, value, baseUrl);
            case "font-weight":
                return ComputeFontWeight(style, value);
            case "font-width" or "font-stretch":
                return FontWidths.TryGetValue(value, out var width) ? width : ClampPercentage(ComputeGeneric(style, property, value, baseUrl));
            case "font-style":
                return ComputeFontStyle(style, property, value, baseUrl);
            case "font-size-adjust":
                return ComputeFontSizeAdjust(style, property, value, baseUrl);
            case "grid-row-start" or "grid-row-end" or "grid-column-start" or "grid-column-end":
                return ComputeGridLine(style, property, value, baseUrl);
            case "grid-template-rows" or "grid-template-columns":
            {
                // https://drafts.csswg.org/css-grid-3/#masonry-intrinsic-repeat (tentative): as in the browsers, an intrinsic
                // auto repeat computes to none when the element is not a grid lanes container (csswg-drafts#10915)
                var trackList = ClampNegativeLengthPercentages(ComputeGeneric(style, property, value, baseUrl));
                return CssGrid.HasIntrinsicAutoRepeat(trackList) && style.GetComputedValue("display") is not ("grid-lanes" or "inline-grid-lanes" or "inline grid-lanes") ? "none" : trackList;
            }

            // The specified value of grid-lanes-direction keeps the order of the reversals, its computed value is in the canonical order
            // (WPT css/css-grid/grid-lanes/tentative/parsing/grid-lanes-direction-computed.html)
            case "grid-lanes-direction" when value.Contains("track-reverse fill-reverse", StringComparison.Ordinal):
                return value.Replace("track-reverse fill-reverse", "fill-reverse track-reverse", StringComparison.Ordinal);

            case "grid-auto-rows" or "grid-auto-columns":
                return ClampNegativeLengthPercentages(ComputeGeneric(style, property, value, baseUrl));
            case "font-feature-settings" or "font-variation-settings":
                return ComputeFontSettings(style, property, value, baseUrl);
            case "line-height":
                return ComputeLineHeight(style, value);
            case "display":
                return ComputeDisplay(style, value);
            case "text-emphasis-position":
                return CssPropertyRules.CanonicalizeTextEmphasisPosition(value);
            case "text-emphasis-style":
                return CssPropertyRules.ComputeTextEmphasisStyle(value, horizontal: style.GetComputedValue("writing-mode") == "horizontal-tb");
            case "border-image-outset":
                // https://drafts.csswg.org/css-backgrounds-3/#border-image-outset: the math functions are clamped to non-negative values
                return NegativeLength().Replace(ComputeGeneric(style, property, value, baseUrl), "0px");
            case "box-shadow":
                return CssPropertyRules.ComputeBoxShadow(ComputeGeneric(style, property, value, baseUrl));
            case "box-shadow-offset":
                return CssPropertyRules.ComputeBoxShadowOffset(ComputeGeneric(style, property, value, baseUrl));
            case "caret-color":
                return CssPropertyRules.ComputeCaretColor(ComputeGeneric(style, property, value, baseUrl));
            case "corner-top-left-shape" or "corner-top-right-shape" or "corner-bottom-right-shape" or "corner-bottom-left-shape"
                or "corner-start-start-shape" or "corner-start-end-shape" or "corner-end-start-shape" or "corner-end-end-shape":
                return CssPropertyRules.ComputeCornerShape(ComputeGeneric(style, property, value, baseUrl));
            case "filter" or "backdrop-filter":
                return CssPropertyRules.ComputeFilter(ComputeGeneric(style, property, value, baseUrl));
            case "animation-timing-function" or "transition-timing-function":
                return CssPropertyRules.ComputeEasing(ComputeGeneric(style, property, value, baseUrl));
            case "animation-range-start" or "animation-range-end":
                return CssPropertyRules.ComputeAnimationRange(property, ComputeGeneric(style, property, value, baseUrl));
            case "justify-items" when value == "legacy":
                // https://drafts.csswg.org/css-align-3/#valdef-justify-items-legacy: the legacy keyword alone takes the
                // position of an inherited legacy value, and computes to normal otherwise
                return style.Parent?.GetComputedValue("justify-items") is { } inherited && inherited.StartsWith("legacy ", StringComparison.Ordinal) ? inherited : "normal";
            case "scroll-marker-group" when value is "before" or "after":
                // https://drafts.csswg.org/css-overflow-5/#scroll-marker-group-property: the omitted mode is links
                return value + " links";
            case "overflow-clip-margin-top" or "overflow-clip-margin-right" or "overflow-clip-margin-bottom" or "overflow-clip-margin-left":
                return CssPropertyRules.CanonicalizeOverflowClipMargin(ComputeGeneric(style, property, value, baseUrl));
            case "position-area":
                return CssPropertyRules.ComputePositionArea(value);
            case "float":
                return style.GetComputedValue("position") is "absolute" or "fixed" ? "none" : value;
            case "outline-color" when value == "auto":
                // https://drafts.csswg.org/css-ui-4/#outline-color: "When outline-style is auto, outline-color: auto computes to
                // auto and represents the accent color. Otherwise, outline-color: auto computes to currentcolor."
                return style.GetComputedValue("outline-style") == "auto" ? "auto" : "currentcolor";
            case "position" when value is not ("absolute" or "fixed") && IsInTopLayer(style):
                // https://drafts.csswg.org/css-position-4/#top-styling: "If its specified position property is not absolute or
                // fixed, it computes to absolute"
                return "absolute";
            case "border-top-width" or "border-right-width" or "border-bottom-width" or "border-left-width" or "outline-width":
                // https://drafts.csswg.org/css-backgrounds/#border-width: the width is 0 when the style is none or hidden, but
                // this is the used value (see ComputedStyle.GetUsedBorderWidth), not the computed value; the width of outline
                // does not depend on outline-style either (https://drafts.csswg.org/css-ui-4/#outline-width)
                return ComputeLineWidth(style, value);
            case "column-rule-width" or "row-rule-width":
                return ComputeRuleWidths(style, property, value);
            case "background-position-x" or "background-position-y" or "mask-position" or "object-position" or "perspective-origin":
            case "offset-position" or "offset-anchor" when value is not ("normal" or "auto"):
                return ComputePosition(style, property, value, baseUrl);
            case "clip-path" or "shape-outside" or "offset-path":
            {
                // The logical keywords of the positions and of the lines of shape() are the physical ones of the writing mode
                // of the element (the points of the computed value are offsets from the top left corner)
                var generic = ComputeGeneric(style, property, value, baseUrl);
                return CssPropertyRules.ComputeShapeProperty(name, generic.StartsWith("shape(", StringComparison.Ordinal) ? ResolveLogicalShapeKeywords(style, generic) : generic);
            }
            case "border-shape" when value != "none":
                return CssPropertyRules.ComputeBorderShape(ComputeGeneric(style, property, value, baseUrl));
            case "offset-rotate":
                return CssPropertyRules.ComputeOffsetRotate(ComputeGeneric(style, property, value, baseUrl));
            case "background-size" or "mask-size":
                return CssPropertyRules.ComputeBgSize(ComputeGeneric(style, property, value, baseUrl));
            case "transform-origin":
                return ComputeTransformOrigin(style, property, value, baseUrl);
            case "border-spacing":
                return ComputeBorderSpacing(ClampMathFunctionResult(property, value, ComputeGeneric(style, property, value, baseUrl)));
            case "scale":
                return ComputeScale(style, property, value, baseUrl);
            case "translate":
                return ComputeTranslate(style, property, value, baseUrl);
            case "rotate":
                return ComputeRotate(style, property, value, baseUrl);
            case "writing-mode":
                // https://drafts.csswg.org/css-writing-modes-4/#svg-writing-mode
                return value switch
                {
                    "lr" or "lr-tb" or "rl" or "rl-tb" => "horizontal-tb",
                    "tb" or "tb-rl" => "vertical-rl",
                    _ => value,
                };
            case "text-orientation" when value == "sideways-right":
                return "sideways";
            case "text-combine-upright" when value.StartsWith("digits", StringComparison.Ordinal):
                return ComputeTextCombineUprightDigits(style, property, value, baseUrl);
            case "word-spacing" when value == "normal":
                return "0px";
            case "stroke-width" or "stroke-dashoffset":
            {
                // https://drafts.fxtf.org/fill-stroke/#stroke-width, https://svgwg.org/svg2-draft/painting.html#StrokeDashoffsetProperty:
                // the numbers (and the math functions that resolve to a number) are converted to absolute lengths
                var stroke = ClampMathFunctionResult(property, value, ComputeGeneric(style, property, value, baseUrl));
                return TryParseNumber(stroke, out var strokeNumber) ? FormatPixels(strokeNumber) : stroke;
            }

            case "stroke-dasharray" when value != "none":
                // The dashes are non-negative: a negative result of a math function is clamped to zero
                return ClampNegativeLengthPercentages(ComputeStrokeDasharray(ComputeGeneric(style, property, value, baseUrl)));
            case "text-align-all" or "text-align-last":
                return ComputeTextAlign(style, name, value);
            case "text-fit" when value.Contains(" consistent", StringComparison.Ordinal):
                // The default fitting target is omitted (as Blink, which serializes the computed value in its shortest form)
                return ComputeGeneric(style, property, value, baseUrl).Replace(" consistent", "", StringComparison.Ordinal);
            case "content" when value == "normal" && style.PseudoElement is "before" or "after":
                return "none";
            case "color":
                return ComputeColorProperty(style, value);
            case "text-shadow" when value != "none":
                return ComputeTextShadow(style, property, value, baseUrl);
            case "zoom":
                return ComputeZoom(style, value);
            case "contain" when style.PseudoElement == Layout.ScrollControls.MarkerGroup:
                return ComputeScrollMarkerGroupContain(style, value);
            case "contain" when value is "layout style paint" or "size layout style paint":
                // https://drafts.csswg.org/css-contain-2/#contain-property: the computed value uses the shorthand keywords, as in
                // the browsers (https://drafts.csswg.org/cssom/#serializing-css-values: the shortest serialization)
                return value == "layout style paint" ? "content" : "strict";
            case "contain-intrinsic-width" or "contain-intrinsic-height" when !value.StartsWith("auto", StringComparison.Ordinal) && style.GetComputedValue("content-visibility") == "auto":
                // https://github.com/w3c/csswg-drafts/issues/8407: content-visibility: auto makes contain-intrinsic-size gain the
                // auto keyword, so that the element remembers its size (as in the browsers)
                return "auto " + ComputeGeneric(style, property, value, baseUrl);
            case "hyphenate-limit-chars":
                return ComputeHyphenateLimitChars(style, property, value, baseUrl);
            case "dynamic-range-limit" when value.Contains('(', StringComparison.Ordinal):
                return CssPropertyRules.ComputeDynamicRangeLimit(ComputeGeneric(style, property, value, baseUrl));
        }

        if (OpacityProperties.Contains(name))
            return ComputeOpacity(style, value);

        var result = ClampMathFunctionResult(property, value, ComputeGeneric(style, property, value, baseUrl));
        if (IntegerProperties.Contains(name) && TryParseNumber(result, out var number))
            return SerializeNumber(Math.Floor(number + 0.5));

        // https://drafts.csswg.org/css-inline-3/#sizing-drop-initials: <number> <integer>; the sink is rounded to the nearest
        // integer (https://drafts.csswg.org/css-values-4/#calc-type-checking)
        if (name == "initial-letter" && result.IndexOf(' ', StringComparison.Ordinal) is > 0 and var separator && TryParseNumber(result[(separator + 1)..], out var sink))
            return string.Concat(result.AsSpan(0, separator + 1), SerializeNumber(Math.Floor(sink + 0.5)));

        return result;
    }

    /// <summary>
    /// hyphenate-limit-chars (https://drafts.csswg.org/css-text-4/#hyphenate-limit-chars-property): the math functions are rounded to
    /// the nearest integer (https://drafts.csswg.org/css-values-4/#calc-type-checking), then the value has its shortest form.
    /// </summary>
    private static string ComputeHyphenateLimitChars(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var values = new List<string>(3);
        foreach (var part in ComputeGeneric(style, property, value, baseUrl).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            values.Add(TryParseNumber(part, out var number) ? SerializeNumber(Math.Max(0, Math.Floor(number + 0.5))) : part);
        }

        return CssPropertyRules.CanonicalizeHyphenateLimitChars(values);
    }

    /// <summary>
    /// The result of a math function is clamped to the range allowed by the property (https://drafts.csswg.org/css-values-4/#calc-range),
    /// e.g. flex-shrink: calc(-1) computes to 0 and column-count: calc(0) to 1. Only a value that is a single number,
    /// percentage or dimension is clamped.
    /// </summary>
    private static string ClampMathFunctionResult(CssPropertyDefinition property, string value, string computed)
    {
        if (!value.Contains('(', StringComparison.Ordinal) || CssPropertyRegistry.GetGrammar(property) is not { } grammar)
            return computed;

        if (ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is not [CssFunction function] || !CssCalc.IsMathFunction(function.Name))
            return computed;

        if (ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(computed)) is not [CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension } token])
            return computed;

        if (FindRange(grammar, token.Token, depth: 0) is not { } range)
            return computed;

        var clamped = Math.Clamp(token.Token.NumericValue, range.Min ?? double.NegativeInfinity, range.Max ?? double.PositiveInfinity);
        if (clamped == token.Token.NumericValue)
            return computed;

        return SerializeNumber(clamped) + token.Token.Type switch
        {
            CssTokenType.Percentage => "%",
            CssTokenType.Dimension => token.Token.Value,
            _ => "",
        };
    }

    /// <summary>Returns the range of the type of the grammar that accepts a numeric token (e.g. [0,∞] for &lt;length [0,∞]&gt;), or null.</summary>
    private static (double? Min, double? Max)? FindRange(GrammarNode node, CssToken token, int depth)
    {
        if (depth > 16)
            return null;

        switch (node)
        {
            case GroupNode group:
                foreach (var child in group.Children)
                {
                    if (FindRange(child, token, depth + 1) is { } range)
                        return range;
                }

                return null;

            case TypeNode { IsProperty: true } propertyReference:
                return CssPropertyRegistry.Get(propertyReference.Name) is { } referenced && CssPropertyRegistry.GetGrammar(referenced) is { } referencedGrammar ? FindRange(referencedGrammar, token, depth + 1) : null;

            case TypeNode type:
            {
                var accepts = token.Type switch
                {
                    CssTokenType.Number => type.Name is "number" or "integer",
                    CssTokenType.Percentage => type.Name is "percentage" || type.Name.EndsWith("-percentage", StringComparison.Ordinal),
                    _ => GetDimensionType(InfraAscii.ToAsciiLowercase(token.Value)) is { } dimensionType && (type.Name == dimensionType || type.Name == dimensionType + "-percentage"),
                };

                if (accepts)
                    return type.RangeMin is null && type.RangeMax is null ? null : (type.RangeMin, type.RangeMax);

                if (type.Name.EndsWith("()", StringComparison.Ordinal) || CssPropertyRegistry.GetTypeGrammar("<" + type.Name + ">") is not { } typeGrammar)
                    return null;

                return FindRange(typeGrammar, token, depth + 1);
            }

            default:
                return null;
        }
    }

    /// <summary>Returns the type of a canonical unit of the computed values (px, deg, s, hz, dppx, fr).</summary>
    private static string? GetDimensionType(string unit) => unit switch
    {
        "px" => "length",
        "deg" => "angle",
        "s" => "time",
        "hz" => "frequency",
        "dppx" => "resolution",
        "fr" => "flex",
        _ => null,
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Generic computation
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Computes the lengths, angles, times, colors, URLs and math functions of a value.</summary>
    public static string ComputeGeneric(ComputedStyle style, CssPropertyDefinition? property, string value, string? baseUrl, bool? hasColors = null)
    {
        // Most values are a keyword (e.g. block, none, auto): an identifier in lowercase is its own computed value, unless it
        // is a color, or a family name of font-family (serialized as a family name)
        if (property is not null && IsLowercaseIdentifier(value) && !(hasColors ?? HasColorType(property)) && property.Name != "font-family")
            return value;

        var values = CssParser.ParseListOfComponentValues(value);
        // The font-relative lengths of the font properties refer to the font of the parent (https://drafts.csswg.org/css-values-4/#font-relative-lengths)
        var context = new ComputationContext(style, hasColors ?? (property is not null && HasColorType(property)), baseUrl)
        {
            PercentageUnit = property is null ? null : GetPercentageUnit(property),
            FontSizeContext = property is not null && property.Name.StartsWith("font-", StringComparison.Ordinal),
        };
        var transformed = Transform(values, context);
        return ComputeGradients(CssSerializer.SerializeValue(transformed, property));
    }

    /// <summary>Returns whether a longhand accepts the anchor functions (https://drafts.csswg.org/css-anchor-position-1/#anchor-pos, https://drafts.csswg.org/css-anchor-position-1/#anchor-size-fn): the insets, the margins and the sizes.</summary>
    internal static bool IsAnchorFunctionProperty(string name) => name is "top" or "right" or "bottom" or "left" or "margin-top" or "margin-right" or "margin-bottom" or "margin-left"
        or "width" or "height" or "min-width" or "min-height" or "max-width" or "max-height";

    /// <summary>Returns whether the value is a single identifier of ASCII lowercase letters, digits and hyphens, starting with a letter.</summary>
    private static bool IsLowercaseIdentifier(string value)
    {
        if (value.Length == 0 || !char.IsAsciiLetterLower(value[0]))
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-')
                return false;
        }

        return true;
    }

    private sealed class ComputationContext(ComputedStyle style, bool hasColors, string? baseUrl)
    {
        public ComputedStyle Style { get; } = style;

        public bool HasColors { get; } = hasColors;

        public string? BaseUrl { get; } = baseUrl;

        /// <summary>Whether the font-relative units are relative to the parent (font-size) and lh to the parent line height (font-size, line-height).</summary>
        public bool FontSizeContext { get; init; }

        public bool LineHeightContext { get; init; }

        /// <summary>The unit the percentages of the math functions stand for when they are resolved at used time (e.g. "px" for a &lt;length-percentage&gt;), or null.</summary>
        public string? PercentageUnit { get; init; }
    }

    private static List<CssComponentValue> Transform(List<CssComponentValue> values, ComputationContext context)
    {
        var result = new List<CssComponentValue>(values.Count);
        foreach (var value in values)
        {
            result.Add(Transform(value, context));
        }

        return result;
    }

    private static CssComponentValue Transform(CssComponentValue value, ComputationContext context)
    {
        switch (value)
        {
            case CssPreservedToken { Token: var token } preserved:
                switch (token.Type)
                {
                    case CssTokenType.Dimension:
                        if (ConvertDimension(token.NumericValue, InfraAscii.ToAsciiLowercase(token.Value), context) is { } converted)
                            return Dimension(converted.Value, converted.Unit);

                        return preserved;

                    case CssTokenType.Ident or CssTokenType.Hash when context.HasColors:
                        return ComputeColor(value, context.Style, isColorProperty: false) is { } color ? Raw(color) : preserved;

                    case CssTokenType.Url:
                        return Raw(CssSerializer.SerializeUrl(ResolveUrl(token.Value, context)));

                    default:
                        return preserved;
                }

            case CssFunction function:
            {
                var name = InfraAscii.ToAsciiLowercase(function.Name);
                if (CssCalc.IsMathFunction(name))
                {
                    if (CssCalc.Evaluate(function, CreateCalcContext(context, GetPercentageUnit(function) ?? context.PercentageUnit)) is { } result)
                        return Raw(SerializeComputedCalculation(result));
                }
                else if (CssCalc.GetAnchorFunction(name) is not null)
                {
                    // https://drafts.csswg.org/css-anchor-position-1/#anchor-pos: the function is kept (its fallback is computed)
                    return Raw(SerializeAnchorFunction(function, context));
                }
                else if (name is "sibling-index" or "sibling-count" && ValueMatcher.RemoveWhitespace(function.Value).Count == 0)
                {
                    // https://drafts.csswg.org/css-values-5/#tree-counting
                    return Raw(SerializeNumber(ResolveTreeCountingFunction(name, context.Style.Element) ?? 1));
                }
                else if (ValueMatcher.IsTreeCountingFunction(function))
                {
                    // https://drafts.csswg.org/css-values-5/#tree-counting: an integer
                    if (ResolveTreeCountingFunction(name, context.Style.Element) is { } count)
                        return Raw(SerializeNumber(count));
                }
                else if (name == "light-dark" && SplitArguments(function.Value) is [[var light], [var dark]])
                {
                    // https://drafts.csswg.org/css-color-5/#light-dark: computes to the color or image of the used color scheme (also
                    // when the color cannot be resolved yet, e.g. currentcolor in a relative color), and none to image(transparent)
                    var selected = GetUsedColorScheme(context.Style) == "dark" ? dark : light;
                    return selected is CssPreservedToken { Type: CssTokenType.Ident } none && none.Token.IsIdent("none") ? Raw("image(rgba(0, 0, 0, 0))") : Transform(selected, context);
                }
                else if (context.HasColors && IsColorFunction(name))
                {
                    if (ComputeColor(function, context.Style, isColorProperty: false) is { } color)
                        return Raw(color);
                }
                else if (name == "path")
                {
                    // https://svgwg.org/svg2-draft/paths.html#TheDProperty: the path data is converted to absolute commands
                    if (CssPath.Serialize(function, absolute: true) is { } path)
                        return Raw(path);
                }
                else if (name is "url" or "src" && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.String } url])
                {
                    return Raw(CssSerializer.SerializeUrl(ResolveUrl(url.Token.Value, context)));
                }
                else if (name is "url" or "src" && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.String } modifiedUrl, .. var urlModifiers] && CssUrlModifiers.Canonicalize(urlModifiers) is { } modifiers)
                {
                    // https://drafts.csswg.org/css-values-5/#request-url-modifiers
                    return Raw(CssUrlModifiers.Serialize("url", ResolveUrl(modifiedUrl.Token.Value, context), modifiers));
                }
                else if (name is "image-set" or "-webkit-image-set" && ComputeImageSet(function, context) is { } imageSet)
                {
                    return Raw(imageSet);
                }

                else if (name == "cross-fade" && ComputeCrossFade(function, context) is { } crossFade)
                {
                    return Raw(crossFade);
                }
                else if (name == "paint")
                {
                    // https://drafts.css-houdini.org/css-paint-api-1/#paint-notation: the name of the paint is an identifier
                    // (not a color keyword), and the arguments are computed with the grammar of the paint class when the
                    // image is drawn
                    return function;
                }

                var copy = new CssFunction(function.Name);
                copy.Value.AddRange(Transform(function.Value, context));
                return copy;
            }

            case CssSimpleBlock block:
            {
                var copy = new CssSimpleBlock(block.AssociatedToken);
                copy.Value.AddRange(Transform(block.Value, context));
                return copy;
            }

            default:
                return value;
        }
    }

    /// <summary>
    /// Serializes the result of a top-level calculation (https://drafts.csswg.org/css-values-4/#calc-ieee): NaN computes to
    /// zero and the infinite values to the largest finite values (of a single-precision number, as in the browsers).
    /// </summary>
    private static string SerializeComputedCalculation(CssCalcValue result)
    {
        // A zero dimension added to a percentage is not kept in the computed value, as in the browsers (calc(10% + 0px) is 10%: WPT
        // css/css-flexbox/parsing/flex-basis-computed.html)
        if (result.Root is CssCalcSumNode { Children: [CssCalcNumericNode first, CssCalcNumericNode second] }
            && (first.Unit == "%") != (second.Unit == "%") && (first.Unit == "%" ? second : first) is { Value: 0, Unit.Length: > 0 }
            && (first.Unit == "%" ? first : second) is var percentage && double.IsFinite(percentage.Value))
        {
            return CssSerializer.SerializeNumber(percentage.Value) + "%";
        }

        if (!result.IsNumeric || double.IsFinite(result.Value))
            return result.Serialize();

        if (double.IsNaN(result.Value))
            return "0" + (result.Unit == "%" ? result.TypeUnit : result.Unit);

        return SerializeNumber(result.Value > 0 ? float.MaxValue : float.MinValue) + result.Unit;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-images-4/#image-set-notation: each option of image-set() computes to its image (a
    /// &lt;string&gt; is a url()), its resolution in dppx (1dppx when it is omitted) and its type.
    /// </summary>
    private static string? ComputeImageSet(CssFunction function, ComputationContext context)
    {
        var options = new List<string>();
        foreach (var option in SplitArguments(function.Value))
        {
            if (option.Count == 0 || option.Count > 3)
                return null;

            var image = option[0] is CssPreservedToken { Type: CssTokenType.String } url
                ? CssSerializer.SerializeUrl(ResolveUrl(url.Token.Value, context))
                : CssSerializer.SerializeValue([Transform(option[0], context)], property: null);
            string? resolution = null;
            string? type = null;
            foreach (var value in option.Skip(1))
            {
                if (value is CssFunction { Name: var name } && InfraAscii.EqualsIgnoreAsciiCase(name, "type"))
                {
                    type = CssSerializer.SerializeValue([value], property: null);
                }
                else
                {
                    // A resolution is not negative (the result of a calculation is clamped)
                    resolution = CssSerializer.SerializeValue([Transform(value, context)], property: null);
                    if (resolution.StartsWith('-', StringComparison.Ordinal))
                    {
                        resolution = "0" + resolution.TrimStart("-.0123456789e+".ToCharArray());
                    }
                }
            }

            options.Add(image + " " + (resolution ?? "1dppx") + (type is null ? "" : " " + type));
        }

        return "image-set(" + string.Join(", ", options) + ")";
    }

    /// <summary>
    /// https://drafts.csswg.org/css-images-4/#cross-fade-function: each image of cross-fade() computes to its computed image
    /// or color followed by its percentage, which is clamped to [0%, 100%].
    /// </summary>
    private static string? ComputeCrossFade(CssFunction function, ComputationContext context)
    {
        var images = new List<string>();
        foreach (var argument in SplitArguments(function.Value))
        {
            string? image = null;
            string? percentage = null;
            foreach (var value in argument)
            {
                var computed = CssSerializer.SerializeValue([Transform(value, context)], property: null);
                var isPercentage = value is CssPreservedToken { Type: CssTokenType.Percentage } || (value is CssFunction math && CssCalc.IsMathFunction(math.Name));
                if (isPercentage && computed.EndsWith('%', StringComparison.Ordinal) && double.TryParse(computed.AsSpan(0, computed.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    percentage = SerializeNumber(Math.Clamp(number, 0, 100)) + "%";
                }
                else if (image is null)
                {
                    image = computed;
                }
                else
                {
                    return null;
                }
            }

            if (image is null)
                return null;

            images.Add(percentage is null ? image : image + " " + percentage);
        }

        return "cross-fade(" + string.Join(", ", images) + ")";
    }

    /// <summary>A token serialized as is.</summary>
    private static CssPreservedToken Raw(string text) => new(new CssToken { Type = CssTokenType.Delim, Value = text });

    private static CssPreservedToken Dimension(double value, string unit) => new(new CssToken { Type = CssTokenType.Dimension, NumericValue = value, Value = unit });

    private static string ResolveUrl(string url, ComputationContext context)
    {
        // A fragment-only URL is not resolved (https://drafts.csswg.org/css-values-4/#local-urls)
        if (url.Length == 0 || url[0] == '#')
            return url;

        var baseUrl = context.BaseUrl ?? context.Style.Engine.Document.DocumentBaseUrl.Serialize();
        return Url.Parse(url, baseUrl)?.Href ?? url;
    }

    private static readonly ConcurrentDictionary<string, string?> PercentageUnits = new(StringComparer.Ordinal);

    /// <summary>
    /// The unit the percentages of the math functions of a property stand for when they are resolved at used time: "px" when the
    /// property accepts a &lt;length-percentage&gt; (e.g. min(10%, 20%) cannot be simplified, the basis may be negative), or null.
    /// </summary>
    internal static string? GetPercentageUnit(CssPropertyDefinition property) => PercentageUnits.GetOrAdd(property.Name, static (_, property) =>
        CssPropertyRegistry.GetGrammar(property) is { } grammar && ContainsTypeOutsideFunctions(grammar, "length-percentage", new HashSet<string>(StringComparer.Ordinal)) ? "px" : null, property);

    private static bool ContainsTypeOutsideFunctions(GrammarNode node, string typeName, HashSet<string> visited)
    {
        switch (node)
        {
            case GroupNode group:
                foreach (var child in group.Children)
                {
                    if (ContainsTypeOutsideFunctions(child, typeName, visited))
                        return true;
                }

                return false;

            case TypeNode type when !type.Name.EndsWith("()", StringComparison.Ordinal):
            {
                if (!type.IsProperty && type.Name == typeName)
                    return true;

                if (!visited.Add(type.IsProperty ? "'" + type.Name : type.Name))
                    return false;

                var grammar = type.IsProperty
                    ? CssPropertyRegistry.Get(type.Name) is { } property ? CssPropertyRegistry.GetGrammar(property) : null
                    : CssPropertyRegistry.GetTypeGrammar("<" + type.Name + ">");
                return grammar is not null && ContainsTypeOutsideFunctions(grammar, typeName, visited);
            }

            default:
                return false;
        }
    }

    /// <summary>The unit the percentages of a math function stand for (the unit of the other dimensions), used for type checking.</summary>
    private static string? GetPercentageUnit(CssFunction function)
    {
        foreach (var value in function.Value)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                    return GetUnitCategory(InfraAscii.ToAsciiLowercase(dimension.Token.Value)) ?? "px";
                case CssFunction nested when GetPercentageUnit(nested) is { } unit:
                    return unit;
                case CssSimpleBlock block:
                    var inner = new CssFunction("calc");
                    inner.Value.AddRange(block.Value);
                    if (GetPercentageUnit(inner) is { } blockUnit)
                        return blockUnit;

                    break;
            }
        }

        return null;
    }

    private static CssCalcContext CreateCalcContext(ComputationContext context, string? percentageUnit, (double Value, string Unit)? percentageBasis = null) => new()
    {
        ResolveDimension = (value, unit) => ResolveRelativeUnit(value, unit, context),
        PercentageUnit = percentageUnit,
        PercentageBasis = percentageBasis,
        ResolveTreeCountingFunction = name => ResolveTreeCountingFunction(name, context.Style.Element),
        ResolveRandomBaseValue = sharing => context.Style.Engine.GetRandomBaseValue(sharing, context.Style.Element, context.Style.PseudoElement),
        AnchorFunctions = CssAnchorFunctions.Anchor | CssAnchorFunctions.AnchorSize,
        SerializeAnchorFunction = function => SerializeAnchorFunction(function, context),
    };

    /// <summary>
    /// Serializes an anchor function of a computed value (https://drafts.csswg.org/css-anchor-position-1/#anchor-pos): it is kept, with
    /// its fallback and the math function of its &lt;anchor-side&gt; (a &lt;percentage&gt;, e.g. with a tree counting function) computed.
    /// </summary>
    private static string SerializeAnchorFunction(CssFunction function, ComputationContext context)
    {
        var sideContext = new ComputationContext(context.Style, context.HasColors, context.BaseUrl);
        return CssAnchorFunction.Serialize(
            function,
            fallback => CssSerializer.SerializeValue([Transform(fallback, context)], property: null),
            side => CssSerializer.SerializeValue([Transform(side, sideContext)], property: null));
    }

    /// <summary>https://drafts.csswg.org/css-values-5/#tree-counting: the index of the element among its siblings (from 1), or the number of siblings.</summary>
    private static double? ResolveTreeCountingFunction(string name, Element element)
    {
        var index = 1;
        for (var sibling = element.PreviousElementSibling; sibling is not null; sibling = sibling.PreviousElementSibling)
        {
            index++;
        }

        if (name == "sibling-index")
            return index;

        var count = index;
        for (var sibling = element.NextElementSibling; sibling is not null; sibling = sibling.NextElementSibling)
        {
            count++;
        }

        return count;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Units (https://drafts.csswg.org/css-values-4/#lengths)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the canonical unit of the category of a unit (px, deg, s, hz, dppx), or null when the unit is unknown.</summary>
    private static string? GetUnitCategory(string unit) => unit switch
    {
        "deg" or "grad" or "rad" or "turn" => "deg",
        "s" or "ms" => "s",
        "hz" or "khz" => "hz",
        "dppx" or "x" or "dpi" or "dpcm" => "dppx",
        "fr" => "fr",
        _ => IsLengthUnit(unit) ? "px" : null,
    };

    private static bool IsLengthUnit(string unit) => unit is "px" or "cm" or "mm" or "q" or "in" or "pt" or "pc"
        or "em" or "rem" or "ex" or "rex" or "ch" or "rch" or "cap" or "rcap" or "ic" or "ric" or "lh" or "rlh"
        or "vw" or "vh" or "vi" or "vb" or "vmin" or "vmax" or "svw" or "svh" or "svi" or "svb" or "svmin" or "svmax"
        or "lvw" or "lvh" or "lvi" or "lvb" or "lvmin" or "lvmax" or "dvw" or "dvh" or "dvi" or "dvb" or "dvmin" or "dvmax"
        or "cqw" or "cqh" or "cqi" or "cqb" or "cqmin" or "cqmax";

    /// <summary>Converts a dimension to its canonical unit (px, deg, s, hz, dppx); returns null when the unit is unknown.</summary>
    private static (double Value, string Unit)? ConvertDimension(double value, string unit, ComputationContext context)
    {
        return unit switch
        {
            "px" => (value, "px"),
            "cm" => (value * 96 / 2.54, "px"),
            "mm" => (value * 96 / 25.4, "px"),
            "q" => (value * 96 / 101.6, "px"),
            "in" => (value * 96, "px"),
            "pt" => (value * 4 / 3, "px"),
            "pc" => (value * 16, "px"),
            "deg" => (value, "deg"),
            "grad" => (value * 0.9, "deg"),
            "rad" => (value * 180 / Math.PI, "deg"),
            "turn" => (value * 360, "deg"),
            "s" => (value, "s"),
            "ms" => (value / 1000, "s"),
            "hz" => (value, "hz"),
            "khz" => (value * 1000, "hz"),
            "dppx" or "x" => (value, "dppx"),
            "dpi" => (value / 96, "dppx"),
            "dpcm" => (value * 2.54 / 96, "dppx"),
            _ => ResolveRelativeUnit(value, unit, context),
        };
    }

    /// <summary>Resolves a relative length (font-relative, viewport-relative or container-relative) to px.</summary>
    private static (double Value, string Unit)? ResolveRelativeUnit(double value, string unit, ComputationContext context)
    {
        var style = context.Style;
        var environment = style.Environment;

        // The font size is only computed when it is used (e.g. font-size: var(--x) with a registered --x: 2rem does not depend on it)
        double FontSize() => context.FontSizeContext ? style.ParentFontSize : style.FontSize;
        bool IsVertical() => style.GetComputedValue("writing-mode") is not "horizontal-tb";

        // https://drafts.csswg.org/css-values-4/#font-relative-lengths: the font properties use the font metrics of the parent
        // (the initial values of the font properties for the root element)
        double FontUnit(FontRelativeUnit fontUnit)
        {
            var fontStyle = context.FontSizeContext ? style.Parent : style;
            return fontStyle is not null ? fontStyle.GetFontRelativeLength(fontUnit) : ComputedStyle.GetInitialFontRelativeLength(style.Engine, fontUnit);
        }

        double RootFontUnit(FontRelativeUnit fontUnit)
        {
            var root = style.RootStyle ?? (style.IsRoot && !context.FontSizeContext ? style : null);
            return root is not null ? root.GetFontRelativeLength(fontUnit) : ComputedStyle.GetInitialFontRelativeLength(style.Engine, fontUnit);
        }

        // The container query length units of the inline and block axes use the writing mode of the element
        double ContainerUnit(ContainerUnitAxis axis)
        {
            var horizontal = axis switch
            {
                ContainerUnitAxis.Width => true,
                ContainerUnitAxis.Height => false,
                ContainerUnitAxis.Inline => !IsVertical(),
                _ => IsVertical(),
            };

            return ContainerQueries.GetContainerSize(style, horizontal) ?? (horizontal ? ViewportWidth() : ViewportHeight());
        }

        // https://drafts.csswg.org/css-values-4/#viewport-relative-lengths: the viewport without the scrollbar gutters of the
        // root scroller that do not depend on its contents (see StyleEngine.GetViewportUnitSize); in the page context and the
        // page-margin boxes, the default page box of the printing (https://github.com/w3c/csswg-drafts/issues/5437)
        double ViewportWidth() => style.Engine.GetViewportUnitSize(environment, style.PseudoElement).Width;
        double ViewportHeight() => style.Engine.GetViewportUnitSize(environment, style.PseudoElement).Height;

        double? pixels = unit switch
        {
            "em" => value * FontSize(),
            "rem" => value * style.RootFontSize,

            // The metrics of the fonts of the element (of its parent for the font properties), and of the root element
            "ex" => value * FontUnit(FontRelativeUnit.Ex),
            "cap" => value * FontUnit(FontRelativeUnit.Cap),
            "ch" => value * FontUnit(FontRelativeUnit.Ch),
            "ic" => value * FontUnit(FontRelativeUnit.Ic),
            "rex" => value * RootFontUnit(FontRelativeUnit.Ex),
            "rcap" => value * RootFontUnit(FontRelativeUnit.Cap),
            "rch" => value * RootFontUnit(FontRelativeUnit.Ch),
            "ric" => value * RootFontUnit(FontRelativeUnit.Ic),
            "lh" => value * (context.FontSizeContext || context.LineHeightContext ? style.ParentLineHeight : style.LineHeight),
            "rlh" => value * style.RootLineHeight,

            // The viewport units
            "vw" or "svw" or "lvw" or "dvw" => value * ViewportWidth() / 100,
            "vh" or "svh" or "lvh" or "dvh" => value * ViewportHeight() / 100,
            "vi" or "svi" or "lvi" or "dvi" => value * (IsVertical() ? ViewportHeight() : ViewportWidth()) / 100,
            "vb" or "svb" or "lvb" or "dvb" => value * (IsVertical() ? ViewportWidth() : ViewportHeight()) / 100,
            "vmin" or "svmin" or "lvmin" or "dvmin" => value * Math.Min(ViewportWidth(), ViewportHeight()) / 100,
            "vmax" or "svmax" or "lvmax" or "dvmax" => value * Math.Max(ViewportWidth(), ViewportHeight()) / 100,

            // The container query length units (https://drafts.csswg.org/css-conditional-5/#container-lengths): the small
            // viewport size is used when there is no eligible query container (or when its size is not known without layout)
            "cqw" => value * ContainerUnit(ContainerUnitAxis.Width) / 100,
            "cqh" => value * ContainerUnit(ContainerUnitAxis.Height) / 100,
            "cqi" => value * ContainerUnit(ContainerUnitAxis.Inline) / 100,
            "cqb" => value * ContainerUnit(ContainerUnitAxis.Block) / 100,
            "cqmin" => value * Math.Min(ContainerUnit(ContainerUnitAxis.Inline), ContainerUnit(ContainerUnitAxis.Block)) / 100,
            "cqmax" => value * Math.Max(ContainerUnit(ContainerUnitAxis.Inline), ContainerUnit(ContainerUnitAxis.Block)) / 100,
            _ => null,
        };

        return pixels is { } result ? (result, "px") : null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Fonts
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://drafts.csswg.org/css-fonts/#font-size-prop: an absolute length.</summary>
    private static string ComputeFontSize(ComputedStyle style, string value)
    {
        var parent = style.ParentFontSize;
        if (AbsoluteFontSizes.TryGetValue(value, out var absolute))
            return FormatPixels(absolute);

        double? result = value switch
        {
            "larger" => parent * 1.2,
            "smaller" => parent / 1.2,
            "math" => parent * GetMathScaleFactor(style),
            _ => null,
        };

        if (result is null)
        {
            var context = new ComputationContext(style, hasColors: false, baseUrl: null) { FontSizeContext = true };
            result = EvaluateLength(value, context, percentageBasis: parent);
        }

        return FormatPixels(Math.Max(0, result ?? parent));
    }

    /// <summary>
    /// https://w3c.github.io/mathml-core/#the-math-script-level-property: auto-add adds 1 to the inherited value when the
    /// inherited math-style is compact, add(&lt;integer&gt;) adds the integer to the inherited value.
    /// </summary>
    private static string ComputeMathDepth(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var inherited = GetMathDepth(style.Parent);
        long result;
        if (value == "auto-add")
        {
            result = inherited + (style.Parent?.GetComputedValue("math-style") == "compact" ? 1 : 0);
        }
        else if (ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is [CssFunction function] && InfraAscii.ToAsciiLowercase(function.Name) == "add")
        {
            result = inherited + (TryParseNumber(ComputeGeneric(style, property, CssSerializer.SerializeValue(function.Value, property: null), baseUrl), out var added) ? (long)Math.Floor(added + 0.5) : 0);
        }
        else
        {
            result = TryParseNumber(ComputeGeneric(style, property, value, baseUrl), out var depth) ? (long)Math.Floor(depth + 0.5) : 0;
        }

        return Math.Clamp(result, int.MinValue, int.MaxValue).ToString(CultureInfo.InvariantCulture);
    }

    private static long GetMathDepth(ComputedStyle? style) => style is not null && long.TryParse(style.GetComputedValue("math-depth"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var depth) ? depth : 0;

    /// <summary>
    /// https://w3c.github.io/mathml-core/#the-math-script-level-property: the scale factor of font-size: math, from the
    /// inherited math-depth to the computed math-depth: the scriptPercentScaleDown and scriptScriptPercentScaleDown of the
    /// OpenType MATH table of the inherited first available font are used for the first two levels, 0.71 per level otherwise.
    /// </summary>
    private static double GetMathScaleFactor(ComputedStyle style)
    {
        var a = GetMathDepth(style.Parent);
        var b = GetMathDepth(style);
        if (a == b)
            return 1;

        var invert = false;
        if (b < a)
        {
            (a, b) = (b, a);
            invert = true;
        }

        var e = b - a;
        var scale = 1.0;
        if (style.Parent is { } parent && parent.Fonts.PrimaryFont.Font.MathTable is { HasConstants: true } math)
        {
            var scriptPercentScaleDown = math.ScriptPercentScaleDown is > 0 and var script ? script / 100.0 : 0.71;
            var scriptScriptPercentScaleDown = math.ScriptScriptPercentScaleDown is > 0 and var scriptScript ? scriptScript / 100.0 : 0.5041;
            if (a <= 0 && b >= 2)
            {
                scale *= scriptScriptPercentScaleDown;
                e -= 2;
            }
            else if (a == 1)
            {
                scale *= scriptScriptPercentScaleDown / scriptPercentScaleDown;
                e--;
            }
            else if (b == 1)
            {
                scale *= scriptPercentScaleDown;
                e--;
            }
        }

        scale *= Math.Pow(0.71, e);
        return invert ? 1 / scale : scale;
    }

    /// <summary>Evaluates a length, a percentage (of the basis) or a math function; returns null when the value is something else.</summary>
    private static double? EvaluateLength(string value, ComputationContext context, double percentageBasis)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        if (values is not [var single])
            return null;

        switch (single)
        {
            case CssPreservedToken { Type: CssTokenType.Percentage } percentage:
                return percentage.Token.NumericValue * percentageBasis / 100;
            case CssPreservedToken { Type: CssTokenType.Number, Token.NumericValue: 0 }:
                return 0;
            case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                return ConvertDimension(dimension.Token.NumericValue, InfraAscii.ToAsciiLowercase(dimension.Token.Value), context) is { Unit: "px" } converted ? converted.Value : null;
            case CssFunction function when CssCalc.IsMathFunction(function.Name):
                return CssCalc.Evaluate(function, CreateCalcContext(context, "px", (percentageBasis, "px"))) is { IsResolved: true, Unit: "px" } result ? result.Value : null;
            default:
                return null;
        }
    }

    /// <summary>https://drafts.csswg.org/css-fonts/#font-weight-prop: a number; bolder and lighter are relative to the parent.</summary>
    private static string ComputeFontWeight(ComputedStyle style, string value)
    {
        switch (value)
        {
            case "normal":
                return "400";
            case "bold":
                return "700";
            case "bolder" or "lighter":
            {
                // https://drafts.csswg.org/css-fonts/#relative-weights
                var parent = style.Parent is { } parentStyle && TryParseNumber(parentStyle.GetComputedValue("font-weight"), out var parentWeight) ? parentWeight : 400;
                double weight;
                if (value == "bolder")
                {
                    weight = parent switch
                    {
                        < 350 => 400,
                        < 550 => 700,
                        < 900 => 900,
                        _ => parent,
                    };
                }
                else
                {
                    weight = parent switch
                    {
                        < 100 => parent,
                        < 550 => 100,
                        < 750 => 400,
                        _ => 700,
                    };
                }

                return SerializeNumber(weight);
            }
        }

        var computed = ComputeGeneric(style, CssPropertyRegistry.Get("font-weight"), value, baseUrl: null);
        return TryParseNumber(computed, out var number) ? SerializeNumber(Math.Clamp(number, 1, 1000)) : computed;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts-5/#font-size-adjust-prop: the number is clamped to 0 or more, and from-font is the
    /// aspect value of the first available font of the element for the metric (computed without font-size-adjust).
    /// </summary>
    private static string ComputeFontSizeAdjust(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var computed = ComputeGeneric(style, property, value, baseUrl);
        if (computed == "none")
            return computed;

        var parts = computed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var metric = parts.Length == 2 && FontSizeAdjust.ParseMetric(parts[0]) is { } parsed ? parsed : FontSizeAdjustMetric.ExHeight;
        var number = parts[^1];
        double aspect;
        if (number == "from-font")
        {
            var fonts = style.Engine.Document.FontSelector.GetFonts(FontDescription.FromStyle(style, includeSizeAdjust: false));
            var font = fonts.PrimaryFont;
            aspect = font.GetAspectValue(metric, font.GetEmSize(style.FontSize));
        }
        else if (TryParseNumber(number, out var specified))
        {
            aspect = Math.Max(0, specified);
        }
        else
        {
            return computed;
        }

        return metric == FontSizeAdjustMetric.ExHeight ? SerializeNumber(aspect) : FontSizeAdjust.GetKeyword(metric) + " " + SerializeNumber(aspect);
    }

    /// <summary>https://drafts.csswg.org/css-inline-3/#line-height-property: normal, a number, or an absolute length.</summary>
    private static string ComputeLineHeight(ComputedStyle style, string value)
    {
        if (value == "normal")
            return value;

        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        if (values is [CssPreservedToken { Type: CssTokenType.Number } number])
            return SerializeNumber(Math.Max(0, number.Token.NumericValue));

        var context = new ComputationContext(style, hasColors: false, baseUrl: null) { LineHeightContext = true };
        if (values is [CssFunction function] && CssCalc.IsMathFunction(function.Name)
            && CssCalc.Evaluate(function, CreateCalcContext(context, GetPercentageUnit(function) ?? "px", (style.FontSize, "px"))) is { IsResolved: true } result)
        {
            return result.Unit.Length == 0 ? SerializeNumber(Math.Max(0, result.Value)) : FormatPixels(Math.Max(0, result.Value));
        }

        return EvaluateLength(value, context, style.FontSize) is { } pixels ? FormatPixels(Math.Max(0, pixels)) : value;
    }

    /// <summary>Returns the used line height in px of a computed line-height, or null for normal (it depends on the font metrics).</summary>
    public static double? GetLineHeightPixels(string value, double fontSize)
    {
        if (value == "normal")
            return null;

        if (TryParseNumber(value, out var number))
            return number * fontSize;

        return ParsePixels(value);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Box properties
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-display/#the-display-properties: the display of the root element, of the floats and of
    /// the absolutely positioned elements is blockified (https://drafts.csswg.org/css-display/#transformations), as the
    /// display of the flex and grid items. A vertical -webkit-box whose lines are clamped is a flow-root
    /// (https://drafts.csswg.org/css-overflow-4/#webkit-line-clamp). The math display of an element that is not a MathML
    /// element (including the pseudo-elements) is flow (https://w3c.github.io/mathml-core/#new-display-math-value), and
    /// display: contents computes to none on a MathML element and on the replaced elements and form controls of HTML. The
    /// parent box of a flex or grid item is the nearest ancestor that is not display: contents.
    /// </summary>
    /// <summary>
    /// Returns whether the element of a style is rendered in the top layer (https://drafts.csswg.org/css-position-4/#top-layer):
    /// the style of the element or of its <c>::backdrop</c> pseudo-element (https://drafts.csswg.org/css-position-4/#top-styling:
    /// "every element rendered in the top layer, as well as its corresponding ::backdrop pseudo-element").
    /// </summary>
    /// <remarks>
    /// The select popover of a select element (its <c>::picker(select)</c> pseudo-element) is rendered in the top layer while
    /// its picker is open. The root element that is in the top layer (displayed fullscreen) keeps its position and its
    /// display, as in the browsers: it is already a child of the viewport.
    /// </remarks>
    private static bool IsInTopLayer(ComputedStyle style) => style.PseudoElement switch
    {
        null => IsElementRenderedInTopLayer(style.Element, style) && !ReferenceEquals(style.Element.NodeDocument.DocumentElement, style.Element),
        "backdrop" => IsElementRenderedInTopLayer(style.Element, style: null),
        StyleEngine.SelectPicker or StyleEngine.SelectPickerPrefix + "backdrop" => Dom.Document.IsTopLayerPicker(style.Element),
        _ => false,
    };

    /// <summary>
    /// Whether an element is rendered in the top layer (https://drafts.csswg.org/css-position-4/#rendered-in-the-top-layer): it
    /// is in the top layer and its computed <c>overlay</c> is <c>auto</c> (a select element whose entry is its picker is not).
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="style">The style of the element, or null to get it from the style engine.</param>
    private static bool IsElementRenderedInTopLayer(Element element, ComputedStyle? style)
    {
        var document = element.NodeDocument;
        if (document.TopLayerElementsForRendering is not { Count: > 0 } || Dom.Document.IsTopLayerPicker(element))
            return false;

        style ??= document.GetStyleEngine().GetStyle(element);
        return document.IsRenderedInTopLayer(element, style.GetComputedValue("overlay"));
    }

    /// <summary>
    /// Whether the parent box of an element is a MathML box laid out by the math layout (<c>math</c> or <c>block math</c>): its
    /// float property has no effect (https://w3c.github.io/mathml-core/#css-styling, as in the browsers: WPT
    /// mathml/relations/css-styling/floats/not-floating-001.html), so it does not blockify the display.
    /// </summary>
    private static bool IsChildOfMathContainer(ComputedStyle style)
    {
        var parent = style.Parent;
        while (parent is not null && parent.GetComputedValue("display") == "contents")
        {
            parent = parent.Parent;
        }

        return parent?.GetComputedValue("display") is "math" or "block math";
    }

    /// <summary>
    /// https://drafts.csswg.org/css-overflow-5/#scroll-marker-group-pseudo: "The generation of ::scroll-marker pseudo-elements
    /// shouldn't invalidate layout outside of this pseudo-element": <c>::scroll-marker-group { contain: size !important; }</c>.
    /// As in Chrome, the group always has layout containment too (it is the containing block of its positioned markers), and
    /// an absolutely positioned group has no size containment (it is sized by its markers, which does not affect the layout
    /// outside of it).
    /// </summary>
    private static string ComputeScrollMarkerGroupContain(ComputedStyle style, string value)
    {
        var contain = Layout.LayoutKeywords.ParseContain(value) | Layout.Containment.Layout;
        if (style.GetComputedValue("position") is not ("absolute" or "fixed"))
        {
            contain = (contain & ~Layout.Containment.InlineSize) | Layout.Containment.Size;
        }

        var hasSize = contain.HasFlag(Layout.Containment.Size);
        var hasInlineSize = contain.HasFlag(Layout.Containment.InlineSize);
        var hasStyle = contain.HasFlag(Layout.Containment.Style);
        var hasPaint = contain.HasFlag(Layout.Containment.Paint);
        if (hasStyle && hasPaint && !hasInlineSize)
            return hasSize ? "strict" : "content";

        var result = hasSize ? "size layout" : hasInlineSize ? "inline-size layout" : "layout";
        if (hasStyle)
        {
            result += " style";
        }

        return hasPaint ? result + " paint" : result;
    }

    /// <summary>
    /// https://drafts.css-houdini.org/css-layout-api-1/#box-tree-transformations: the display of the children of a layout API
    /// container is blockified, unless the <c>childDisplay</c> of the layout options of its layout is <c>normal</c>. As in
    /// Chromium, the children are blockified while the layout is not registered (the default <c>childDisplay</c> is <c>block</c>).
    /// </summary>
    private static bool BlockifiesLayoutApiChildren(ComputedStyle style, string parentDisplay)
    {
        if (CssDisplay.GetLayoutFunctionName(parentDisplay) is not { } name)
            return false;

        return style.Element.NodeDocument.LayoutWorkletOrNull?.GetDocumentLayoutDefinition(name) is not { Options.ChildDisplay: ChildDisplayType.Normal };
    }

    private static string ComputeDisplay(ComputedStyle style, string value)
    {
        style.IsLegacyLineClampBox = false;
        var display = CssDisplay.Canonicalize(value);
        if (display is "none")
            return display;

        // The registration of a layout changes the styles and the boxes of the layout API containers (see LayoutWorklet)
        if (CssDisplay.IsLayoutFunction(display))
        {
            style.Element.NodeDocument.LayoutWorkletCore.HasLayoutContainers = true;
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#embedded-content-rendering-rules: an audio element that is not
        // exposing a user interface (it has no controls attribute and scripting is enabled) is not rendered, irrespective of
        // the CSS rules
        if (style.PseudoElement is null && style.Element is HtmlAudioElement audio && !audio.HasAttributeWithLocalName("controls") && audio.NodeDocument.ScriptingEnabled)
            return "none";

        if (display is "math" or "block math" && (style.PseudoElement is not null || style.Element.NamespaceUri != Namespaces.MathML))
        {
            display = display == "math" ? "inline" : "block";
        }

        // https://drafts.csswg.org/css-display/#unbox-mathml: display: contents computes to none on the MathML elements
        if (display == "contents" && style.PseudoElement is null && style.Element.NamespaceUri == Namespaces.MathML)
            return "none";

        // https://drafts.csswg.org/css-display/#unbox-html: display: contents computes to none on the replaced elements and
        // the form controls of HTML
        if (display == "contents" && style.PseudoElement is null && style.Element.IsHtmlNamespace && style.Element.LocalName is "br" or "wbr" or "meter" or "progress" or "canvas" or "embed" or "object" or "audio" or "iframe" or "img" or "video" or "frame" or "frameset" or "input" or "textarea" or "select")
            return "none";

        if (display is "-webkit-box" or "-webkit-inline-box" && style.GetComputedValue("-webkit-box-orient") == "vertical" && style.GetComputedValue("continue") is "collapse" or "discard" or "-webkit-legacy")
        {
            display = display == "-webkit-box" ? "flow-root" : "inline-block";
            style.IsLegacyLineClampBox = true;
        }

        if (style.IsRoot)
            return display == "contents" ? "block" : CssDisplay.Blockify(display);

        // https://drafts.csswg.org/css-lists-3/#list-style-position-property: an inside marker is an inline box, an outside
        // marker is a block container (display does not apply to ::marker)
        if (style.PseudoElement == "marker")
            return style.GetComputedValue("list-style-position") == "outside" ? "inline-block" : "inline";

        // https://drafts.csswg.org/css-position-4/#top-styling: "If its specified display property is contents, it computes to block"
        if (display == "contents")
            return IsInTopLayer(style) ? "block" : display;

        var blockify = style.GetComputedValue("position") is "absolute" or "fixed" || (style.GetComputedValue("float") != "none" && !IsChildOfMathContainer(style));
        if (!blockify && (style.PseudoElement is null or "before" or "after" or StyleEngine.DetailsContent or StyleEngine.Checkmark or StyleEngine.PickerIcon or StyleEngine.SelectPicker))
        {
            // The ancestors that are display: contents do not generate a box: the parent box is the box of the nearest other ancestor
            var parent = style.Parent;
            while (parent is not null && parent.GetComputedValue("display") == "contents")
            {
                parent = parent.Parent;
            }

            var parentDisplay = parent?.GetComputedValue("display");
            blockify = parentDisplay is "flex" or "inline-flex" or "grid" or "inline-grid" or "grid-lanes" or "inline-grid-lanes" or "inline grid-lanes" or "block flex" or "block grid"
                || parentDisplay is not null && BlockifiesLayoutApiChildren(style, parentDisplay);

            // https://drafts.csswg.org/css-writing-modes-4/#block-flow: "If a box has a different writing-mode value than its
            // parent box (i.e. nearest ancestor without display: contents) [...] If the box has a specified display of inline,
            // its display computes to inline-block. If the box has a specified display of run-in, its display computes to block."
            if (!blockify && display is "inline" or "run-in" && parent is not null && parent.GetComputedValue("writing-mode") != style.GetComputedValue("writing-mode"))
                return display == "inline" ? "inline-block" : "block";
        }

        return blockify ? CssDisplay.Blockify(display) : display;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-backgrounds/#border-width: an absolute length snapped as a border width, or zero when
    /// the style is none (or hidden).
    /// </summary>
    /// <summary>
    /// https://drafts.csswg.org/css-gaps/#column-row-rule-width: each width of the list is snapped as a border width, and does
    /// not depend on the rule style.
    /// </summary>
    private static string ComputeRuleWidths(ComputedStyle style, CssPropertyDefinition property, string value)
    {
        var values = CssParser.ParseListOfComponentValues(value);
        if (ValueMatcher.RemoveWhitespace(values) is [var single] && single is not CssFunction { Name: "repeat" or "REPEAT" })
            return ComputeLineWidth(style, CssSerializer.SerializeValue([single], property));

        var builder = new StringBuilder();
        AppendWidths(values);
        return builder.ToString();

        void AppendWidths(List<CssComponentValue> list)
        {
            foreach (var component in list)
            {
                switch (component)
                {
                    case CssPreservedToken { Type: CssTokenType.Whitespace }:
                        break;

                    case CssPreservedToken { Type: CssTokenType.Comma }:
                        builder.Append(", ");
                        break;

                    case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "repeat"):
                        builder.Append("repeat(");
                        var arguments = SplitArguments(function.Value);
                        // The integer of a math function is rounded
                        builder.Append(arguments[0] is [CssFunction math] && CssCalc.IsMathFunction(math.Name) && CssCalc.Evaluate(math, CssCalcContext.Default) is { IsNumeric: true, Unit: "" } count
                            ? SerializeNumber(Math.Floor(count.Value + 0.5))
                            : CssSerializer.SerializeValue(arguments[0], property: null));
                        foreach (var argument in arguments.Skip(1))
                        {
                            builder.Append(", ").Append(ComputeLineWidth(style, CssSerializer.SerializeValue(argument, property)));
                        }

                        builder.Append(')');
                        break;

                    default:
                        builder.Append(ComputeLineWidth(style, CssSerializer.SerializeValue([component], property)));
                        break;
                }
            }
        }
    }

    private static string ComputeLineWidth(ComputedStyle style, string value)
    {
        double? width = value switch
        {
            // https://drafts.csswg.org/css-borders-4/#valdef-line-width-hairline: one device pixel
            "hairline" => 1 / style.Environment.DevicePixelRatio,
            "thin" => 1,
            "medium" => 3,
            "thick" => 5,
            _ => EvaluateLength(value, new ComputationContext(style, hasColors: false, baseUrl: null), percentageBasis: 0),
        };

        if (width is not { } pixels)
            return ComputeGeneric(style, property: null, value, baseUrl: null);

        // https://drafts.csswg.org/css-values-4/#snap-a-length-as-a-border-width
        // (the length is rounded to a single-precision float first, as in the browsers: 25.4mm is 96px, not 95.99999999999999px
        // floored to 95px)
        var ratio = style.Environment.DevicePixelRatio;
        var devicePixels = (double)(float)(Math.Max(0, pixels) * ratio);
        if (devicePixels > 0 && devicePixels < 1)
        {
            devicePixels = 1;
        }
        else
        {
            devicePixels = Math.Floor(devicePixels);
        }

        return FormatPixels(devicePixels / ratio);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-backgrounds/#background-position: the positions compute to a pair of offsets from the top
    /// left corner (the keywords are percentages, e.g. center is 50% and right 10px is calc(100% - 10px)).
    /// </summary>
    private static string ComputePosition(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var computed = ComputeGeneric(style, property, value, baseUrl);
        var singleAxis = property.Name.EndsWith("-x", StringComparison.Ordinal) || property.Name.EndsWith("-y", StringComparison.Ordinal);
        var layers = computed.Split(", ");
        for (var i = 0; i < layers.Length; i++)
        {
            var layer = ResolveLogicalPositionKeywords(style, layers[i]);
            layers[i] = singleAxis ? ComputeAxisPosition(layer) : ComputePositionLayer(layer);
        }

        return string.Join(", ", layers);
    }

    /// <summary>
    /// Replaces the logical keywords of a &lt;position&gt; (https://drafts.csswg.org/css-values-5/#position) by the physical
    /// keywords of the writing mode and the direction of the element: x-start and x-end are the start and end sides of the
    /// horizontal axis (the inline axis in a horizontal writing mode, the block axis otherwise), y-start and y-end the ones of
    /// the vertical axis.
    /// </summary>
    private static string ResolveLogicalPositionKeywords(ComputedStyle style, string layer)
    {
        if (!layer.Contains("x-", StringComparison.Ordinal) && !layer.Contains("y-", StringComparison.Ordinal))
            return layer;

        var parts = SplitComponents(layer);
        if (!Array.Exists(parts, part => part is "x-start" or "x-end" or "y-start" or "y-end"))
            return layer;

        var (xStartIsLeft, yStartIsTop) = GetLogicalPositionSides(style);
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = ResolveLogicalPositionKeyword(parts[i], xStartIsLeft, yStartIsTop);
        }

        return string.Join(' ', parts);
    }

    /// <summary>Returns whether x-start is the left side and y-start the top side for the writing mode and the direction of an element.</summary>
    private static (bool XStartIsLeft, bool YStartIsTop) GetLogicalPositionSides(ComputedStyle style)
    {
        var writingMode = style.GetComputedValue("writing-mode");
        var rtl = LogicalProperties.IsRightToLeft(style);
        var xStartIsLeft = writingMode switch
        {
            "horizontal-tb" => !rtl,
            "vertical-rl" or "sideways-rl" => false,
            _ => true,
        };
        var yStartIsTop = writingMode switch
        {
            "horizontal-tb" => true,
            "sideways-lr" => rtl,
            _ => !rtl,
        };
        return (xStartIsLeft, yStartIsTop);
    }

    private static string ResolveLogicalPositionKeyword(string keyword, bool xStartIsLeft, bool yStartIsTop) => keyword switch
    {
        "x-start" => xStartIsLeft ? "left" : "right",
        "x-end" => xStartIsLeft ? "right" : "left",
        "y-start" => yStartIsTop ? "top" : "bottom",
        "y-end" => yStartIsTop ? "bottom" : "top",
        _ => keyword,
    };

    /// <summary>
    /// Replaces the logical keywords (x-start, x-end, y-start, y-end) of the positions and of the lines of a computed
    /// <c>shape()</c> (https://drafts.csswg.org/css-shapes-2/#shape-function) by the physical keywords of the element (see
    /// <see cref="ResolveLogicalPositionKeywords"/>): the points of its computed value are offsets from the top left corner.
    /// </summary>
    private static string ResolveLogicalShapeKeywords(ComputedStyle style, string shape)
    {
        if (!shape.Contains("x-", StringComparison.Ordinal) && !shape.Contains("y-", StringComparison.Ordinal))
            return shape;

        var (xStartIsLeft, yStartIsTop) = GetLogicalPositionSides(style);
        var result = new System.Text.StringBuilder(shape.Length);
        for (var i = 0; i < shape.Length;)
        {
            // An identifier of the value (the keywords are separated by spaces, commas and parentheses)
            var end = i;
            while (end < shape.Length && (char.IsAsciiLetterOrDigit(shape[end]) || shape[end] is '-' or '_' or '.' or '%'))
            {
                end++;
            }

            if (end == i)
            {
                result.Append(shape[i]);
                i++;
                continue;
            }

            result.Append(ResolveLogicalPositionKeyword(shape[i..end], xStartIsLeft, yStartIsTop));
            i = end;
        }

        return result.ToString();
    }

    /// <summary>A position on one axis (background-position-x): a keyword and an optional offset.</summary>
    private static string ComputeAxisPosition(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts switch
        {
            ["left" or "top" or "x-start" or "y-start"] => "0%",
            ["center"] => "50%",
            ["right" or "bottom" or "x-end" or "y-end"] => "100%",
            ["left" or "top" or "x-start" or "y-start", var offset] => offset,
            ["right" or "bottom" or "x-end" or "y-end", var offset] => FromEnd(offset),
            _ => value,
        };
    }

    /// <summary>A &lt;position&gt;: a pair of offsets from the top left corner (e.g. right 10% bottom 5px is 90% calc(100% - 5px)).</summary>
    internal static string ComputePositionLayer(string value)
    {
        var parts = SplitComponents(value);
        static bool IsKeyword(string part) => part is "left" or "right" or "top" or "bottom" or "center";

        // A single keyword or offset: the other axis is center
        if (parts.Length == 1)
        {
            return parts[0] switch
            {
                "top" or "bottom" => "50% " + Offset(parts[0], offset: null),
                var single when IsKeyword(single) => Offset(single, offset: null) + " 50%",
                _ => value + " 50%",
            };
        }

        // Two values: horizontal then vertical (the keywords can be in any order: top left)
        if (parts.Length == 2)
        {
            var (horizontal, vertical) = parts[0] is "top" or "bottom" || parts[1] is "left" or "right" ? (parts[1], parts[0]) : (parts[0], parts[1]);
            return (IsKeyword(horizontal) ? Offset(horizontal, offset: null) : horizontal) + " " + (IsKeyword(vertical) ? Offset(vertical, offset: null) : vertical);
        }

        // The keywords followed by an optional offset (e.g. right 10% bottom, bottom 16% left)
        var sides = new List<(string Keyword, string? Offset)>(2);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!IsKeyword(parts[i]))
                return value;

            string? offset = null;
            if (i + 1 < parts.Length && !IsKeyword(parts[i + 1]))
            {
                offset = parts[i + 1];
                i++;
            }

            sides.Add((parts[i - (offset is null ? 0 : 1)], offset));
        }

        if (sides.Count != 2)
            return value;

        var (x, y) = sides[0].Keyword is "top" or "bottom" || sides[1].Keyword is "left" or "right" ? (sides[1], sides[0]) : (sides[0], sides[1]);
        return Offset(x.Keyword, x.Offset) + " " + Offset(y.Keyword, y.Offset);

        static string Offset(string keyword, string? offset) => keyword switch
        {
            "center" => "50%",
            "right" or "bottom" => offset is null ? "100%" : FromEnd(offset),
            _ => offset ?? "0%",
        };
    }

    /// <summary>An offset from the right or bottom edge: calc(100% - offset), simplified when the offset is a percentage (right 20% is 80%).</summary>
    private static string FromEnd(string offset)
    {
        if (offset.EndsWith('%', StringComparison.Ordinal) && TryParseNumber(offset[..^1], out var percentage))
            return SerializeNumber(100 - percentage) + "%";

        return offset.StartsWith('-', StringComparison.Ordinal) && !offset.StartsWith("-(", StringComparison.Ordinal) ? "calc(100% + " + offset[1..] + ")" : "calc(100% - " + offset + ")";
    }

    /// <summary>
    /// https://drafts.csswg.org/css-transforms/#transform-origin-property: the keywords are converted to percentages, the
    /// lengths are absolute; the z offset is kept, unless it is zero (the shortest form, as in the browsers, which is also the
    /// form of the interpolated values).
    /// </summary>
    private static string ComputeTransformOrigin(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var parts = SplitComponents(ComputeGeneric(style, property, value, baseUrl));
        if (parts.Length == 3)
            return ComputePositionLayer(parts[0] + " " + parts[1]) + (parts[2] == "0px" ? "" : " " + parts[2]);

        return ComputePositionLayer(string.Join(' ', parts));
    }

    /// <summary>
    /// https://drafts.csswg.org/css-tables-3/#propdef-border-spacing: two absolute lengths, serialized in their shortest form
    /// as in the browsers (the vertical spacing is omitted when it is equal to the horizontal spacing).
    /// </summary>
    private static string ComputeBorderSpacing(string computed)
    {
        var parts = SplitComponents(computed);
        return parts.Length == 2 && parts[0] == parts[1] ? parts[0] : computed;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-transforms-2/#propdef-scale: the percentages are converted to numbers. The value is
    /// serialized in its shortest form: the z scale is omitted when it is 1, and the y scale when it is equal to the x scale.
    /// </summary>
    private static string ComputeScale(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var computed = ComputeGeneric(style, property, value, baseUrl);
        var parts = SplitComponents(computed);
        var numbers = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.EndsWith('%', StringComparison.Ordinal) && TryParseNumber(part[..^1], out var percentage))
            {
                numbers[i] = percentage / 100;
            }
            else if (!TryParseNumber(part, out numbers[i]))
            {
                return computed;
            }
        }

        var count = numbers.Length;
        if (count == 3 && numbers[2] == 1)
        {
            count = 2;
        }

        if (count == 2 && numbers[0] == numbers[1])
        {
            count = 1;
        }

        return string.Join(' ', numbers.Take(count).Select(SerializeNumber));
    }

    /// <summary>
    /// https://drafts.csswg.org/css-transforms-2/#propdef-translate: the shortest form, without a zero z translation and then
    /// without a zero y translation (a zero length, not a percentage).
    /// </summary>
    private static string ComputeTranslate(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var computed = ComputeGeneric(style, property, value, baseUrl);
        var parts = SplitComponents(computed).ToList();
        if (parts.Count == 3 && parts[2] == "0px")
        {
            parts.RemoveAt(2);
        }

        if (parts.Count == 2 && parts[1] == "0px")
        {
            parts.RemoveAt(1);
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-transforms-2/#propdef-rotate: the axis precedes the angle; an axis parallel to the x
    /// or y axis is serialized as the keyword (the sign of the vector is applied to the angle), and the z axis is omitted.
    /// </summary>
    private static string ComputeRotate(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var computed = ComputeGeneric(style, property, value, baseUrl);
        var parts = SplitComponents(computed);
        var angleIndex = Array.FindIndex(parts, part => part.EndsWith("deg", StringComparison.Ordinal) && TryParseNumber(part[..^3], out _));
        if (angleIndex < 0 || parts.Length is not (1 or 2 or 4))
            return computed;

        var angle = double.Parse(parts[angleIndex].AsSpan(0, parts[angleIndex].Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture);
        var axis = parts.Where((_, i) => i != angleIndex).ToArray();
        double x, y, z;
        switch (axis)
        {
            case []:
            case ["z"]:
                (x, y, z) = (0, 0, 1);
                break;
            case ["x"]:
                (x, y, z) = (1, 0, 0);
                break;
            case ["y"]:
                (x, y, z) = (0, 1, 0);
                break;
            case [var a, var b, var c] when TryParseNumber(a, out x) && TryParseNumber(b, out y) && TryParseNumber(c, out z):
                break;
            default:
                return computed;
        }

        static string Angle(double value) => SerializeNumber(value) + "deg";

        if (y == 0 && z == 0 && x != 0)
            return "x " + Angle(Math.Sign(x) * angle);

        if (x == 0 && z == 0 && y != 0)
            return "y " + Angle(Math.Sign(y) * angle);

        if (x == 0 && y == 0 && z != 0)
            return Angle(Math.Sign(z) * angle);

        return SerializeNumber(x) + " " + SerializeNumber(y) + " " + SerializeNumber(z) + " " + Angle(angle);
    }

    /// <summary>Splits a value on its top-level whitespace (a math function is a single component).</summary>
    /// <summary>
    /// https://drafts.csswg.org/css-text-decor-4/#text-shadow-property: each shadow computes to a color (currentcolor when
    /// it is omitted) and three absolute lengths (the blur radius is 0px when it is omitted).
    /// </summary>
    private static string ComputeTextShadow(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var computed = ComputeGeneric(style, property, value, baseUrl);
        var shadows = new List<string>();
        foreach (var shadow in SplitArguments(CssParser.ParseListOfComponentValues(computed)))
        {
            string? color = null;
            var lengths = new List<string>(3);
            foreach (var component in shadow)
            {
                var text = CssSerializer.SerializeValue([component], property: null);
                if (component is CssPreservedToken { Type: CssTokenType.Dimension or CssTokenType.Number })
                {
                    lengths.Add(text == "0" ? "0px" : text);
                }
                else
                {
                    color = text;
                }
            }

            while (lengths.Count < 3)
            {
                lengths.Add("0px");
            }

            // The blur radius is non-negative (https://drafts.csswg.org/css-backgrounds-3/#shadow-blur-radius): a negative result of a
            // math function is clamped when it is computed (https://drafts.csswg.org/css-values-4/#calc-range)
            if (lengths[2].StartsWith('-', StringComparison.Ordinal))
            {
                lengths[2] = "0px";
            }

            shadows.Add((color ?? "currentcolor") + " " + string.Join(' ', lengths));
        }

        return string.Join(", ", shadows);
    }

    /// <summary>Splits the space-separated components of a computed value; a function (e.g. calc(50% + 10px)) is one component.</summary>
    internal static string[] SplitSpaceSeparated(string value) => value.Contains('(', StringComparison.Ordinal) ? SplitComponents(value) : value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    internal static string[] SplitComponents(string value)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        var result = new string[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            result[i] = CssSerializer.SerializeValue([values[i]], property: null);
        }

        return result;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-grid/#typedef-grid-row-start-grid-line: the integer of a math function is rounded, and
    /// the span is at least 1.
    /// </summary>
    private static string ComputeGridLine(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var parts = SplitComponents(ComputeGeneric(style, property, value, baseUrl));
        var span = parts.Contains("span", StringComparer.Ordinal);
        for (var i = 0; i < parts.Length; i++)
        {
            if (TryParseNumber(parts[i], out var number))
            {
                number = Math.Floor(number + 0.5);
                parts[i] = SerializeNumber(span ? Math.Max(1, number) : number);
            }
        }

        // span 1 <custom-ident> is span <custom-ident>
        return string.Join(' ', parts.Length == 3 && span && parts[1] == "1" ? [parts[0], parts[2]] : parts);
    }

    // https://drafts.csswg.org/css-grid/#track-sizing: the track sizes are not negative (math functions are clamped)
    [GeneratedRegex(@"(?<![\w.-])-[0-9]+(\.[0-9]+)?(e[+-]?[0-9]+)?px", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NegativeLength();

    // A negative percentage that is not the first term of a calc() (e.g. calc(-5% + 10px))
    [GeneratedRegex(@"(?<![\w.-]|calc\()-[0-9]+(\.[0-9]+)?(e[+-]?[0-9]+)?%", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NegativePercentage();

    /// <summary>
    /// Clamps the negative lengths and percentages of a computed value whose components are non-negative (e.g. the track
    /// sizes): they are the results of math functions, which are clamped to the range of the type
    /// (https://drafts.csswg.org/css-values-4/#calc-range).
    /// </summary>
    private static string ClampNegativeLengthPercentages(string computed)
    {
        if (!computed.Contains('-', StringComparison.Ordinal))
            return computed;

        computed = NegativeLength().Replace(computed, "0px");
        return computed.Contains('%', StringComparison.Ordinal) ? NegativePercentage().Replace(computed, "0%") : computed;
    }

    /// <summary>https://drafts.csswg.org/css-fonts/#font-width-prop: a negative percentage (of a math function) is clamped to 0%.</summary>
    private static string ClampPercentage(string value) => value.StartsWith('-', StringComparison.Ordinal) && value.EndsWith('%', StringComparison.Ordinal) && TryParseNumber(value[..^1], out _) ? "0%" : value;

    /// <summary>https://drafts.csswg.org/css-fonts/#font-style-prop: the oblique angle is clamped to [-90deg, 90deg], and oblique 0deg is normal.</summary>
    private static string ComputeFontStyle(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var computed = ComputeGeneric(style, property, value, baseUrl);
        if (SplitComponents(computed) is ["oblique", var angle] && angle.EndsWith("deg", StringComparison.Ordinal) && TryParseNumber(angle[..^3], out var degrees))
        {
            degrees = Math.Clamp(degrees, -90, 90);
            return degrees == 0 ? "normal" : "oblique " + SerializeNumber(degrees) + "deg";
        }

        return computed;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts/#font-feature-settings-prop: the duplicate features are removed (the last one
    /// wins), and the features are sorted by tag, like the browsers. The same applies to the axes of font-variation-settings.
    /// </summary>
    private static string ComputeFontSettings(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var computed = ComputeGeneric(style, property, value, baseUrl);
        if (computed == "normal")
            return computed;

        var settings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in computed.Split(", "))
        {
            var tagEnd = setting.IndexOf('"', 1, StringComparison.Ordinal);
            if (!setting.StartsWith('"', StringComparison.Ordinal) || tagEnd < 0)
                return computed;

            settings[setting[..(tagEnd + 1)]] = setting;
        }

        return string.Join(", ", settings.Values);
    }

    /// <summary>https://drafts.csswg.org/css-text-4/#text-align-property: match-parent computes to the parent's value, with start and end resolved.</summary>
    private static string ComputeTextAlign(ComputedStyle style, string property, string value)
    {
        if (value != "match-parent")
            return value;

        if (style.Parent is not { } parent)
            return property == "text-align-all" ? "start" : "auto";

        var parentValue = parent.GetComputedValue(property);
        var rtl = parent.GetComputedValue("direction") == "rtl";
        return parentValue switch
        {
            "start" => rtl ? "right" : "left",
            "end" => rtl ? "left" : "right",
            _ => parentValue,
        };
    }

    /// <summary>https://drafts.csswg.org/css-color-4/#transparency: a number clamped to [0, 1].</summary>
    private static string ComputeOpacity(ComputedStyle style, string value)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        double? number = values switch
        {
            [CssPreservedToken { Type: CssTokenType.Number } token] => token.Token.NumericValue,
            [CssPreservedToken { Type: CssTokenType.Percentage } token] => token.Token.NumericValue / 100,
            [CssFunction function] when CssCalc.IsMathFunction(function.Name) => CssCalc.Evaluate(function, CreateCalcContext(new ComputationContext(style, hasColors: false, baseUrl: null), percentageUnit: "", (1, ""))) is { IsResolved: true, Unit: "" } result ? result.Value : null,
            _ => null,
        };

        return number is { } opacity ? SerializeNumber(double.IsNaN(opacity) ? 0 : Math.Clamp(opacity, 0, 1)) : value;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-viewport/#zoom-property: the computed value is a number (a percentage is converted). As in
    /// the browsers, the legacy normal keyword and 0 compute to 1.
    /// </summary>
    private static string ComputeZoom(ComputedStyle style, string value)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        double? number = values switch
        {
            [CssPreservedToken { Type: CssTokenType.Ident } token] when token.Token.IsIdent("normal") => 1,
            [CssPreservedToken { Type: CssTokenType.Number } token] => token.Token.NumericValue,
            [CssPreservedToken { Type: CssTokenType.Percentage } token] => token.Token.NumericValue / 100,
            [CssFunction function] when CssCalc.IsMathFunction(function.Name) => CssCalc.Evaluate(function, CreateCalcContext(new ComputationContext(style, hasColors: false, baseUrl: null), percentageUnit: "", (1, ""))) is { IsResolved: true, Unit: "" } result ? result.Value : null,
            _ => null,
        };

        if (number is not { } zoom)
            return value;

        zoom = double.IsNaN(zoom) ? 0 : Math.Max(zoom, 0);
        return SerializeNumber(zoom == 0 ? 1 : zoom);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Colors (https://drafts.csswg.org/css-color-4/#resolving-color-values)
    // ----------------------------------------------------------------------------------------------------------------

    private static readonly FrozenDictionary<string, string> DeprecatedSystemColors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // https://drafts.csswg.org/css-color-4/#deprecated-system-colors
        ["activeborder"] = "buttonborder",
        ["activecaption"] = "canvas",
        ["appworkspace"] = "canvas",
        ["background"] = "canvas",
        ["buttonhighlight"] = "buttonface",
        ["buttonshadow"] = "buttonface",
        ["captiontext"] = "canvastext",
        ["inactiveborder"] = "buttonborder",
        ["inactivecaption"] = "canvas",
        ["inactivecaptiontext"] = "graytext",
        ["infobackground"] = "canvas",
        ["infotext"] = "canvastext",
        ["menu"] = "canvas",
        ["menutext"] = "canvastext",
        ["scrollbar"] = "canvas",
        ["threeddarkshadow"] = "buttonborder",
        ["threedface"] = "buttonface",
        ["threedhighlight"] = "buttonborder",
        ["threedlightshadow"] = "buttonborder",
        ["threedshadow"] = "buttonborder",
        ["window"] = "canvas",
        ["windowframe"] = "buttonborder",
        ["windowtext"] = "canvastext",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    // https://drafts.csswg.org/css-color-4/#css-system-colors (the colors of the browsers)
    private static readonly FrozenDictionary<string, string> LightSystemColors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["accentcolor"] = "rgb(0, 117, 255)",
        ["accentcolortext"] = "rgb(255, 255, 255)",
        ["activetext"] = "rgb(255, 0, 0)",
        ["buttonborder"] = "rgb(118, 118, 118)",
        ["buttonface"] = "rgb(239, 239, 239)",
        ["buttontext"] = "rgb(0, 0, 0)",
        ["canvas"] = "rgb(255, 255, 255)",
        ["canvastext"] = "rgb(0, 0, 0)",
        ["field"] = "rgb(255, 255, 255)",
        ["fieldtext"] = "rgb(0, 0, 0)",
        ["graytext"] = "rgb(128, 128, 128)",
        ["highlight"] = "rgb(181, 213, 255)",
        ["highlighttext"] = "rgb(0, 0, 0)",
        ["linktext"] = "rgb(0, 0, 238)",
        ["mark"] = "rgb(255, 255, 0)",
        ["marktext"] = "rgb(0, 0, 0)",
        ["selecteditem"] = "rgb(0, 117, 255)",
        ["selecteditemtext"] = "rgb(255, 255, 255)",
        ["visitedtext"] = "rgb(85, 26, 139)",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> DarkSystemColors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["accentcolor"] = "rgb(153, 200, 255)",
        ["accentcolortext"] = "rgb(0, 0, 0)",
        ["activetext"] = "rgb(255, 158, 158)",
        ["buttonborder"] = "rgb(107, 107, 107)",
        ["buttonface"] = "rgb(107, 107, 107)",
        ["buttontext"] = "rgb(255, 255, 255)",
        ["canvas"] = "rgb(18, 18, 18)",
        ["canvastext"] = "rgb(255, 255, 255)",
        ["field"] = "rgb(59, 59, 59)",
        ["fieldtext"] = "rgb(255, 255, 255)",
        ["graytext"] = "rgb(128, 128, 128)",
        ["highlight"] = "rgb(99, 99, 99)",
        ["highlighttext"] = "rgb(255, 255, 255)",
        ["linktext"] = "rgb(158, 158, 255)",
        ["mark"] = "rgb(102, 92, 0)",
        ["marktext"] = "rgb(255, 255, 255)",
        ["selecteditem"] = "rgb(153, 200, 255)",
        ["selecteditemtext"] = "rgb(59, 59, 59)",
        ["visitedtext"] = "rgb(208, 173, 240)",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static bool IsColorFunction(string name) => name is "rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or "ictcp" or "jzazbz" or "jzczhz" or "color" or "device-cmyk" or "light-dark" or "color-mix" or "color-hdr" or "alpha" or "contrast-color";

    /// <summary>The color property: currentcolor is the inherited color (https://drafts.csswg.org/css-color-4/#resolving-color-values).</summary>
    private static string ComputeColorProperty(ComputedStyle style, string value)
    {
        // An inherited color-hdr() cannot be resolved to a color (https://drafts.csswg.org/css-color-hdr-1/#hdr-color-function):
        // currentcolor is replaced with it, e.g. color-mix(in srgb, currentcolor, red) keeps the color-hdr() of the parent
        if (!style.IsHighlight && value.Contains("currentcolor", StringComparison.Ordinal) && GetInheritedColor(style) is var inherited && inherited.Contains("color-hdr(", StringComparison.Ordinal))
        {
            value = ReplaceCurrentColor(value, inherited);
        }

        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        if (values is [var single] && ComputeColor(single, style, isColorProperty: true) is { } color)
            return color;

        return ComputeGeneric(style, property: null, value, baseUrl: null, hasColors: true);
    }

    /// <summary>Returns the computed color of a component value, or null when it is not a color (or cannot be computed).</summary>
    private static string? ComputeColor(CssComponentValue value, ComputedStyle style, bool isColorProperty)
    {
        // currentcolor is kept in the computed value of the other properties (it is resolved in the used value), and in the
        // computed value of the color of the highlight pseudo-elements (https://drafts.csswg.org/css-pseudo-4/#highlight-cascade)
        if ((!isColorProperty || style.IsHighlight) && value is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent("currentcolor"))
            return "currentcolor";

        // https://drafts.csswg.org/css-color-hdr-1/#hdr-color-function: the actual color depends on the HDR headroom, which
        // is not exposed, so the computed value keeps the function with the computed colors and headrooms
        if (value is CssFunction function && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "color-hdr"))
        {
            if (CssHdrColor.GetStops(function) is not { } stops)
                return null;

            var context = new ComputationContext(style, hasColors: false, baseUrl: null);
            var computed = new List<(string Color, string? Headroom)>(stops.Length);
            foreach (var stop in stops)
            {
                if (ComputeColor(stop.Color, style, isColorProperty) is not { } color)
                    return null;

                string? headroom = null;
                if (stop.Headroom is not null)
                {
                    if (CssHdrColor.EvaluateHeadroom(stop.Headroom, CreateCalcContext(context, percentageUnit: null)) is not { } headroomValue)
                        return null;

                    headroom = SerializeNumber(headroomValue);
                }

                computed.Add((color, headroom));
            }

            return CssHdrColor.Serialize(computed);
        }

        return ResolveColor(value, style, isColorProperty)?.Serialize();
    }

    /// <summary>Resolves a color (https://drafts.csswg.org/css-color-4/#resolving-color-values), or returns null when it is not a color or depends on currentcolor (outside the color property).</summary>
    private static CssColor? ResolveColor(CssComponentValue value, ComputedStyle style, bool isColorProperty)
    {
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Ident } ident:
            {
                var name = InfraAscii.ToAsciiLowercase(ident.Token.Value);
                if (name == "currentcolor")
                    return isColorProperty ? ParseColor(GetInheritedColor(style)) : null;

                if (name == "transparent" || CssColor.IsNamedColor(name))
                    return CssColor.Parse(value);

                name = DeprecatedSystemColors.GetValueOrDefault(name, name);
                return GetSystemColors(style).TryGetValue(name, out var systemColor) ? ParseColor(systemColor) : null;
            }

            case CssPreservedToken { Type: CssTokenType.Hash }:
                return CssColor.Parse(value);

            case CssFunction function:
            {
                var name = InfraAscii.ToAsciiLowercase(function.Name);
                if (name == "light-dark")
                {
                    // https://drafts.csswg.org/css-color-5/#light-dark
                    var arguments = SplitArguments(function.Value);
                    if (arguments is not [[var light], [var dark]])
                        return null;

                    return ResolveColor(GetUsedColorScheme(style) == "dark" ? dark : light, style, isColorProperty);
                }

                if (name == "contrast-color")
                    return ValueMatcher.RemoveWhitespace(function.Value) is [var background] && ResolveColor(background, style, isColorProperty) is { } backgroundColor ? ComputeContrastColor(backgroundColor) : null;

                // The color of color-hdr() depends on the HDR headroom, which is not exposed: a color that depends on it (e.g.
                // color-mix() or a relative color) keeps its specified form, as with currentcolor
                if (name == "color-hdr")
                    return null;

                var context = new ComputationContext(style, hasColors: false, baseUrl: null);
                if (name == "color-mix")
                {
                    return CssColorMix.Compute(
                        function,
                        color => ResolveColor(color, style, isColorProperty),
                        math => CssCalc.Evaluate(math, CreateCalcContext(context, percentageUnit: null)) is { IsNumeric: true, Unit: "%" } result ? result.Value : null);
                }

                if (!IsColorFunction(name))
                    return null;

                if (CssRelativeColor.IsRelativeColorSyntax(function))
                    return CssRelativeColorResolver.Compute(function, color => ResolveColor(color, style, isColorProperty), math => CssCalc.Evaluate(math, CreateCalcContext(context, percentageUnit: null)));

                // The math functions of the channels are evaluated first
                var copy = new CssFunction(function.Name);
                var hasMathFunction = false;
                foreach (var argument in function.Value)
                {
                    if (argument is CssFunction math && CssCalc.IsMathFunction(math.Name))
                    {
                        hasMathFunction = true;
                        var result = CssCalc.Evaluate(math, CreateCalcContext(context, percentageUnit: null));
                        if (result is not { IsNumeric: true })
                            return null;

                        copy.Value.Add(result.Unit switch
                        {
                            "" => new CssPreservedToken(new CssToken { Type = CssTokenType.Number, NumericValue = result.Value }),
                            "%" => new CssPreservedToken(new CssToken { Type = CssTokenType.Percentage, NumericValue = result.Value }),
                            var unit => Dimension(result.Value, unit),
                        });
                    }
                    else
                    {
                        copy.Value.Add(argument);
                    }
                }

                var parsed = CssColor.Parse(copy);
                return hasMathFunction && parsed is not null ? ClampLabChannels(parsed) : parsed;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-color-4/#parse-a-css-color-value without an element: parses the string as a &lt;color&gt;
    /// and resolves it to a used color. The values that depend on other properties use their initial values: currentcolor is
    /// opaque black, and the system colors and light-dark() use the light color scheme. Returns null on failure.
    /// </summary>
    internal static CssColor? ParseCssColorValue(string input)
    {
        return ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(input)) is [var value] ? ResolveColorWithoutElement(value) : null;

        static CssColor? ResolveColorWithoutElement(CssComponentValue value)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Ident } ident:
                {
                    var name = InfraAscii.ToAsciiLowercase(ident.Token.Value);
                    if (name == "currentcolor")
                        return ParseColor(LightSystemColors["canvastext"]);

                    if (name == "transparent" || CssColor.IsNamedColor(name))
                        return CssColor.Parse(value);

                    return LightSystemColors.TryGetValue(DeprecatedSystemColors.GetValueOrDefault(name, name), out var systemColor) ? ParseColor(systemColor) : null;
                }

                case CssPreservedToken { Type: CssTokenType.Hash }:
                    return CssColor.Parse(value);

                case CssFunction function:
                {
                    var name = InfraAscii.ToAsciiLowercase(function.Name);
                    if (name == "light-dark")
                        return SplitArguments(function.Value) is [[var light], [_]] ? ResolveColorWithoutElement(light) : null;

                    if (name == "contrast-color")
                        return ValueMatcher.RemoveWhitespace(function.Value) is [var background] && ResolveColorWithoutElement(background) is { } backgroundColor ? ComputeContrastColor(backgroundColor) : null;

                    if (name == "color-mix")
                        return CssColorMix.Compute(function, ResolveColorWithoutElement, math => CssCalc.Evaluate(math, CssCalcContext.Default) is { IsNumeric: true, Unit: "%" } result ? result.Value : null);

                    // https://drafts.csswg.org/css-color-hdr-1/#headroom-interpolation: the color of an SDR display (a headroom of 0)
                    if (name == "color-hdr")
                    {
                        if (!CssHdrColor.IsValid(function) || CssHdrColor.GetStops(function) is not [var first, var second]
                            || ResolveColorWithoutElement(first.Color) is not { } firstColor || ResolveColorWithoutElement(second.Color) is not { } secondColor
                            || CssHdrColor.EvaluateHeadroom(first.Headroom, CssCalcContext.Default) is not { } firstHeadroom || CssHdrColor.EvaluateHeadroom(second.Headroom, CssCalcContext.Default) is not { } secondHeadroom)
                        {
                            return null;
                        }

                        return CssColorConversion.InterpolateByHeadroom(firstColor, firstHeadroom, secondColor, secondHeadroom, headroom: 0);
                    }

                    if (!IsColorFunction(name))
                        return null;

                    if (CssRelativeColor.IsRelativeColorSyntax(function))
                        return CssRelativeColorResolver.Compute(function, ResolveColorWithoutElement, math => CssCalc.Evaluate(math, CssCalcContext.Default));

                    return CssColor.Parse(function);
                }

                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-color-5/#contrast-color: white or black, whichever has the higher contrast with the color.
    /// The contrast is the WCAG 2.1 contrast ratio (https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio) of the relative
    /// luminance of the opaque color in sRGB; white wins a tie.
    /// </summary>
    private static CssColor? ComputeContrastColor(CssColor color)
    {
        if (CssColorConversion.FromColor(color) is not { } components)
            return null;

        var srgb = CssColorConversion.Convert(components, "srgb").Channels;
        static double Linear(double? value)
        {
            var channel = Math.Clamp(value ?? 0, 0, 1);
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Linear(srgb[0])) + (0.7152 * Linear(srgb[1])) + (0.0722 * Linear(srgb[2]));
        var contrastWithWhite = 1.05 / (luminance + 0.05);
        var contrastWithBlack = (luminance + 0.05) / 0.05;
        return CssColor.Parse(new CssPreservedToken(new CssToken { Type = CssTokenType.Ident, Value = contrastWithWhite >= contrastWithBlack ? "white" : "black" }));
    }

    /// <summary>
    /// The math functions are clamped to the range of their channel (https://drafts.csswg.org/css-values-4/#calc-range): the lightness
    /// of lab(), lch(), oklab() and oklch() to [0%, 100%] and the chroma to positive values (https://drafts.csswg.org/css-color-4/#specifying-lab-lch).
    /// </summary>
    private static CssColor ClampLabChannels(CssColor color)
    {
        if (color.Function is not ("lab" or "lch" or "oklab" or "oklch"))
            return color;

        var channels = (double?[])color.Channels.Clone();
        channels[0] = channels[0] is { } lightness ? Math.Clamp(lightness, 0, color.Function.StartsWith("ok", StringComparison.Ordinal) ? 1 : 100) : null;
        if (color.Function is "lch" or "oklch" && channels[1] < 0)
        {
            channels[1] = 0;
        }

        return CssColor.Create(color.Function, channels, color.Alpha);
    }

    /// <summary>Parses a computed color (e.g. the computed value of the color property).</summary>
    private static CssColor? ParseColor(string value) => ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is [var single] ? CssColor.Parse(single) : null;

    private static string GetInheritedColor(ComputedStyle style) => style.Parent?.GetComputedValue("color") ?? LightSystemColors["canvastext"];

    /// <summary>Returns the used color scheme of the element (https://drafts.csswg.org/css-color-adjust/#color-scheme-prop).</summary>
    /// <summary>Returns the value of a system color (e.g. <c>canvas</c>, lowercase) for the used color scheme of an element (https://drafts.csswg.org/css-color-adjust-1/#color-scheme-effect).</summary>
    internal static string GetSystemColor(ComputedStyle style, string name) => GetSystemColors(style)[name];

    /// <summary>
    /// Returns the values of the system colors of an element: the palette of the forced colors mode
    /// (https://drafts.csswg.org/css-color-adjust-1/#forced), or the colors of the used color scheme of the element.
    /// </summary>
    private static FrozenDictionary<string, string> GetSystemColors(ComputedStyle style)
        => style.Environment.ForcedColorsPalette?.Colors ?? (GetUsedColorScheme(style) == "dark" ? DarkSystemColors : LightSystemColors);

    /// <summary>
    /// Returns the system color (https://drafts.csswg.org/css-color-4/#css-system-colors) that a lowercase keyword names, or
    /// null when it is not a system color: the keyword, or the system color that a deprecated system color maps to.
    /// </summary>
    internal static string? GetSystemColorKeyword(string name)
    {
        name = DeprecatedSystemColors.GetValueOrDefault(name, name);
        return LightSystemColors.ContainsKey(name) ? name : null;
    }

    internal static string GetUsedColorScheme(ComputedStyle style)
    {
        // https://drafts.csswg.org/css-color-adjust-1/#valdef-color-scheme-normal: "the element supports the page's supported
        // color schemes, if they are set, and that it supports no color schemes at all otherwise"
        var value = style.GetComputedValue("color-scheme");
        if (value == "normal" || value.Length == 0)
        {
            if (style.Environment.PageColorSchemes is not { } pageColorSchemes)
                return "light";

            value = pageColorSchemes;
        }

        var preferred = style.Environment.PrefersColorScheme;
        var keywords = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (Array.IndexOf(keywords, preferred) >= 0)
            return preferred;

        if (Array.IndexOf(keywords, "light") >= 0)
            return "light";

        return Array.IndexOf(keywords, "dark") >= 0 ? "dark" : "light";
    }

    /// <summary>
    /// Resolves the currentcolor keywords of a computed value (https://drafts.csswg.org/cssom/#resolved-value): the colors that
    /// depend on currentcolor (color-mix() and the relative colors) are computed with the color.
    /// </summary>
    public static string ResolveCurrentColor(ComputedStyle style, string property, string value, string color)
    {
        var replaced = ReplaceCurrentColor(value, color);
        if (value.Contains("color-mix(", StringComparison.Ordinal) || value.Contains("contrast-color(", StringComparison.Ordinal) || value.Contains("(from ", StringComparison.Ordinal))
            return ComputeGeneric(style, CssPropertyRegistry.Get(property), replaced, baseUrl: null, hasColors: true);

        return replaced;
    }

    /// <summary>Replaces the currentcolor keywords of a computed value with the color (https://drafts.csswg.org/cssom/#resolved-value).</summary>
    public static string ReplaceCurrentColor(string value, string color)
    {
        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (true)
        {
            var next = value.IndexOf("currentcolor", index, StringComparison.Ordinal);
            if (next < 0)
                break;

            var end = next + "currentcolor".Length;
            var isKeyword = (next == 0 || !IsNameCharacter(value[next - 1])) && (end == value.Length || !IsNameCharacter(value[end]));
            builder.Append(value, index, next - index).Append(isKeyword ? color : "currentcolor");
            index = end;
        }

        return builder.Append(value, index, value.Length - index).ToString();
    }

    private static bool IsNameCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';

    /// <summary>Returns whether the grammar of the property contains colors (the keywords of the value are then computed as colors).</summary>
    private static bool HasColorType(CssPropertyDefinition property) => ColorProperties.GetOrAdd(property.Name, static (_, property) =>
    {
        var grammar = CssPropertyRegistry.GetGrammar(property);
        return grammar is not null && ContainsType(grammar, "color", new HashSet<string>(StringComparer.Ordinal));
    }, property);

    internal static bool ContainsType(GrammarNode node, string typeName, HashSet<string> visited)
    {
        switch (node)
        {
            case GroupNode group:
                foreach (var child in group.Children)
                {
                    if (ContainsType(child, typeName, visited))
                        return true;
                }

                return false;

            case FunctionNode function:
                return function.Arguments is not null && ContainsType(function.Arguments, typeName, visited);

            case TypeNode type:
            {
                if (!type.IsProperty && type.Name == typeName)
                    return true;

                var key = type.IsProperty ? "'" + type.Name : type.Name;
                if (!visited.Add(key))
                    return false;

                var grammar = type.IsProperty
                    ? CssPropertyRegistry.Get(type.Name) is { } property ? CssPropertyRegistry.GetGrammar(property) : null
                    : CssPropertyRegistry.GetTypeGrammar(type.Name.EndsWith("()", StringComparison.Ordinal) ? type.Name : "<" + type.Name + ">");
                return grammar is not null && ContainsType(grammar, typeName, visited);
            }

            default:
                return false;
        }
    }

    /// <summary>Splits the arguments of a function on the top-level commas (the whitespace is removed).</summary>
    internal static List<List<CssComponentValue>> SplitArguments(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add([]);
            }
            else if (value is not CssPreservedToken { Type: CssTokenType.Whitespace })
            {
                result[^1].Add(value);
            }
        }

        return result;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------------------------

    public static string SerializeNumber(double value) => CssSerializer.SerializeNumber(value);

    /// <summary>
    /// Converts the numbers of a computed stroke-dasharray to absolute lengths
    /// (https://svgwg.org/svg2-draft/painting.html#StrokeDasharrayProperty).
    /// </summary>
    private static string ComputeStrokeDasharray(string value)
    {
        if (!value.Contains(',', StringComparison.Ordinal))
            return TryParseNumber(value, out var single) ? FormatPixels(single) : value;

        var items = value.Split(',', StringSplitOptions.TrimEntries);
        for (var i = 0; i < items.Length; i++)
        {
            if (TryParseNumber(items[i], out var number))
            {
                items[i] = FormatPixels(number);
            }
        }

        return string.Join(", ", items);
    }

    public static string FormatPixels(double value) => CssSerializer.SerializeNumber(value) + "px";

    /// <summary>Parses a computed length in px (e.g. "16px").</summary>
    public static double? ParsePixels(string value)
    {
        if (!value.EndsWith("px", StringComparison.Ordinal))
            return null;

        return double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    /// <summary>
    /// Computes <c>text-combine-upright: digits &lt;integer [2,4]&gt;?</c> (https://drafts.csswg.org/css-writing-modes-4/#valdef-text-combine-upright-digits-integer):
    /// the number of digits defaults to 2; as in the browsers, a math function whose result is outside of [2, 4] makes the
    /// declaration invalid (none).
    /// </summary>
    private static string ComputeTextCombineUprightDigits(ComputedStyle style, CssPropertyDefinition property, string value, string? baseUrl)
    {
        var argument = value.AsSpan("digits".Length).Trim();
        if (argument.IsEmpty)
            return "digits 2";

        var computed = ComputeGeneric(style, property, "digits " + argument.ToString(), baseUrl).AsSpan("digits".Length).Trim();
        return double.TryParse(computed, NumberStyles.Float, CultureInfo.InvariantCulture, out var digits) && digits is >= 2 and <= 4 && digits == Math.Floor(digits)
            ? "digits " + SerializeNumber(digits)
            : "none";
    }

    public static bool TryParseNumber(string value, out double result) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
