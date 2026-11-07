using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// Forced colors mode (https://drafts.csswg.org/css-color-adjust-1/#forced): when the mode is active (see
// MediaEnvironment.ForcedColors) and forced-color-adjust is auto on an element, a few properties compute to another value
// (box-shadow and text-shadow to none, color-scheme to light dark...), and the colors of the element are forced to system
// colors at used-value time: the computed values are the ones of the cascade (they are inherited, and exposed by
// computedStyleMap()), the used values are returned by GetUsedValue (the painting) and by GetResolvedValue for the properties
// whose resolved value is the used value (getComputedStyle()).
internal sealed partial class ComputedStyle
{
    /// <summary>How the colors of a property are forced to system colors in forced colors mode.</summary>
    private enum ForcedColorsKind : byte
    {
        /// <summary>The property is not affected by the forced colors mode.</summary>
        None,

        /// <summary>The <c>color</c> property: the color that would result from an empty author style sheet.</summary>
        Color,

        /// <summary>The <c>background-color</c> property: the system color paired with the forced color, with the alpha of the original color.</summary>
        Background,

        /// <summary>The border and outline colors: the system color of the user agent style sheet, or else the forced color.</summary>
        Border,

        /// <summary>The other colors: the forced color (as <c>currentcolor</c>).</summary>
        CurrentColor,
    }

    /// <summary>
    /// Returns how the colors of a longhand are forced ("Properties Affected by Forced Colors Mode",
    /// https://drafts.csswg.org/css-color-adjust-1/#forced-colors-properties). <c>accent-color</c> and <c>scrollbar-color</c>
    /// compute to <c>auto</c> instead (see <see cref="AdjustForForcedColors"/>); the colors of the text
    /// (<c>-webkit-text-fill-color</c> and <c>-webkit-text-stroke-color</c>) are forced too, as in Chromium ("this is the list of
    /// color properties existing at the time of writing, but it may not be an exhaustive list").
    /// </summary>
    private static ForcedColorsKind GetForcedColorsKind(string property) => property switch
    {
        "color" => ForcedColorsKind.Color,
        "background-color" => ForcedColorsKind.Background,
        "border-top-color" or "border-right-color" or "border-bottom-color" or "border-left-color" or "outline-color" => ForcedColorsKind.Border,
        "caret-color" or "column-rule-color" or "row-rule-color" or "fill" or "stroke" or "flood-color" or "lighting-color" or "stop-color"
            or "text-decoration-color" or "text-emphasis-color" or "-webkit-text-fill-color" or "-webkit-text-stroke-color" => ForcedColorsKind.CurrentColor,
        _ => ForcedColorsKind.None,
    };

    /// <summary>
    /// Whether the resolved value of a color property is its used value (https://drafts.csswg.org/cssom/#resolved-values):
    /// getComputedStyle() returns the forced colors of these properties, and the computed values of the other ones.
    /// </summary>
    private static bool IsResolvedAsUsedColor(string property)
        => property is "color" or "background-color" or "border-top-color" or "border-right-color" or "border-bottom-color" or "border-left-color" or "outline-color" or "caret-color";

    // 0: not computed yet, 1: the colors are forced, -1: they are not
    private sbyte _forcedColorsAdjusted;

    /// <summary>The state of a style in forced colors mode.</summary>
    private sealed class ForcedColorsData
    {
        /// <summary>The style of the element computed from the declarations of the user agent origin only.</summary>
        public ComputedStyle? UserAgentStyle;

        /// <summary>The color of the element in a document without author style sheet, and its system color keyword.</summary>
        public (string Keyword, string Value)? UserAgentColor;

        /// <summary>The used value of <c>color</c>.</summary>
        public string? UsedColor;
    }

    /// <summary>
    /// Whether the colors of the style are forced: the forced colors mode is active and <c>forced-color-adjust</c> is <c>auto</c>
    /// on the element. A highlight pseudo-element "must honor any forced colors mode applied to its originating element"
    /// (https://drafts.csswg.org/css-pseudo-4/#highlight-styling: forced-color-adjust does not apply to it, so its computed
    /// value is the one of the originating element).
    /// </summary>
    internal bool IsForcedColorsAdjusted
    {
        get
        {
            if (_forcedColorsAdjusted == 0)
            {
                _forcedColorsAdjusted = Environment.ForcedColors && GetComputedValue("forced-color-adjust") is "auto" or "" ? (sbyte)1 : (sbyte)-1;
            }

            return _forcedColorsAdjusted > 0;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Computed values
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the computed value of a property in forced colors mode
    /// (https://drafts.csswg.org/css-color-adjust-1/#forced-colors-properties). When forced-color-adjust is auto:
    /// "box-shadow and text-shadow compute to none; background-image computes to none unless the original value contains a
    /// url() function; color-scheme computes to light dark; scrollbar-color computes to auto; accent-color computes to auto"
    /// (unless it is a system color, as in Chromium), and font-variant-emoji: normal and unicode compute to text. With
    /// forced-color-adjust: preserve-parent-color, "if the color property inherits from its parent [...], then it computes to
    /// the used color of its parent's color value".
    /// </summary>
    private string AdjustForForcedColors(string property, string value)
    {
        switch (property)
        {
            case "color":
                // (a highlight pseudo-element inherits its color from the highlight pseudo-element of the parent element)
                if (!IsHighlight && Parent is { } parent && GetComputedValue("forced-color-adjust") == "preserve-parent-color" && InheritsColor())
                    return parent.GetUsedColor();

                return value;

            case "box-shadow" or "text-shadow":
                return value != "none" && IsForcedColorsAdjusted ? "none" : value;

            case "color-scheme":
                return IsForcedColorsAdjusted ? "light dark" : value;

            case "scrollbar-color":
                return value != "auto" && IsForcedColorsAdjusted ? "auto" : value;

            case "accent-color":
                return value != "auto" && IsForcedColorsAdjusted && !HasSystemColor(property, value) ? "auto" : value;

            case "background-image":
                return value != "none" && IsForcedColorsAdjusted && !value.Contains("url(", StringComparison.Ordinal) ? "none" : value;

            case "font-variant-emoji":
                return value is "normal" or "unicode" && IsForcedColorsAdjusted ? "text" : value;

            default:
                return value;
        }
    }

    /// <summary>
    /// Whether the color property inherits from the parent: "there is no cascaded value or the cascaded value is currentColor,
    /// inherit, or another keyword that inherits from the parent".
    /// </summary>
    private bool InheritsColor()
    {
        if (IsAnimatedByAnimations("color"))
            return false;

        return GetDeclaredKeyword("color") is null or "currentcolor" or "inherit" or "unset" or "revert" or "revert-layer" or "revert-rule";
    }

    /// <summary>
    /// Returns the cascaded value of a property after the substitution of its arbitrary substitution functions (e.g. var()), in
    /// lowercase, or null when the property has no cascaded value. A value that is invalid at computed-value time is <c>unset</c>.
    /// </summary>
    private string? GetDeclaredKeyword(string property)
    {
        if (!TryGetCascadedValue(property, out var cascaded))
            return null;

        var declared = cascaded.Value;
        string? text = declared.ComputationValue ?? declared.Value;
        if (declared.ShorthandName is { } shorthand)
        {
            using var scope = CssSubstitution.BeginPropertyComputation(property);
            text = SubstituteShorthand(shorthand, CssRandom.AssignIfConditionCachingKeys(shorthand, declared.ShorthandValue ?? ""), declared.Name, GetSubstitutionContext(cascaded));
        }
        else if (CssSubstitution.MayContainSubstitution(text))
        {
            using var scope = CssSubstitution.BeginPropertyComputation(property);
            text = SubstituteLonghand(declared.Name, CssRandom.AssignIfConditionCachingKeys(declared.Name, text), GetSubstitutionContext(cascaded));
        }

        return text is null ? "unset" : InfraAscii.ToAsciiLowercase(text.Trim());
    }

    /// <summary>
    /// Returns whether the computed value of a color property is a system color ("if its computed value is a color other than a
    /// system color, its used value is instead forced to a system color"): the declaration that the value comes from (the
    /// cascaded value of the element, or of the ancestor that the value is inherited from) is a system color keyword, whose
    /// color is the computed value (an animated value is not a system color).
    /// </summary>
    private bool HasSystemColor(string property, string computedValue)
    {
        if (CssPropertyRegistry.Get(property) is not { } definition)
            return false;

        for (var style = this; ;)
        {
            var keyword = style.GetDeclaredKeyword(property);
            if (keyword is null or "unset" or "revert" or "revert-layer" or "revert-rule")
            {
                keyword = definition.Inherited || style.IsHighlight ? "inherit" : "initial";
            }

            if (keyword == "inherit" && style.GetInheritanceSource(property) is { } source)
            {
                style = source;
                continue;
            }

            if (keyword is "inherit" or "initial")
            {
                // The initial value of color is the CanvasText system color
                keyword = property == "color" && !style.IsHighlight ? "canvastext" : "";
            }

            return CssValueComputer.GetSystemColorKeyword(keyword) is { } systemColor && computedValue == CssValueComputer.GetSystemColor(style, systemColor);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Used values
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the used value of a color property for the painting: the resolved value (<see cref="GetResolvedValue(string)"/>),
    /// whose colors are forced to system colors in forced colors mode.
    /// </summary>
    /// <remarks>
    /// In a visited link, the colors are the ones of the visited style (see <see cref="GetVisitedDependentValue"/>), and
    /// <c>currentcolor</c> is the painted color of the element in the other properties (e.g. <c>stop-color</c>).
    /// </remarks>
    internal string GetUsedValue(string property)
    {
        var value = GetResolvedValue(property, forceColors: Environment.ForcedColors);
        if (VisitedLink is null || UnvisitedStyle is not null)
            return value;

        var physical = LogicalProperties.GetPhysicalProperty(property, this) ?? property;
        if (VisitedProperties.Contains(physical))
            return GetVisitedDependentValue(physical, value);

        return GetComputedValue(property).Contains("currentcolor", StringComparison.Ordinal) ? VisitedOrSelf.GetResolvedValue(property, forceColors: Environment.ForcedColors) : value;
    }

    /// <summary>
    /// Returns the used background color of the canvas, which is the one of the style (of the root element, or of the body
    /// element whose background is propagated). "UAs must propagate the forced-color-adjust value set on the root element to the
    /// document viewport (where it can affect e.g. the canvas background). Note that forced-color-adjust is not propagated from
    /// HTML body": the color is forced when the colors of the root element are.
    /// </summary>
    internal string GetCanvasBackgroundColor(ComputedStyle root)
    {
        if (!Environment.ForcedColors || IsForcedColorsAdjusted == root.IsForcedColorsAdjusted)
            return GetUsedValue("background-color");

        var color = ResolveColorKeywords("background-color", GetComputedValue("background-color"));
        return root.IsForcedColorsAdjusted ? ForceBackgroundColor(color) : color;
    }

    /// <summary>Returns the used value of a color property of a style whose colors are forced (the computed value is given).</summary>
    private string ForceColors(string property, ForcedColorsKind kind, string value)
    {
        if (IsHighlight)
            return ForceHighlightColors(property, kind, value);

        switch (kind)
        {
            case ForcedColorsKind.Color:
                return GetUsedColor();

            case ForcedColorsKind.Background:
                return HasSystemColor(property, value) ? value : ForceBackgroundColor(ResolveColorKeywords(property, value));

            case ForcedColorsKind.Border:
                // (auto is the color of the focus ring of the user agent)
                if (value == "auto" || HasSystemColor(property, value))
                    return value;

                return GetUserAgentSystemColor(property) ?? GetUsedColor();

            default:
                return HasSystemColor(property, value) ? value : ReplaceColors(property, value, GetUsedColor());
        }
    }

    /// <summary>
    /// Returns the used value of <c>color</c> in forced colors mode, when the colors of the style are forced: the computed value
    /// when it is a system color, otherwise the color that the element has without author style sheet ("the UA determines the
    /// appropriate forced system color, which should match the color that would result from an empty author style sheet").
    /// </summary>
    private string GetForcedColor(string computedValue)
    {
        var data = Rare.ForcedColors ??= new ForcedColorsData();
        return data.UsedColor ??= HasSystemColor("color", computedValue) ? computedValue : GetUserAgentColor().Value;
    }

    /// <summary>
    /// Forces a background color: "it is forced to the color opposite the color property's system color value in the system
    /// color pairings [...]. However, its alpha channel is taken from the original background-color value so that transparent
    /// backgrounds remain transparent". The system color that the user agent style sheet gives to the background of the element
    /// (e.g. <c>ButtonFace</c> for a disabled button, whose text is <c>GrayText</c>) is used first.
    /// </summary>
    private string ForceBackgroundColor(string color)
    {
        var background = GetUserAgentSystemColor("background-color") ?? CssValueComputer.GetSystemColor(this, GetPairedBackground(GetUserAgentColor().Keyword));
        return WithAlphaOf(background, color);
    }

    /// <summary>
    /// Returns the color of the backplates of the text of a block container in forced colors mode: the system color that its
    /// background color is forced to, opaque (see <see cref="ForceBackgroundColor"/>).
    /// </summary>
    internal string GetBackplateColor()
        => GetUserAgentSystemColor("background-color") ?? CssValueComputer.GetSystemColor(this, GetPairedBackground(GetUserAgentColor().Keyword));

    /// <summary>
    /// Returns the background of the system color pairing of a foreground system color
    /// (https://drafts.csswg.org/css-color-4/#css-system-colors: the pairings "expected to form legible background/foreground
    /// colors"): the text of the links and the disabled text are on the canvas.
    /// </summary>
    private static string GetPairedBackground(string foreground) => foreground switch
    {
        "buttontext" => "buttonface",
        "fieldtext" => "field",
        "highlighttext" => "highlight",
        "marktext" => "mark",
        "selecteditemtext" => "selecteditem",
        "accentcolortext" => "accentcolor",
        _ => "canvas",
    };

    /// <summary>Returns an opaque color (a system color) with the alpha of another color (a resolved color).</summary>
    private static string WithAlphaOf(string opaque, string color)
    {
        if (CssValueComputer.ParseCssColorValue(color) is not { } parsed)
            return color;

        var alpha = Math.Clamp(parsed.Alpha ?? 0, 0, 1);
        if (alpha >= 1 || !opaque.StartsWith("rgb(", StringComparison.Ordinal))
            return opaque;

        return string.Concat("rgba(", opaque.AsSpan(4, opaque.Length - 5), ", " + CssColor.FormatNumber(alpha) + ")");
    }

    /// <summary>
    /// Returns the style of the element computed from the declarations of the user agent origin only (the style of the element
    /// in a document without author style sheet), which the forced colors come from. Only its own declarations are meaningful:
    /// it inherits from the style of the parent.
    /// </summary>
    private ComputedStyle GetUserAgentStyle()
    {
        var data = Rare.ForcedColors ??= new ForcedColorsData();
        return data.UserAgentStyle ??= Engine.CreateUserAgentStyle(this);
    }

    /// <summary>
    /// Returns the value that a system color keyword of the user agent style sheet gives to a color property of the element, or
    /// null when the user agent style sheet does not give a system color to the property.
    /// </summary>
    private string? GetUserAgentSystemColor(string property)
    {
        var userAgentStyle = GetUserAgentStyle();
        return userAgentStyle.GetDeclaredKeyword(property) is { } keyword && CssValueComputer.GetSystemColorKeyword(keyword) is not null ? userAgentStyle.GetComputedValue(property) : null;
    }

    /// <summary>
    /// Returns the color of the element in a document without author style sheet, when it is a system color: the system color
    /// that the user agent style sheet gives to the element (e.g. <c>LinkText</c> for a link, <c>ButtonText</c> for a button),
    /// or else the one of its parent (the color is inherited: the text of an element in a button is <c>ButtonText</c> too), or
    /// <c>CanvasText</c> (the initial value).
    /// </summary>
    private (string Keyword, string Value) GetUserAgentColor()
    {
        if (_rareData?.ForcedColors?.UserAgentColor is { } cached)
            return cached;

        // (iteratively: the trees can be deep)
        List<ComputedStyle>? styles = null;
        (string Keyword, string Value)? result = null;
        for (var style = this; style is not null; style = style.Parent)
        {
            if (style._rareData?.ForcedColors?.UserAgentColor is { } known)
            {
                result = known;
                break;
            }

            var userAgentStyle = style.GetUserAgentStyle();
            if (userAgentStyle.GetDeclaredKeyword("color") is { } keyword && CssValueComputer.GetSystemColorKeyword(keyword) is { } systemColor)
            {
                result = (systemColor, userAgentStyle.GetComputedValue("color"));
            }

            (styles ??= []).Add(style);
            if (result is not null)
                break;
        }

        var color = result ?? ("canvastext", CssValueComputer.GetSystemColor(this, "canvastext"));
        if (styles is not null)
        {
            foreach (var style in styles)
            {
                (style.Rare.ForcedColors ??= new ForcedColorsData()).UserAgentColor = color;
            }
        }

        return color;
    }

    /// <summary>
    /// Forces the colors of a highlight pseudo-element, whose originating element has forced colors: the colors other than
    /// system colors are replaced with the highlight colors of the user agent (as in Chromium: <c>Highlight</c> and
    /// <c>HighlightText</c>, <c>Mark</c> and <c>MarkText</c> for the target text; a spelling or grammar error keeps the color of
    /// its text, on the canvas color). The background is opaque, as a backplate, unless it is fully transparent (not painted);
    /// the text of a highlight without background keeps its color, which is legible on the background of the element.
    /// </summary>
    private string ForceHighlightColors(string property, ForcedColorsKind kind, string value)
    {
        var name = StyleEngine.GetHighlightName(PseudoElement);
        switch (kind)
        {
            case ForcedColorsKind.Color:
                return GetUsedColor();

            case ForcedColorsKind.Background:
            {
                if (value == "currentcolor" || HasSystemColor(property, value))
                    return ResolveColorKeywords(property, value);

                var color = ResolveColorKeywords(property, value);
                if (CssValueComputer.ParseCssColorValue(color) is not { Alpha: > 0 })
                    return color;

                return CssValueComputer.GetSystemColor(this, name switch
                {
                    "target-text" => "mark",
                    "spelling-error" or "grammar-error" => "canvas",
                    _ => "highlight",
                });
            }

            default:
                return value == "currentcolor" || HasSystemColor(property, value) ? ResolveColorKeywords(property, value) : ReplaceColors(property, value, GetUsedColor());
        }
    }

    /// <summary>Returns the used color of a highlight pseudo-element whose originating element has forced colors (see <see cref="ForceHighlightColors"/>).</summary>
    private string GetForcedHighlightColor(string computedValue, ComputedStyle originating)
    {
        // The highlight does not change the color of the text
        if (computedValue == "currentcolor")
            return originating.GetUsedColor();

        if (HasSystemColor("color", computedValue))
            return computedValue;

        var name = StyleEngine.GetHighlightName(PseudoElement);
        if (name is "spelling-error" or "grammar-error")
            return originating.GetUsedColor();

        // The text is on the background of the highlight when it has one
        var background = GetComputedValue("background-color");
        var hasBackground = background == "currentcolor" || CssValueComputer.ParseCssColorValue(background) is { Alpha: > 0 };
        return hasBackground ? CssValueComputer.GetSystemColor(this, name == "target-text" ? "marktext" : "highlighttext") : originating.GetUsedColor();
    }

    /// <summary>
    /// Replaces the colors of the resolved value of a property with a color: the value of the properties that are a single color;
    /// each color of the lists (<c>column-rule-color</c>, <c>caret-color</c>), and the color or the fallback color of an SVG
    /// paint (<c>none</c>, the paint servers and the context paints are kept).
    /// </summary>
    private static string ReplaceColors(string property, string value, string color)
    {
        if (property is not ("fill" or "stroke" or "caret-color" or "column-rule-color" or "row-rule-color"))
            return color;

        if (value is "none" or "context-fill" or "context-stroke" or "")
            return value;

        var components = CssParser.ParseListOfComponentValues(value);
        var builder = new StringBuilder(value.Length);
        Append(components);
        return builder.ToString();

        void Append(List<CssComponentValue> values)
        {
            foreach (var component in values)
            {
                switch (component)
                {
                    case CssPreservedToken { Type: CssTokenType.Hash }:
                        builder.Append(color);
                        break;

                    case CssPreservedToken { Type: CssTokenType.Ident } ident when ident.Token.Value is not ("none" or "context-fill" or "context-stroke" or "repeat"):
                        // (auto is the color of the text for caret-color; the other identifiers are colors)
                        builder.Append(color);
                        break;

                    case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "repeat"):
                    {
                        // repeat(<count>, <colors>): the count (an integer or auto) is kept
                        var comma = function.Value.FindIndex(static argument => argument is CssPreservedToken { Type: CssTokenType.Comma });
                        builder.Append("repeat(").Append(CssSerializer.SerializeValue(function.Value.GetRange(0, comma + 1), property: null));
                        if (comma >= 0)
                        {
                            builder.Append(' ');
                        }

                        Append(function.Value.GetRange(comma + 1, function.Value.Count - comma - 1));
                        builder.Append(')');
                        break;
                    }

                    case CssFunction function when !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "url"):
                        builder.Append(color);
                        break;

                    default:
                        builder.Append(CssSerializer.SerializeValue([component], property: null));
                        break;
                }
            }
        }
    }
}
