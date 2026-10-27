using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The presentational hints defined in prose by the rendering section of HTML
/// (https://html.spec.whatwg.org/multipage/rendering.html#presentational-hints): the attributes that map to pixel length,
/// dimension and color properties. The hints expressed as CSS are in <see cref="HtmlUserAgentStyleSheet.PresentationalHints"/>.
/// </summary>
internal static partial class PresentationalHints
{
    /// <summary>Returns the declarations of the presentational hints of an HTML element, or null (the media queries of the source elements of a picture are evaluated in the environment).</summary>
    /// <param name="visitedLink">Whether the element is a link that matches <c>:visited</c> (the cascade of its visited style, see <see cref="ComputedStyle.VisitedLink"/>).</param>
    public static List<CssDeclarationEntry>? GetDeclarations(Element element, MediaEnvironment environment, bool visitedLink = false)
    {
        // The cells get the cellpadding attribute of their table
        if (element.AttributeCount == 0 && element.LocalName is not ("body" or "img" or "td" or "th" or "center"))
            return null;

        // Most elements have no presentational hint: the builder of the current thread is reused
        var css = StringBuilderCache.Acquire();
        switch (element.LocalName)
        {
            case "body":
                // https://html.spec.whatwg.org/multipage/rendering.html#the-page: without attribute, the default margins are
                // in the user agent style sheet (like in the browsers, revert rolls back to them)
                // (the rightmargin and bottommargin attributes are not mapped)
                var frame = element.NodeDocument.BrowsingContext?.Container;
                if ((FirstAttribute(element, "marginheight", "topmargin") ?? frame?.GetAttributeValue("marginheight")) is { } marginHeight)
                {
                    AppendPixels(css, "margin-top", marginHeight, defaultValue: "8px");
                    AppendPixels(css, "margin-bottom", marginHeight, defaultValue: "8px");
                }

                if ((FirstAttribute(element, "marginwidth", "leftmargin") ?? frame?.GetAttributeValue("marginwidth")) is { } marginWidth)
                {
                    AppendPixels(css, "margin-left", marginWidth, defaultValue: "8px");
                    AppendPixels(css, "margin-right", marginWidth, defaultValue: "8px");
                }

                AppendColor(css, "background-color", element.GetAttributeValue("bgcolor"));
                AppendColor(css, "color", element.GetAttributeValue("text"));
                AppendBackgroundImage(css, element);
                break;

            case "table":
                AppendDimension(css, "width", element.GetAttributeValue("width"), ignoreZero: true);
                AppendDimension(css, "height", element.GetAttributeValue("height"), ignoreZero: false);
                AppendColor(css, "background-color", element.GetAttributeValue("bgcolor"));
                AppendBackgroundImage(css, element);
                if (element.GetAttributeValue("cellspacing") is { } cellspacing && TryParseNonNegativeInteger(cellspacing, out var spacing))
                {
                    css.Append("border-spacing: ").Append(spacing.ToString(CultureInfo.InvariantCulture)).Append("px;");
                }

                if (element.GetAttributeValue("border") is { } border)
                {
                    // https://html.spec.whatwg.org/multipage/rendering.html#tables-2: 1px when the value cannot be parsed
                    var width = TryParseNonNegativeInteger(border, out var value) ? value : 1;
                    css.Append("border-width: ").Append(width.ToString(CultureInfo.InvariantCulture)).Append("px;");
                }

                AppendColor(css, "border-color", element.GetAttributeValue("bordercolor"));
                break;

            case "td" or "th":
                AppendDimension(css, "width", element.GetAttributeValue("width"), ignoreZero: true);
                AppendDimension(css, "height", element.GetAttributeValue("height"), ignoreZero: true);
                AppendAlign(css, element);

                // In quirks mode, the nowrap attribute is ignored when the width attribute is a nonzero length
                if (element.NodeDocument.Mode == DocumentMode.Quirks && element.HasAttributeWithLocalName("nowrap") && ParseDimension(element.GetAttributeValue("width")) is { Percentage: false, Value: > 0 })
                {
                    css.Append("white-space: normal;");
                }

                AppendColor(css, "background-color", element.GetAttributeValue("bgcolor"));
                AppendBackgroundImage(css, element);
                if (GetTable(element)?.GetAttributeValue("cellpadding") is { } cellpadding && TryParseNonNegativeInteger(cellpadding, out var padding))
                {
                    css.Append("padding: ").Append(padding.ToString(CultureInfo.InvariantCulture)).Append("px;");
                }

                break;

            case "tr" or "thead" or "tbody" or "tfoot":
                AppendDimension(css, "height", element.GetAttributeValue("height"), ignoreZero: false);
                AppendAlign(css, element);
                AppendColor(css, "background-color", element.GetAttributeValue("bgcolor"));
                AppendBackgroundImage(css, element);
                break;

            case "center":
                // https://html.spec.whatwg.org/multipage/rendering.html#flow-content-3: "The center element [...] expected to
                // center text within themselves, as if they had their 'text-align' property set to 'center' in a presentational
                // hint, and to align descendants to the center" (the alignment of the descendants: see GetDescendantAlignment)
                css.Append("text-align: center;");
                break;

            case "div":
                // The align attribute of the div element sets text-align and aligns the descendants
                AppendAlign(css, element);
                break;

            case "col" or "colgroup":
                // https://html.spec.whatwg.org/multipage/rendering.html#tables-2 maps the width of col; the browsers map the width of
                // colgroup too (WPT css/css-tables/tentative/colgroup-col.html)
                AppendDimension(css, "width", element.GetAttributeValue("width"), ignoreZero: false);
                break;

            case "li":
                // https://html.spec.whatwg.org/multipage/rendering.html#lists: the value attribute of a li element
                if (element.GetAttributeValue("value") is { } liValue && Html.Microsyntaxes.TryParseInteger(liValue, out var ordinal))
                {
                    css.Append("counter-set: list-item ").Append(ClampToInt32(ordinal).ToString(CultureInfo.InvariantCulture)).Append(';');
                }

                break;

            case "ol":
                AppendListCounterReset(css, element);
                break;

            case "hr":
                // https://html.spec.whatwg.org/multipage/rendering.html#the-hr-element-2
                AppendDimension(css, "width", element.GetAttributeValue("width"), ignoreZero: false);
                AppendColor(css, "color", element.GetAttributeValue("color"));
                if (element.GetAttributeValue("size") is { } hrSize && TryParseNonNegativeInteger(hrSize, out var hrSizeValue))
                {
                    if (element.HasAttributeWithLocalName("color") || element.HasAttributeWithLocalName("noshade"))
                    {
                        css.Append("border-width: ").Append(CssSerializer.SerializeNumber(hrSizeValue / 2.0)).Append("px;");
                    }
                    else if (hrSizeValue == 1)
                    {
                        css.Append("border-bottom-width: 0;");
                    }
                    else if (hrSizeValue > 1)
                    {
                        css.Append("height: ").Append((hrSizeValue - 2).ToString(CultureInfo.InvariantCulture)).Append("px;");
                    }
                }

                break;

            case "a" or "area":
                AppendLinkColor(css, element, visitedLink);
                break;

            case "img" or "video" or "canvas" or "embed" or "iframe" or "object" or "marquee" or "input":
                // https://html.spec.whatwg.org/multipage/rendering.html#attributes-for-embedded-content-and-images
                if (element.LocalName == "input" && !InfraAscii.EqualsIgnoreAsciiCase(element.GetAttributeValue("type") ?? "", "image"))
                    break;

                switch (element.LocalName)
                {
                    case "img":
                        // The width and height attributes of the dimension attribute source (the img element, or the selected
                        // source element of its parent picture element)
                        var source = element is HtmlImageElement image ? image.GetDimensionAttributeSource(environment) : element;
                        AppendDimension(css, "width", source.GetAttributeValue("width"), ignoreZero: false);
                        AppendDimension(css, "height", source.GetAttributeValue("height"), ignoreZero: false);
                        AppendAspectRatioUsingDimensionRules(css, source);
                        break;

                    case "video" or "input":
                        AppendDimension(css, "width", element.GetAttributeValue("width"), ignoreZero: false);
                        AppendDimension(css, "height", element.GetAttributeValue("height"), ignoreZero: false);
                        AppendAspectRatioUsingDimensionRules(css, element);
                        break;

                    case "canvas":
                        // https://html.spec.whatwg.org/multipage/rendering.html#map-to-the-aspect-ratio-property
                        if (element.GetAttributeValue("width") is { } canvasWidth && TryParseNonNegativeInteger(canvasWidth, out var aspectWidth)
                            && element.GetAttributeValue("height") is { } canvasHeight && TryParseNonNegativeInteger(canvasHeight, out var aspectHeight))
                        {
                            css.Append("aspect-ratio: auto ").Append(aspectWidth.ToString(CultureInfo.InvariantCulture)).Append(" / ").Append(aspectHeight.ToString(CultureInfo.InvariantCulture)).Append(';');
                        }

                        break;

                    default:
                        AppendDimension(css, "width", element.GetAttributeValue("width"), ignoreZero: false);
                        AppendDimension(css, "height", element.GetAttributeValue("height"), ignoreZero: false);
                        break;
                }

                // The hspace and vspace attributes of embed, img, object, input (Image Button) and marquee
                // (https://html.spec.whatwg.org/multipage/rendering.html#the-marquee-element-2)
                if (element.LocalName is "embed" or "img" or "object" or "input" or "marquee")
                {
                    AppendDimension(css, "margin-left", element.GetAttributeValue("hspace"), ignoreZero: false);
                    AppendDimension(css, "margin-right", element.GetAttributeValue("hspace"), ignoreZero: false);
                    AppendDimension(css, "margin-top", element.GetAttributeValue("vspace"), ignoreZero: false);
                    AppendDimension(css, "margin-bottom", element.GetAttributeValue("vspace"), ignoreZero: false);
                }

                // The align attribute set to center or middle: "a value that aligns the vertical middle of the element with the
                // parent element's baseline", the -webkit-baseline-middle value of the browsers (the other values are in the
                // presentational hints style sheet)
                if (element.LocalName is "embed" or "iframe" or "img" or "input" or "object" && element.GetAttributeValue("align") is { } replacedAlign
                    && (InfraAscii.EqualsIgnoreAsciiCase(replacedAlign, "center") || InfraAscii.EqualsIgnoreAsciiCase(replacedAlign, "middle")))
                {
                    css.Append("vertical-align: -webkit-baseline-middle;");
                }

                if (element.LocalName is "img" or "object" or "input" && element.GetAttributeValue("border") is { } imageBorder && TryParseNonNegativeInteger(imageBorder, out var borderWidth) && borderWidth > 0)
                {
                    css.Append("border-width: ").Append(borderWidth.ToString(CultureInfo.InvariantCulture)).Append("px; border-style: solid;");
                }

                // The frameborder attribute of an iframe: zero or an error (rules for parsing integers) removes the borders
                if (element.LocalName == "iframe" && element.GetAttributeValue("frameborder") is { } frameBorder && (!Html.Microsyntaxes.TryParseInteger(frameBorder, out var frameBorderValue) || frameBorderValue == 0))
                {
                    css.Append("border-width: 0;");
                }

                if (element.LocalName == "marquee")
                {
                    AppendColor(css, "background-color", element.GetAttributeValue("bgcolor"));
                }

                break;

            case "font":
                // https://html.spec.whatwg.org/multipage/rendering.html#phrasing-content-3
                AppendColor(css, "color", element.GetAttributeValue("color"));
                if (element.GetAttributeValue("face") is { Length: > 0 } face)
                {
                    css.Append("font-family: ").Append(face).Append(';');
                }

                if (GetLegacyFontSize(element.GetAttributeValue("size")) is { } size)
                {
                    css.Append("font-size: ").Append(size).Append(';');
                }

                break;
        }

        if (css.Length == 0)
        {
            StringBuilderCache.Release(css);
            return null;
        }

        var declarations = new List<CssDeclarationEntry>();
        CssStyleDeclaration.ParseDeclarationBlock(StringBuilderCache.GetStringAndRelease(css), declarations, atRule: null);
        return declarations;
    }

    /// <summary>
    /// Returns the declarations of the presentational hints of a MathML element, or null: the attributes common to the MathML
    /// elements (https://w3c.github.io/mathml-core/#global-attributes) and the attributes of mi, mspace and mpadded.
    /// </summary>
    public static List<CssDeclarationEntry>? GetMathMLDeclarations(Element element)
    {
        if (element.AttributeCount == 0)
            return null;

        var css = new StringBuilder();

        // https://w3c.github.io/mathml-core/#attributes-common-to-html-and-mathml-elements
        if (element.GetAttributeValue("dir") is { } dir && InfraAscii.ToAsciiLowercase(dir) is "ltr" or "rtl")
        {
            css.Append("direction: ").Append(InfraAscii.ToAsciiLowercase(dir)).Append(';');
        }

        // https://w3c.github.io/mathml-core/#legacy-mathml-style-attributes
        AppendMathMLValue(css, "color", element.GetAttributeValue("mathcolor"), "<color>");
        AppendMathMLValue(css, "background-color", element.GetAttributeValue("mathbackground"), "<color>");
        AppendMathMLValue(css, "font-size", element.GetAttributeValue("mathsize"), "<length-percentage>");

        // https://w3c.github.io/mathml-core/#the-displaystyle-and-scriptlevel-attributes
        if (element.GetAttributeValue("displaystyle") is { } displayStyle)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(displayStyle, "true"))
            {
                css.Append("math-style: normal;");
            }
            else if (InfraAscii.EqualsIgnoreAsciiCase(displayStyle, "false"))
            {
                css.Append("math-style: compact;");
            }
        }

        if (element.GetAttributeValue("scriptlevel") is { Length: > 0 } scriptLevel)
        {
            var sign = scriptLevel[0] is '+' or '-' ? scriptLevel[0] : (char?)null;
            var digits = sign is null ? scriptLevel : scriptLevel[1..];
            if (digits.Length > 0 && digits.All(char.IsAsciiDigit) && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var level))
            {
                css.Append("math-depth: ").Append(sign switch
                {
                    '+' => "add(" + level.ToString(CultureInfo.InvariantCulture) + ")",
                    '-' => "add(-" + level.ToString(CultureInfo.InvariantCulture) + ")",
                    _ => level.ToString(CultureInfo.InvariantCulture),
                }).Append(';');
            }
        }

        switch (element.LocalName)
        {
            case "mi":
                // https://w3c.github.io/mathml-core/#the-mathvariant-attribute
                if (element.GetAttributeValue("mathvariant") is { } mathVariant && InfraAscii.EqualsIgnoreAsciiCase(mathVariant, "normal"))
                {
                    css.Append("text-transform: none;");
                }

                break;

            case "mspace":
                // https://w3c.github.io/mathml-core/#space-mspace: the height and the depth are the height of the box
                AppendMathMLValue(css, "width", GetMathMLLength(element, "width"), "<length-percentage>");
                var height = GetMathMLLength(element, "height");
                var depth = GetMathMLLength(element, "depth");
                AppendMathMLValue(css, "height", height is not null && depth is not null ? "calc(" + height + " + " + depth + ")" : height ?? depth, "<length-percentage>");
                break;

            case "mpadded":
                // https://w3c.github.io/mathml-core/#adjust-space-around-content-mpadded
                AppendMathMLValue(css, "width", GetMathMLLength(element, "width"), "<length-percentage>");
                break;
        }

        if (css.Length == 0)
            return null;

        var declarations = new List<CssDeclarationEntry>();
        CssStyleDeclaration.ParseDeclarationBlock(css.ToString(), declarations, atRule: null);
        return declarations;
    }

    /// <summary>Returns the value of an attribute that is a valid &lt;length-percentage&gt; and not a percentage, or null.</summary>
    private static string? GetMathMLLength(Element element, string name)
    {
        if (element.GetAttributeValue(name) is not { } value || !IsValidMathMLValue(value, "<length-percentage>"))
            return null;

        return ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is [CssPreservedToken { Type: CssTokenType.Percentage }] ? null : value;
    }

    /// <summary>Appends a declaration when the value of the attribute matches the type (a CSS-wide keyword or a substitution function is not a valid value).</summary>
    private static void AppendMathMLValue(StringBuilder css, string property, string? value, string type)
    {
        if (value is not null && IsValidMathMLValue(value, type))
        {
            css.Append(property).Append(": ").Append(value).Append(';');
        }
    }

    /// <summary>Whether the value of a MathML attribute is a valid &lt;length-percentage&gt; (https://w3c.github.io/mathml-core/#dfn-length-percentage).</summary>
    internal static bool IsValidMathMLLengthPercentage(string value) => IsValidMathMLValue(value, "<length-percentage>");

    private static bool IsValidMathMLValue(string value, string type)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        return values.Count > 0 && !ValueMatcher.ContainsSubstitutionFunction(values) && CssPropertyRegistry.GetTypeGrammar(type) is { } grammar && ValueMatcher.Matches(grammar, values);
    }

    /// <summary>
    /// The presentation attributes of SVG (https://svgwg.org/svg2-draft/styling.html#PresentationAttributes): the attributes
    /// named after a property; the geometry properties only apply to some elements.
    /// </summary>
    private static readonly FrozenSet<string> SvgPresentationAttributes = FrozenSet.Create(StringComparer.Ordinal,
        "alignment-baseline", "baseline-shift", "clip", "clip-path", "clip-rule", "color", "color-interpolation", "color-interpolation-filters",
        "cursor", "direction", "display", "dominant-baseline", "fill", "fill-opacity", "fill-rule", "filter", "flood-color",
        "flood-opacity", "font-family", "font-size", "font-size-adjust", "font-stretch", "font-style", "font-variant", "font-weight",
        "glyph-orientation-horizontal", "glyph-orientation-vertical", "image-rendering", "letter-spacing", "lighting-color",
        "marker-end", "marker-mid", "marker-start", "mask", "mask-type", "opacity", "overflow", "paint-order", "pointer-events",
        "shape-rendering", "stop-color", "stop-opacity", "stroke", "stroke-dasharray", "stroke-dashoffset", "stroke-linecap",
        "stroke-linejoin", "stroke-miterlimit", "stroke-opacity", "stroke-width", "text-anchor", "text-decoration", "text-overflow",
        "text-rendering", "transform-origin", "unicode-bidi", "vector-effect", "visibility", "white-space", "word-spacing", "writing-mode");

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![\w.%-])(?<number>[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?)(?![\w.%])", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial System.Text.RegularExpressions.Regex UnitlessNumber();

    /// <summary>Returns the declarations of the presentation attributes of an SVG element, or null.</summary>
    public static List<CssDeclarationEntry>? GetSvgDeclarations(Element element)
    {
        List<CssDeclarationEntry>? declarations = null;

        // The animated values of the attributes (https://svgwg.org/specs/animations/#TermAnimatedValue) replace their values
        var animated = element.NodeDocument.SmilOrNull?.GetAttributeValues(element);
        for (var i = 0; i < element.AttributeCount; i++)
        {
            if (element.GetAttributeNamespace(i) is not null)
                continue;

            var name = element.GetAttributeLocalName(i);
            if (animated is not null && animated.ContainsKey(name))
                continue;

            AddSvgDeclarations(element, name, element.GetAttributeValue(i), ref declarations);
        }

        if (animated is not null)
        {
            foreach (var (name, value) in animated)
            {
                AddSvgDeclarations(element, name, value, ref declarations);
            }
        }

        return declarations is { Count: > 0 } ? declarations : null;
    }

    /// <summary>Whether an attribute is a presentation attribute of all the SVG elements (the geometry properties only apply to some elements).</summary>
    public static bool IsSvgPresentationAttribute(string name) => SvgPresentationAttributes.Contains(name);

    private static void AddSvgDeclarations(Element element, string name, string value, ref List<CssDeclarationEntry>? declarations)
    {
        if (IsSvgTransformAttribute(element.LocalName, name))
        {
            // https://drafts.csswg.org/css-transforms-1/#transform-attribute-specificity: the transform attribute (patternTransform,
            // gradientTransform) is a presentation attribute of the transform property, with the syntax of SVG
            if (SvgTransform.TryParseList(value, out var transforms) && transforms.Count > 0)
            {
                declarations ??= [];
                CssStyleDeclaration.ParseDeclarationBlock("transform:" + SvgTransform.ToCss(transforms), declarations, atRule: null);
            }

            return;
        }

        if (name == "pathLength")
        {
            // https://svgwg.org/svg2-draft/paths.html#PathLengthCSSProperty: the pathLength attribute (a number of user units) is the
            // presentation attribute of the path-length property of the shapes
            if (element.LocalName is "path" or "circle" or "ellipse" or "line" or "polygon" or "polyline" or "rect" && SvgParsing.TryParseSingleNumber(value, out var pathLength) && pathLength >= 0)
            {
                declarations ??= [];
                CssStyleDeclaration.ParseDeclarationBlock(string.Create(CultureInfo.InvariantCulture, $"path-length:{pathLength}px"), declarations, atRule: null);
            }

            return;
        }

        if (!SvgPresentationAttributes.Contains(name) && !IsSvgGeometryAttribute(element.LocalName, name))
            return;

        // https://svgwg.org/specs/animations/#FillAttribute: the fill attribute of the animation elements is not the presentation
        // attribute of the fill property; https://github.com/w3c/svgwg/issues/1057: the width and height attributes of a nested
        // svg element are not presentation attributes
        if (name == "fill" ? element is SvgAnimationElement : name is "width" or "height" && element is SvgSvgElement svg && !SvgUserSpace.IsOutermost(svg))
            return;

        // The lengths of the presentation attributes can be unitless numbers (user units)
        if (name == "d")
        {
            // https://svgwg.org/svg2-draft/paths.html#TheDProperty: the path data, up to the first error, is a path() function
            var commands = SvgPathData.Parse(value);
            if (commands.Count > 0)
            {
                declarations ??= [];
                CssStyleDeclaration.ParseDeclarationBlock("d:" + CssPath.Serialize(fillRule: null, commands), declarations, atRule: null);
            }

            return;
        }

        declarations ??= [];
        var count = declarations.Count;
        CssStyleDeclaration.ParseDeclarationBlock(name + ":" + value, declarations, atRule: null);
        if (declarations.Count == count && double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            CssStyleDeclaration.ParseDeclarationBlock(name + ":" + value.Trim() + "px", declarations, atRule: null);
        }
        else if (declarations.Count == count && (name == "transform-origin" || (name == "clip-path" && !value.AsSpan().ContainsAny('"', '\''))))
        {
            // A list of unitless numbers (e.g. transform-origin="170 150"), or the unitless lengths of a basic shape (e.g.
            // clip-path="inset(0 50 0 0)"; a string is the path data of path()): the numbers are user units
            CssStyleDeclaration.ParseDeclarationBlock(name + ":" + UnitlessNumber().Replace(value, "${number}px"), declarations, atRule: null);
        }
    }

    /// <summary>
    /// Parses the value of a presentation attribute of SVG as the declarations of its property (the lengths can be unitless
    /// numbers): the values of the SMIL animations of the properties.
    /// </summary>
    public static List<CssDeclarationEntry> ParseSvgPresentationAttribute(string name, string value)
    {
        var declarations = new List<CssDeclarationEntry>();
        CssStyleDeclaration.ParseDeclarationBlock(name + ":" + value, declarations, atRule: null);
        if (declarations.Count == 0 && double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            CssStyleDeclaration.ParseDeclarationBlock(name + ":" + value.Trim() + "px", declarations, atRule: null);
        }

        return declarations;
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/styling.html#TransformProperty: the attribute that is the presentation attribute of the
    /// transform property: <c>patternTransform</c> on a pattern, <c>gradientTransform</c> on a gradient, <c>transform</c> on the
    /// other elements.
    /// </summary>
    private static bool IsSvgTransformAttribute(string element, string attribute) => element switch
    {
        "pattern" => attribute == "patternTransform",
        "linearGradient" or "radialGradient" => attribute == "gradientTransform",
        _ => attribute == "transform",
    };

    /// <summary>https://svgwg.org/svg2-draft/geometry.html: the geometry properties of the elements.</summary>
    private static bool IsSvgGeometryAttribute(string element, string attribute) => attribute switch
    {
        "cx" or "cy" => element is "circle" or "ellipse",
        "r" => element == "circle",
        "rx" or "ry" => element is "ellipse" or "rect",
        "x" or "y" or "width" or "height" => element is "foreignObject" or "image" or "rect" or "svg" or "symbol" or "use",
        "d" => element == "path",
        _ => false,
    };

    private static string? FirstAttribute(Element element, string name1, string name2) => element.GetAttributeValue(name1) ?? element.GetAttributeValue(name2);

    /// <summary>
    /// Returns whether the border attribute of a table, or of the table of a cell, is "not equivalent to zero"
    /// (https://html.spec.whatwg.org/multipage/rendering.html#magic-border-selector): the attribute is present and the rules
    /// for parsing non-negative integers do not give zero. As in the browsers, a value that cannot be parsed is not zero: the
    /// border of the table is then 1px wide.
    /// </summary>
    public static bool HasNonZeroTableBorder(Element element)
    {
        var table = element.LocalName == "table" ? element : GetTable(element);
        return table?.GetAttributeValue("border") is { } border && !(TryParseNonNegativeInteger(border, out var value) && value == 0);
    }

    private static Element? GetTable(Element cell)
    {
        for (var node = cell.ParentNode; node is Element ancestor; node = ancestor.ParentNode)
        {
            if (ancestor.IsHtmlNamespace && ancestor.LocalName == "table")
                return ancestor;
        }

        return null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/rendering.html#maps-to-the-pixel-length-property</summary>
    private static void AppendPixels(StringBuilder css, string property, string? value, string defaultValue)
    {
        var pixels = value is not null && TryParseNonNegativeInteger(value, out var result) ? result.ToString(CultureInfo.InvariantCulture) + "px" : defaultValue;
        css.Append(property).Append(": ").Append(pixels).Append(';');
    }

    /// <summary>https://html.spec.whatwg.org/multipage/rendering.html#maps-to-the-dimension-property</summary>
    private static void AppendDimension(StringBuilder css, string property, string? value, bool ignoreZero)
    {
        if (ParseDimension(value) is not { } dimension || (ignoreZero && dimension.Value == 0))
            return;

        css.Append(property).Append(": ").Append(CssSerializer.SerializeNumber(dimension.Value)).Append(dimension.Percentage ? "%" : "px").Append(';');
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#rules-for-parsing-dimension-values</summary>
    private static (double Value, bool Percentage)? ParseDimension(string? input)
    {
        if (input is null)
            return null;

        var position = 0;
        while (position < input.Length && input[position] is ' ' or '\t' or '\n' or '\f' or '\r')
        {
            position++;
        }

        var start = position;
        while (position < input.Length && char.IsAsciiDigit(input[position]))
        {
            position++;
        }

        if (position == start)
            return null;

        var value = double.Parse(input.AsSpan(start, position - start), NumberStyles.None, CultureInfo.InvariantCulture);
        if (position < input.Length && input[position] == '.')
        {
            // The position advances past the dot even when no digit follows ("200.%" is a percentage)
            position++;
            var fractionStart = position;
            while (position < input.Length && char.IsAsciiDigit(input[position]))
            {
                position++;
            }

            if (position > fractionStart)
            {
                value += double.Parse("0." + input[fractionStart..position], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            }
        }

        return (value, position < input.Length && input[position] == '%');
    }

    private static bool TryParseNonNegativeInteger(string value, out long result) => Html.Microsyntaxes.TryParseNonNegativeInteger(value, out result);

    private static long ClampToInt32(long value) => Math.Clamp(value, int.MinValue, int.MaxValue);

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/rendering.html#map-to-the-aspect-ratio-property-(using-dimension-rules): the
    /// width and height attributes, when both are dimensions that are not percentages.
    /// </summary>
    private static void AppendAspectRatioUsingDimensionRules(StringBuilder css, Element element)
    {
        if (ParseDimension(element.GetAttributeValue("width")) is { Percentage: false } width && ParseDimension(element.GetAttributeValue("height")) is { Percentage: false } height)
        {
            css.Append("aspect-ratio: auto ").Append(CssSerializer.SerializeNumber(width.Value)).Append(" / ").Append(CssSerializer.SerializeNumber(height.Value)).Append(';');
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/rendering.html#phrasing-content-3: the link, vlink and alink attributes of the
    /// body element of the document set the color of the elements that match :link, :visited, and :active and either :link or
    /// :visited (a link only matches :visited in the cascade of its visited style).
    /// </summary>
    private static void AppendLinkColor(StringBuilder css, Element element, bool visitedLink)
    {
        if (!HtmlElementStates.IsLink(element) || element.GetRoot() is not Document document || document.Body is not HtmlBodyElement body)
            return;

        var color = UserActionStates.IsActive(element) ? ParseLegacyColor(body.GetAttributeValue("alink")) : null;
        color ??= ParseLegacyColor(body.GetAttributeValue(visitedLink ? "vlink" : "link"));
        if (color is not null)
        {
            css.Append("color: ").Append(color).Append(';');
        }
    }

    /// <summary>
    /// Returns how an HTML element aligns its descendants (https://html.spec.whatwg.org/multipage/rendering.html#align-descendants),
    /// or null when it does not: the center element (https://html.spec.whatwg.org/multipage/rendering.html#flow-content-3) and
    /// the align attribute of the div element and of the thead, tbody, tfoot, tr, td and th elements
    /// (https://html.spec.whatwg.org/multipage/rendering.html#tables-2); justify aligns the descendants to the left. The
    /// block-level descendants in normal flow are aligned by the layout (see <c>FormattingContext.GetBlockJustification</c>).
    /// </summary>
    internal static DescendantAlignment? GetDescendantAlignment(Element element)
    {
        if (!element.IsHtmlNamespace)
            return null;

        switch (element.LocalName)
        {
            case "center":
                return DescendantAlignment.Center;

            case "div" or "thead" or "tbody" or "tfoot" or "tr" or "td" or "th":
                return element.AttributeCount == 0 || element.GetAttributeValue("align") is not { } align ? null : InfraAscii.ToAsciiLowercase(align) switch
                {
                    "center" or "middle" => DescendantAlignment.Center,
                    "left" or "justify" => DescendantAlignment.Left,
                    "right" => DescendantAlignment.Right,
                    _ => null,
                };

            default:
                return null;
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/rendering.html#tables-2, https://html.spec.whatwg.org/multipage/rendering.html#flow-content-3:
    /// the align attribute of the thead, tbody, tfoot, tr, td, th and div elements sets text-align (the alignment of the
    /// descendants is not a CSS property: see <see cref="GetDescendantAlignment"/>).
    /// </summary>
    private static void AppendAlign(StringBuilder css, Element element)
    {
        if (element.GetAttributeValue("align") is not { } align)
            return;

        var textAlign = InfraAscii.ToAsciiLowercase(align) switch
        {
            "center" or "middle" => "center",
            "left" => "left",
            "right" => "right",
            "justify" => "justify",
            _ => null,
        };

        if (textAlign is not null)
        {
            css.Append("text-align: ").Append(textAlign).Append(';');
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/rendering.html#lists: the start and reversed attributes of an ol element map to counter-reset.</summary>
    private static void AppendListCounterReset(StringBuilder css, Element element)
    {
        long? value = null;
        if (element.GetAttributeValue("start") is { } start && Html.Microsyntaxes.TryParseInteger(start, out var startValue))
        {
            value = ClampToInt32(startValue);
        }

        if (element.HasAttributeWithLocalName("reversed"))
        {
            css.Append("counter-reset: reversed(list-item)");
            if (value is { } reversedValue)
            {
                css.Append(' ').Append((reversedValue + 1).ToString(CultureInfo.InvariantCulture));
            }

            css.Append(';');
        }
        else if (value is { } initialValue)
        {
            css.Append("counter-reset: list-item ").Append((initialValue - 1).ToString(CultureInfo.InvariantCulture)).Append(';');
        }
    }

    /// <summary>
    /// The background attribute of the body and table elements: the background-image property is the result of
    /// encoding-parsing-and-serializing the value relative to the node document (so the query uses the encoding of the
    /// document), when it is not empty and does not fail (https://html.spec.whatwg.org/multipage/rendering.html#the-page,
    /// https://html.spec.whatwg.org/multipage/rendering.html#tables-2).
    /// </summary>
    private static void AppendBackgroundImage(StringBuilder css, Element element)
    {
        if (element.GetAttributeValue("background") is { Length: > 0 } background && Html.Reflection.EncodingParseAndSerializeUrl(element.NodeDocument, background) is { } url)
        {
            css.Append("background-image: ").Append(CssSerializer.SerializeUrl(url)).Append(';');
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/rendering.html#maps-to-the-colour-property (the rules for parsing a legacy color value)</summary>
    private static void AppendColor(StringBuilder css, string property, string? value)
    {
        if (ParseLegacyColor(value) is { } color)
        {
            css.Append(property).Append(": ").Append(color).Append(';');
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#rules-for-parsing-a-legacy-colour-value</summary>
    internal static string? ParseLegacyColor(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        input = input.Trim(' ', '\t', '\n', '\f', '\r');
        if (input.Length == 0 || InfraAscii.EqualsIgnoreAsciiCase(input, "transparent"))
            return null;

        if (CssColor.IsNamedColor(input))
            return InfraAscii.ToAsciiLowercase(input);

        if (input.Length == 4 && input[0] == '#' && char.IsAsciiHexDigit(input[1]) && char.IsAsciiHexDigit(input[2]) && char.IsAsciiHexDigit(input[3]))
            return "#" + input[1] + input[1] + input[2] + input[2] + input[3] + input[3];

        var builder = new StringBuilder();
        foreach (var rune in input.EnumerateRunes())
        {
            builder.Append(rune.Value > 0xFFFF ? "00" : rune.ToString());
        }

        var value = builder.ToString();
        if (value.Length > 128)
        {
            value = value[..128];
        }

        if (value.StartsWith('#', StringComparison.Ordinal))
        {
            value = value[1..];
        }

        var chars = value.Select(c => char.IsAsciiHexDigit(c) ? c : '0').ToList();
        while (chars.Count == 0 || chars.Count % 3 != 0)
        {
            chars.Add('0');
        }

        var length = chars.Count / 3;
        var components = new[] { new string([.. chars.GetRange(0, length)]), new string([.. chars.GetRange(length, length)]), new string([.. chars.GetRange(length * 2, length)]) };
        if (length > 8)
        {
            for (var i = 0; i < 3; i++)
            {
                components[i] = components[i][(length - 8)..];
            }

            length = 8;
        }

        while (length > 2 && components.All(c => c[0] == '0'))
        {
            for (var i = 0; i < 3; i++)
            {
                components[i] = components[i][1..];
            }

            length--;
        }

        if (length > 2)
        {
            for (var i = 0; i < 3; i++)
            {
                components[i] = components[i][..2];
            }
        }

        return "#" + string.Concat(components.Select(c => c.PadLeft(2, '0')));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/rendering.html#phrasing-content-3: the size attribute of the font element.</summary>
    private static string? GetLegacyFontSize(string? input)
    {
        if (input is null)
            return null;

        var position = 0;
        while (position < input.Length && input[position] is ' ' or '\t' or '\n' or '\f' or '\r')
        {
            position++;
        }

        if (position >= input.Length)
            return null;

        var mode = input[position] switch
        {
            '+' => 1,
            '-' => -1,
            _ => 0,
        };

        if (mode != 0)
        {
            position++;
        }

        var start = position;
        while (position < input.Length && char.IsAsciiDigit(input[position]))
        {
            position++;
        }

        if (position == start || !long.TryParse(input.AsSpan(start, Math.Min(position - start, 10)), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return null;

        value = mode switch
        {
            1 => 3 + value,
            -1 => 3 - value,
            _ => value,
        };

        return Math.Clamp(value, 1, 7) switch
        {
            1 => "x-small",
            2 => "small",
            3 => "medium",
            4 => "large",
            5 => "x-large",
            6 => "xx-large",
            _ => "xxx-large",
        };
    }
}
