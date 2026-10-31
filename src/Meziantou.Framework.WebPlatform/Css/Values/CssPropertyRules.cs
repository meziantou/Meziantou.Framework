using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The properties whose values are not serialized from their grammar alone: the canonical form of the specified value of
/// some longhands (e.g. display: block flow is block), and the shorthands whose expansion is defined in prose (e.g.
/// line-clamp sets continue to collapse).
/// </summary>
internal static partial class CssPropertyRules
{
    private delegate string? LonghandRule(List<CssComponentValue> significant);

    private delegate Dictionary<string, string>? ExpandRule(CssPropertyDefinition shorthand, List<CssComponentValue> significant);

    private delegate string? SerializeRule(CssPropertyDefinition shorthand, Dictionary<string, string> lookup);

    private delegate bool ValidationRule(CssPropertyDefinition property, List<CssComponentValue> significant);

    // Declared before LonghandRules, which uses it
    private static readonly string[] OverflowClipMarginLonghands =
    [
        "overflow-clip-margin-top", "overflow-clip-margin-right", "overflow-clip-margin-bottom", "overflow-clip-margin-left",
        "overflow-clip-margin-block-start", "overflow-clip-margin-block-end", "overflow-clip-margin-inline-start", "overflow-clip-margin-inline-end",
    ];

    private static readonly Dictionary<string, LonghandRule> LonghandRules = CreateLonghandRules();

    private static readonly Dictionary<string, (ExpandRule Expand, SerializeRule Serialize)> ShorthandRules = CreateShorthandRules();

    /// <summary>The constraints of the values that are defined in prose (the value matches the grammar of the property).</summary>
    private static readonly Dictionary<string, ValidationRule> ValidationRules = new(StringComparer.Ordinal)
    {
        ["animation"] = IsValidAnimationValue,
        ["animation-name"] = IsValidAnimationValue,
        ["animation-timing-function"] = IsValidAnimationValue,
        ["transition"] = IsValidAnimationValue,
        ["transition-timing-function"] = IsValidAnimationValue,
        ["will-change"] = IsValidWillChange,
        ["view-transition-class"] = IsValidViewTransitionClass,
        ["cursor"] = IsValidCursor,
        ["list-style"] = IsValidSymbols,
        ["list-style-type"] = IsValidSymbols,
        ["content"] = IsValidSymbols,
        ["font-language-override"] = IsValidFontLanguageOverride,
        ["flex"] = IsValidFlex,
        ["-webkit-flex"] = IsValidFlex,
    };

    /// <summary>
    /// flex (https://drafts.csswg.org/css-flexbox-1/#flex-property): "A unitless zero that is not already preceded by two flex
    /// factors must be interpreted as a flex factor": the numbers are the flex factors, in order, and a third number is a
    /// flex-basis, which must be zero (<c>flex: 0 1 1</c> is invalid, it is not a flex-basis of zero followed by two factors).
    /// </summary>
    private static bool IsValidFlex(CssPropertyDefinition property, List<CssComponentValue> significant)
    {
        var numbers = 0;
        foreach (var value in significant)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Number } number && ++numbers == 3 && number.Token.NumericValue != 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The display values are serialized in their shortest form (see <see cref="CssDisplay.Canonicalize"/>). The name of a
    /// <c>layout()</c> function (https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers) is an identifier: its
    /// case is kept.
    /// </summary>
    private static string? CanonicalizeDisplay(List<CssComponentValue> significant)
    {
        foreach (var value in significant)
        {
            if (value is not CssFunction function || !function.Name.Equals("layout", StringComparison.OrdinalIgnoreCase))
                continue;

            if (ValueMatcher.RemoveWhitespace(function.Value) is not [CssPreservedToken { Type: CssTokenType.Ident } name])
                return null;

            var isInline = significant.Exists(static other => other is CssPreservedToken { Type: CssTokenType.Ident } keyword && keyword.Token.Value.Equals("inline", StringComparison.OrdinalIgnoreCase));
            return CssDisplay.SerializeLayoutFunction(name.Token.Value, isInline);
        }

        return CssDisplay.Canonicalize(InfraAscii.ToAsciiLowercase(Serialize(significant, separator: " ")));
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/painting.html#PaintOrder: the order is significant, and the value is serialized in its shortest
    /// form (the keywords at the end that are in the default order of the omitted ones are dropped, e.g. "stroke fill markers" is "stroke").
    /// </summary>
    private static string? CanonicalizePaintOrder(List<CssComponentValue> significant)
    {
        var keywords = new List<string>(3);
        foreach (var value in significant)
        {
            if (value is not CssPreservedToken { Type: CssTokenType.Ident } ident)
                return null;

            keywords.Add(InfraAscii.ToAsciiLowercase(ident.Token.Value));
        }

        if (keywords is ["normal"])
            return "normal";

        var order = GetPaintOrder(keywords);
        while (keywords.Count > 1 && GetPaintOrder(keywords.GetRange(0, keywords.Count - 1)).SequenceEqual(order))
        {
            keywords.RemoveAt(keywords.Count - 1);
        }

        return string.Join(' ', keywords);

        static List<string> GetPaintOrder(List<string> specified)
        {
            var result = new List<string>(specified);
            foreach (var keyword in (ReadOnlySpan<string>)["fill", "stroke", "markers"])
            {
                if (!result.Contains(keyword))
                {
                    result.Add(keyword);
                }
            }

            return result;
        }
    }

    private static Dictionary<string, LonghandRule> CreateLonghandRules()
    {
        var rules = new Dictionary<string, LonghandRule>(StringComparer.Ordinal)
        {
            ["display"] = CanonicalizeDisplay,
            ["paint-order"] = CanonicalizePaintOrder,
            ["view-transition-name"] = CanonicalizeViewTransitionName,
            ["filter"] = CanonicalizeFilter,
            ["text-emphasis-position"] = significant => CanonicalizeTextEmphasisPosition(InfraAscii.ToAsciiLowercase(Serialize(significant, separator: " "))),
            ["text-justify"] = CanonicalizeTextJustify,
            ["overflow-x"] = CanonicalizeOverflow,
            ["overflow-y"] = CanonicalizeOverflow,
            ["overflow-block"] = CanonicalizeOverflow,
            ["overflow-inline"] = CanonicalizeOverflow,
            ["hyphenate-limit-chars"] = CanonicalizeHyphenateLimitChars,
            ["ruby-overhang"] = CanonicalizeRubyOverhang,
            ["aspect-ratio"] = CanonicalizeAspectRatio,
            ["stroke-dasharray"] = CanonicalizeStrokeDasharray,
            ["text-box-edge"] = significant => CanonicalizeTextEdge(significant),
            ["text-decoration-inset"] = CanonicalizeTextDecorationInset,
            ["backdrop-filter"] = CanonicalizeFilter,
            ["animation-timing-function"] = CanonicalizeEasing,
            ["transition-timing-function"] = CanonicalizeEasing,
            ["animation-name"] = CanonicalizeAnimationName,
            ["animation-timeline"] = CanonicalizeAnimationTimeline,
            ["view-timeline-inset"] = CanonicalizeViewTimelineInset,
            ["animation-range-start"] = significant => CanonicalizeAnimationRange(CssPropertyRegistry.Get("animation-range-start")!, significant),
            ["animation-range-end"] = significant => CanonicalizeAnimationRange(CssPropertyRegistry.Get("animation-range-end")!, significant),
            ["mask-position"] = CanonicalizePositionList,
            ["object-position"] = CanonicalizePositionList,
            ["perspective-origin"] = CanonicalizePositionList,
            ["transform-origin"] = CanonicalizeTransformOrigin,
            ["scale"] = CanonicalizeScale,
            ["translate"] = CanonicalizeTranslate,
            ["rotate"] = CanonicalizeRotate,

            // https://drafts.csswg.org/css-images-4/#the-object-fit: contain is the default fit of scale-down, which is omitted
            ["object-fit"] = significant => InfraAscii.ToAsciiLowercase(Serialize(significant, separator: " ")) switch
            {
                "contain scale-down" or "scale-down contain" => "scale-down",
                "scale-down cover" => "cover scale-down",
                var value => value,
            },

            // https://drafts.csswg.org/css-box-4/#margin-trim: "Following the shortest-serialization principle, computed values
            // equivalent to block will serialize as that keyword"
            ["margin-trim"] = significant => InfraAscii.ToAsciiLowercase(Serialize(significant, separator: " ")) switch
            {
                "block-start block-end" or "block-end block-start" => "block",
                var value => value,
            },

            // https://drafts.csswg.org/css-flexbox-2/#flex-wrap-property: balance "behaves as if wrap was specified": wrap is
            // omitted next to it (the shortest serialization), and wrap-reverse precedes it (the order of the grammar)
            ["flex-wrap"] = significant => InfraAscii.ToAsciiLowercase(Serialize(significant, separator: " ")) switch
            {
                "wrap balance" or "balance wrap" => "balance",
                "balance wrap-reverse" => "wrap-reverse balance",
                _ => null,
            },
            ["background-size"] = significant => CanonicalizeBgSize("background-size", significant),
            ["mask-size"] = significant => CanonicalizeBgSize("mask-size", significant),
            ["offset-position"] = CanonicalizePositionList,
            ["offset-anchor"] = CanonicalizePositionList,
            ["offset-rotate"] = CanonicalizeOffsetRotate,
            ["box-shadow"] = CanonicalizeBoxShadow,
            ["cursor"] = CanonicalizeCursor,
            ["border-image-repeat"] = CanonicalizeBorderImageRepeat,
            ["border-image-slice"] = CanonicalizeBorderImageSides,
            ["border-image-width"] = CanonicalizeBorderImageSides,
            ["border-image-outset"] = CanonicalizeBorderImageSides,
            ["clip"] = CanonicalizeClip,
            ["position-area"] = significant => CanonicalizePositionAreas(CssPropertyRegistry.Get("position-area")!, significant),
            ["position-try-fallbacks"] = significant => CanonicalizePositionAreas(CssPropertyRegistry.Get("position-try-fallbacks")!, significant),
            ["position-visibility"] = CanonicalizePositionVisibility,
            ["alignment-baseline"] = CanonicalizeLegacyBaseline,
            ["dominant-baseline"] = CanonicalizeLegacyBaseline,
            ["counter-increment"] = significant => CanonicalizeCounters(significant, defaultValue: "1"),
            ["counter-reset"] = significant => CanonicalizeCounters(significant, defaultValue: "0"),
            ["counter-set"] = significant => CanonicalizeCounters(significant, defaultValue: "0"),
            ["scroll-snap-type"] = CanonicalizeScrollSnapType,
            ["scroll-snap-align"] = CanonicalizeScrollSnapAlign,
        };

        foreach (var name in (string[])["clip-path", "shape-outside", "offset-path"])
        {
            rules[name] = significant => SerializeShapeValue(name, significant, computed: false);
        }

        rules["border-shape"] = SerializeBorderShape;
        foreach (var side in OverflowClipMarginLonghands)
        {
            rules[side] = significant => CanonicalizeOverflowClipMargin(Serialize(significant, separator: " "));
        }

        foreach (var name in AlignmentLonghands)
        {
            rules[name] = CanonicalizeAlignment;
        }

        return rules;
    }

    private static Dictionary<string, (ExpandRule Expand, SerializeRule Serialize)> CreateShorthandRules() => new(StringComparer.Ordinal)
    {
        ["line-clamp"] = (ExpandLineClamp, SerializeLineClamp),
        ["-webkit-line-clamp"] = (ExpandWebkitLineClamp, SerializeWebkitLineClamp),
        ["place-content"] = (ExpandPlace, SerializePair),
        ["place-items"] = (ExpandPlace, SerializePair),
        ["place-self"] = (ExpandPlace, SerializePair),
        ["gap"] = (ExpandPlace, SerializePair),

        // https://drafts.csswg.org/css-sizing-4/#sizing-values: the height is the width when it is omitted
        ["size"] = (ExpandPlace, SerializePair),
        ["white-space"] = (ExpandWhiteSpace, SerializeWhiteSpace),
        ["text-box"] = (ExpandTextBox, SerializeTextBox),
        ["vertical-align"] = (ExpandVerticalAlign, SerializeVerticalAlign),
        ["overflow-clip-margin"] = (ExpandSameValue, SerializeSameValue),

        // https://svgwg.org/svg2-draft/painting.html#MarkerShorthand: marker sets marker-start, marker-mid and marker-end
        ["marker"] = (ExpandMarker, SerializeSameValue),
        ["overflow-clip-margin-block"] = (ExpandSameValue, SerializeSameValue),
        ["overflow-clip-margin-inline"] = (ExpandSameValue, SerializeSameValue),
        ["animation"] = (ExpandAnimation, SerializeAnimation),
        ["animation-range"] = (ExpandAnimationRange, SerializeAnimationRange),
        ["scroll-timeline"] = (ExpandScrollTimeline, SerializeScrollTimeline),
        ["view-timeline"] = (ExpandViewTimeline, SerializeViewTimeline),
        ["transition"] = (ExpandTransition, SerializeTransition),
        ["mask"] = (ExpandMask, SerializeMask),
        ["border-radius"] = (ExpandBorderRadius, SerializeBorderRadius),
        ["border-top-radius"] = (ExpandBorderRadius, SerializeBorderRadius),
        ["border-right-radius"] = (ExpandBorderRadius, SerializeBorderRadius),
        ["border-bottom-radius"] = (ExpandBorderRadius, SerializeBorderRadius),
        ["border-left-radius"] = (ExpandBorderRadius, SerializeBorderRadius),
        ["border-block-start-radius"] = (ExpandBorderRadius, SerializeBorderRadius),
        ["border-block-end-radius"] = (ExpandBorderRadius, SerializeBorderRadius),
        ["border-inline-start-radius"] = (ExpandBorderRadius, SerializeBorderRadius),
        ["border-inline-end-radius"] = (ExpandBorderRadius, SerializeBorderRadius),
        ["corner"] = (ExpandCorner, SerializeCorner),
        ["corner-top"] = (ExpandCorner, SerializeCorner),
        ["corner-right"] = (ExpandCorner, SerializeCorner),
        ["corner-bottom"] = (ExpandCorner, SerializeCorner),
        ["corner-left"] = (ExpandCorner, SerializeCorner),
        ["corner-block-start"] = (ExpandCorner, SerializeCorner),
        ["corner-block-end"] = (ExpandCorner, SerializeCorner),
        ["corner-inline-start"] = (ExpandCorner, SerializeCorner),
        ["corner-inline-end"] = (ExpandCorner, SerializeCorner),
        ["corner-top-left"] = (ExpandCorner, SerializeCorner),
        ["corner-top-right"] = (ExpandCorner, SerializeCorner),
        ["corner-bottom-right"] = (ExpandCorner, SerializeCorner),
        ["corner-bottom-left"] = (ExpandCorner, SerializeCorner),
        ["corner-start-start"] = (ExpandCorner, SerializeCorner),
        ["corner-start-end"] = (ExpandCorner, SerializeCorner),
        ["corner-end-start"] = (ExpandCorner, SerializeCorner),
        ["corner-end-end"] = (ExpandCorner, SerializeCorner),
        ["border-image"] = (ExpandBorderImage, SerializeBorderImage),
        ["list-style"] = (ExpandListStyle, SerializeListStyle),
        ["font-synthesis"] = (ExpandFontSynthesis, SerializeFontSynthesis),
        ["page-break-before"] = (ExpandLegacyPageBreak, SerializeLegacyPageBreak),
        ["page-break-after"] = (ExpandLegacyPageBreak, SerializeLegacyPageBreak),
        ["page-break-inside"] = (ExpandLegacyPageBreak, SerializeLegacyPageBreak),
    };

    /// <summary>
    /// Returns the specified value of a longhand in its canonical form, or null when its serialization follows the grammar.
    /// The value is valid (it matches the grammar of the property).
    /// </summary>
    public static string? CanonicalizeLonghand(CssPropertyDefinition property, List<CssComponentValue> significant)
    {
        if (LonghandRules.TryGetValue(property.Name, out var rule))
            return rule(significant);

        // https://drafts.csswg.org/css-anchor-position-1/#anchor-pos: an anchor function is serialized in its canonical form
        if (significant is [CssFunction function] && CssCalc.GetAnchorFunction(function.Name) is not null)
            return CssAnchorFunction.Serialize(function);

        return null;
    }

    /// <summary>Returns whether a value that matches the grammar of the property satisfies the constraints defined in prose (e.g. steps(1, jump-none) is invalid).</summary>
    public static bool IsValid(CssPropertyDefinition property, List<CssComponentValue> significant) => !ValidationRules.TryGetValue(property.Name, out var rule) || rule(property, significant);

    /// <summary>Returns whether the expansion and the serialization of the shorthand are defined by these rules.</summary>
    public static bool IsShorthand(string name) => ShorthandRules.ContainsKey(name);

    /// <summary>Expands a valid value of a shorthand of <see cref="IsShorthand"/>; returns null when it cannot be expanded.</summary>
    public static List<CssLonghandValue>? Expand(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        if (ShorthandRules[shorthand.Name].Expand(shorthand, significant) is not { } values)
            return null;

        var result = new List<CssLonghandValue>();
        foreach (var longhand in CssPropertyRegistry.GetLonghandsRecursive(shorthand))
        {
            if (!values.TryGetValue(longhand, out var value))
                return null;

            result.Add(new CssLonghandValue(longhand, value));
        }

        return result;
    }

    /// <summary>Serializes a shorthand of <see cref="IsShorthand"/> from the values of its longhands; returns null when they cannot be represented.</summary>
    public static string? Serialize(CssPropertyDefinition shorthand, Dictionary<string, string> lookup) => ShorthandRules[shorthand.Name].Serialize(shorthand, lookup);

    /// <summary>https://drafts.csswg.org/css-view-transitions-2/#view-transition-class-prop: <c>none</c> is not a class (<c>none | &lt;custom-ident&gt;+</c>).</summary>
    private static bool IsValidViewTransitionClass(CssPropertyDefinition property, List<CssComponentValue> significant)
        => significant.Count <= 1 || !significant.Exists(static value => value is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent("none"));

    /// <summary>https://drafts.csswg.org/css-view-transitions-2/#view-transition-name-prop: the keywords are serialized in lowercase (the names are case-sensitive).</summary>
    private static string? CanonicalizeViewTransitionName(List<CssComponentValue> significant)
        => significant is [CssPreservedToken { Type: CssTokenType.Ident } ident] && InfraAscii.ToAsciiLowercase(ident.Token.Value) is ("none" or "auto" or "match-element") and var keyword ? keyword : null;

    /// <summary>
    /// aspect-ratio (https://drafts.csswg.org/css-sizing-4/#aspect-ratio): <c>auto</c> first, and a &lt;ratio&gt; of a single number
    /// is serialized with its second number (https://drafts.csswg.org/css-values-4/#ratio-value: "If the second &lt;number&gt; is
    /// omitted, it defaults to 1"), as in the browsers (<c>16</c> is <c>16 / 1</c>).
    /// </summary>
    private static string? CanonicalizeAspectRatio(List<CssComponentValue> significant)
    {
        var hasAuto = false;
        var numbers = new List<CssComponentValue>(2);
        foreach (var value in significant)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent("auto"))
            {
                hasAuto = true;
            }
            else if (!(value is CssPreservedToken { Type: CssTokenType.Delim } delim && delim.Token.IsDelim('/')))
            {
                numbers.Add(value);
            }
        }

        if (numbers.Count == 0)
            return null;

        var ratio = Serialize(numbers[..1], " ") + " / " + (numbers.Count > 1 ? Serialize(numbers[1..2], " ") : "1");
        return hasAuto ? "auto " + ratio : ratio;
    }

    /// <summary>
    /// stroke-dasharray (https://svgwg.org/svg2-draft/painting.html#StrokeDasharrayProperty): the items of the list are separated by
    /// commas, as in the browsers (<c>10px 20%</c> is <c>10px, 20%</c>).
    /// </summary>
    private static string? CanonicalizeStrokeDasharray(List<CssComponentValue> significant)
    {
        if (significant.Count < 2)
            return null;

        var items = new List<CssComponentValue>(significant.Count);
        foreach (var value in significant)
        {
            if (!(value is CssPreservedToken { Type: CssTokenType.Comma }))
            {
                items.Add(value);
            }
        }

        return Serialize(items, ", ");
    }

    private static string Serialize(List<CssComponentValue> significant, string separator) => string.Join(separator, significant.Select(value => CssSerializer.SerializeValue([value], property: null)));

    private static bool IsIdent(CssComponentValue value, string keyword) => value is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.IsIdent(keyword);

    /// <summary>https://svgwg.org/svg2-draft/painting.html#MarkerShorthand: none or a url() for the three marker properties.</summary>
    private static Dictionary<string, string>? ExpandMarker(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var value = CssSerializer.SerializeValue(significant, shorthand);
        return CssPropertyRegistry.GetLonghandsRecursive(shorthand).ToDictionary(longhand => longhand, _ => value, StringComparer.Ordinal);
    }

    /// <summary>All the longhands are set to the canonical value of the shorthand (e.g. overflow-clip-margin sets the 4 sides).</summary>
    private static Dictionary<string, string>? ExpandSameValue(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var longhands = CssPropertyRegistry.GetLonghandsRecursive(shorthand);
        if (longhands.Count == 0 || CssPropertyRegistry.Get(longhands[0]) is not { } first || CanonicalizeLonghand(first, significant) is not { } value)
            return null;

        return longhands.ToDictionary(longhand => longhand, _ => value, StringComparer.Ordinal);
    }

    private static string? SerializeSameValue(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        string? common = null;
        foreach (var longhand in CssPropertyRegistry.GetLonghandsRecursive(shorthand))
        {
            if (!lookup.TryGetValue(longhand, out var value) || (common is not null && common != value))
                return null;

            common = value;
        }

        return common;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Overflow (https://drafts.csswg.org/css-overflow-4/)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-overflow-4/#overflow-clip-margin: &lt;visual-box&gt; || &lt;length&gt; is serialized
    /// with the box first, without the default padding-box nor a zero length (e.g. 10px content-box is content-box 10px,
    /// padding-box is 0px). The value is a serialized specified or computed value.
    /// </summary>
    public static string CanonicalizeOverflowClipMargin(string value)
    {
        string? box = null;
        string? length = null;
        foreach (var component in Cascade.CssValueComputer.SplitComponents(value))
        {
            if (component is "content-box" or "padding-box" or "border-box")
            {
                box = component;
            }
            else
            {
                length = component;
            }
        }

        var parts = new List<string>(2);
        if (box is not null and not "padding-box")
        {
            parts.Add(box);
        }

        if (length is not (null or "0" or "0px"))
        {
            parts.Add(length);
        }

        return parts.Count == 0 ? "0px" : string.Join(' ', parts);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-overflow-4/#propdef-line-clamp: none resets the longhands; otherwise the omitted maximum
    /// number of lines is auto, the omitted block ellipsis is ellipsis, and continue is collapse (-webkit-legacy with the
    /// -webkit-legacy keyword).
    /// </summary>
    private static Dictionary<string, string>? ExpandLineClamp(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        if (significant is [var none] && IsIdent(none, "none"))
            return LineClampNone();

        string? count = null;
        var auto = false;
        string? ellipsis = null;
        var legacy = false;
        foreach (var value in significant)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Number }:
                    count = CssSerializer.SerializeValue([value], property: null);
                    break;
                case CssFunction:
                    // A math function (an <integer>)
                    count = CssSerializer.SerializeValue([value], property: null);
                    break;
                case var keyword when IsIdent(keyword, "auto"):
                    auto = true;
                    break;
                case var keyword when IsIdent(keyword, "-webkit-legacy"):
                    legacy = true;
                    break;
                default:
                    ellipsis = value is CssPreservedToken { Type: CssTokenType.Ident } ident ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : CssSerializer.SerializeValue([value], property: null);
                    break;
            }
        }

        var maxLines = count is null ? "auto" : auto ? count + " auto" : count;
        return new(StringComparer.Ordinal)
        {
            ["max-lines"] = maxLines,
            ["block-ellipsis"] = ellipsis ?? "ellipsis",
            ["continue"] = legacy ? "-webkit-legacy" : "collapse",
        };
    }

    private static Dictionary<string, string> LineClampNone() => new(StringComparer.Ordinal)
    {
        ["max-lines"] = "auto",
        ["block-ellipsis"] = "no-ellipsis",
        ["continue"] = "normal",
    };

    private static string? SerializeLineClamp(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue("max-lines", out var maxLines) || !lookup.TryGetValue("block-ellipsis", out var ellipsis) || !lookup.TryGetValue("continue", out var @continue))
            return null;

        if (@continue == "normal")
            return maxLines == "auto" && ellipsis == "no-ellipsis" ? "none" : null;

        if (@continue is not ("collapse" or "-webkit-legacy"))
            return null;

        var result = maxLines;
        if (ellipsis != "ellipsis")
        {
            result += " " + ellipsis;
        }

        return @continue == "-webkit-legacy" ? result + " -webkit-legacy" : result;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-overflow-4/#webkit-line-clamp: like line-clamp, an integer sets the block ellipsis to
    /// ellipsis and continue to -webkit-legacy.
    /// </summary>
    private static Dictionary<string, string>? ExpandWebkitLineClamp(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        if (significant is not [var value])
            return null;

        if (IsIdent(value, "none"))
            return LineClampNone();

        return new(StringComparer.Ordinal)
        {
            ["max-lines"] = CssSerializer.SerializeValue([value], property: null),
            ["block-ellipsis"] = "ellipsis",
            ["continue"] = "-webkit-legacy",
        };
    }

    private static string? SerializeWebkitLineClamp(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue("max-lines", out var maxLines) || !lookup.TryGetValue("block-ellipsis", out var ellipsis) || !lookup.TryGetValue("continue", out var @continue))
            return null;

        if (@continue == "normal")
            return maxLines == "auto" && ellipsis == "no-ellipsis" ? "none" : null;

        return ellipsis == "ellipsis" && @continue == "-webkit-legacy" && !maxLines.Contains("auto", StringComparison.Ordinal) ? maxLines : null;
    }
}
