using System.Collections.Concurrent;
using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>A CSS property (https://drafts.csswg.org/css-values-4/#property-definition).</summary>
/// <param name="LogicalGroup">The logical property group (https://drafts.csswg.org/css-logical/#logical-property-group), e.g. "margin" for margin-top and margin-inline-start.</param>
/// <param name="AnimationType">The animation type of the property definition (https://drafts.csswg.org/web-animations-1/#animation-type).</param>
internal sealed record CssPropertyDefinition(string Name, string Grammar, string? Initial, bool Inherited, string[] Longhands, string? AliasOf, string? LogicalGroup = null, CssAnimationTypeKind AnimationType = CssAnimationTypeKind.Unspecified)
{
    public bool IsShorthand => Longhands.Length > 0;
}

/// <summary>A descriptor of an at-rule (https://drafts.csswg.org/css-syntax-3/#descriptor), e.g. font-family in @font-face or a media feature.</summary>
internal sealed record CssDescriptorDefinition(string AtRule, string Name, string Grammar, string? Initial);

/// <summary>The supported CSS properties and their value grammars.</summary>
internal static partial class CssPropertyRegistry
{
    // Declared before ByName, which uses it
    /// <summary>
    /// The longhands that a shorthand resets to their initial value but cannot set (https://drafts.csswg.org/css-backgrounds/#border-shorthand,
    /// https://drafts.fxtf.org/css-masking/#the-mask: mask resets mask-border; https://drafts.csswg.org/css-multicol-2/#columns:
    /// columns resets column-wrap, as in the browsers that support it, WPT css/css-multicol/columns-shorthand-reset-wrap.html).
    /// </summary>
    private static readonly Dictionary<string, string[]> ResetOnlyLonghands = new(StringComparer.Ordinal)
    {
        ["border"] = ["border-image"],
        ["mask"] = ["mask-border-source", "mask-border-slice", "mask-border-width", "mask-border-outset", "mask-border-repeat", "mask-border-mode"],
        ["columns"] = ["column-wrap"],

        // https://drafts.csswg.org/css-fonts/#font-prop: font resets the font properties it cannot set
        ["font"] = ["font-size-adjust", "font-kerning", "font-feature-settings", "font-language-override", "font-optical-sizing", "font-variation-settings"],
    };

    /// <summary>
    /// The properties whose grammar in webref does not match the browsers: the d property of SVG 2 is "none | &lt;string&gt;",
    /// but the browsers only accept the path() function. The names of container-name are &lt;container-name&gt;, whose
    /// &lt;custom-ident&gt; excludes keywords (https://drafts.csswg.org/css-conditional-5/#container-name). The browsers accept
    /// the legacy normal keyword of zoom (https://drafts.csswg.org/css-viewport/#zoom-property).
    /// </summary>
    private static readonly Dictionary<string, string> OverriddenGrammars = new(StringComparer.Ordinal)
    {
        ["d"] = "none | path( <string> )",

        // https://drafts.csswg.org/css-images-3/#the-image-orientation: the <angle> and flip values are deprecated ("UAs must
        // not support them", as the browsers: WPT css/css-images/parsing/image-orientation-invalid.html)
        ["image-orientation"] = "from-image | none",

        // https://drafts.csswg.org/css-anchor-position-1/#anchor-center adds anchor-center as a new value; the browsers accept
        // an overflow position before it, as for the other self positions (WPT css/css-anchor-position/anchor-center-safe.html)
        ["align-self"] = "auto | <overflow-position>? [ normal | <self-position> | anchor-center ] | stretch | <baseline-position>",
        ["justify-self"] = "auto | <overflow-position>? [ normal | <self-position> | left | right | anchor-center ] | stretch | <baseline-position>",
        ["container-name"] = "none | <container-name>+",

        // https://drafts.csswg.org/css-display-4/#the-display-properties, with the inner display types of MathML Core, of
        // the grid lanes (CSS Grid 3) and of the CSS Layout API (https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers:
        // "A new alternative value is added to the <display-inside> production: layout(<ident>)"), and the legacy values of
        // https://compat.spec.whatwg.org/#display-property-mappings
        ["display"] = "[ <display-outside> || [ <display-inside> | math | grid-lanes | layout( <ident> ) ] ] | <display-listitem> | <display-internal> | <display-box> | <display-legacy> | inline-grid-lanes | -webkit-box | -webkit-inline-box | -webkit-flex | -webkit-inline-flex",

        // The grammars of the line clamping properties tested by WPT (css/css-overflow/parsing), which follow the resolutions
        // that are not in the css-overflow-4 of webref yet: max-lines is auto (its initial value, none is not valid anymore) or a
        // number of lines with an optional auto (https://drafts.csswg.org/css-overflow-4/#max-lines), block-ellipsis has an
        // ellipsis keyword instead of auto, and the initial value of continue is normal
        ["max-lines"] = "[ <integer [1,\u221E]> || auto ]",
        ["block-ellipsis"] = "no-ellipsis | ellipsis | <string>",
        ["continue"] = "normal | discard | collapse | -webkit-legacy | overflow | paginate | fragments",
        ["line-clamp"] = "none | [ [ <integer [1,\u221E]> || auto ] || <'block-ellipsis'> ] -webkit-legacy?",

        // https://drafts.csswg.org/css-overflow-3/#valdef-overflow-overlay: "User agents must also support the overlay keyword as
        // a legacy value alias of auto" (see CssPropertyRules.CanonicalizeOverflow)
        ["overflow-x"] = "visible | hidden | clip | scroll | auto | overlay",
        ["overflow-y"] = "visible | hidden | clip | scroll | auto | overlay",
        ["overflow-block"] = "visible | hidden | clip | scroll | auto | overlay",
        ["overflow-inline"] = "visible | hidden | clip | scroll | auto | overlay",

        // https://drafts.csswg.org/css-text-4/#white-space-property: the legacy keywords or the longhands
        ["white-space"] = "normal | pre | nowrap | pre-wrap | break-spaces | pre-line | <'white-space-collapse'> || <'text-wrap-mode'>",

        // https://drafts.csswg.org/css-inline-3/#propdef-vertical-align
        ["vertical-align"] = "[ first | last ] || [ baseline | <baseline-metric> ] || <'baseline-shift'>",

        // https://drafts.csswg.org/css-inline-3/#valdef-alignment-baseline-text-before-edge: the SVG 1.1 values text-before-edge
        // and text-after-edge are legacy aliases of text-top and text-bottom (also of dominant-baseline, as in the browsers);
        // they are not allowed in the vertical-align shorthand
        ["alignment-baseline"] = "baseline | <baseline-metric> | text-before-edge | text-after-edge",
        ["dominant-baseline"] = "auto | <baseline-metric> | text-before-edge | text-after-edge",

        // https://drafts.csswg.org/css-text-decor-4/#text-emphasis-position-property
        ["text-emphasis-position"] = "auto | [ over | under ] && [ right | left ]?",

        // https://drafts.csswg.org/css-backgrounds-3/#box-shadow: a longhand, as in the browsers (css-borders-4 turns it into a
        // shorthand of box-shadow-color, box-shadow-offset...)
        ["box-shadow"] = "none | <spread-shadow>#",

        // https://drafts.csswg.org/css-ui-4/#caret-color: one or two values (the color of the caret, then of the character under a block caret)
        ["caret-color"] = "auto | <color> | [ auto | <color> ] [ auto | <color> ]",

        // https://drafts.csswg.org/css-text-3/#valdef-text-justify-distribute: distribute is a legacy alias of inter-character
        ["text-justify"] = "[ auto | none | inter-word | inter-character | ruby | distribute ] || no-compress",

        // https://drafts.csswg.org/css-ruby-1/#ruby-overhang: "user agents must support an additional value of none for the
        // ruby-overhang property, as a legacy value alias of spaces" (see CssPropertyRules.CanonicalizeRubyOverhang)
        ["ruby-overhang"] = "auto | spaces | none",

        // The grammars of the browsers, which differ from the specifications: column-span has no auto (css-multicol-2 adds it,
        // WPT css/css-multicol/parsing/column-span-invalid.html), column-width accepts no percentage (css-multicol-2 makes it a
        // <box-size>, WPT css/css-multicol/parsing/column-width-invalid.html), and a text shadow has no spread nor inset (the
        // <shadow> of css-borders-4, WPT css/css-text-decor/text-shadow/parsing/text-shadow-invalid.html)
        ["column-span"] = "none | <integer [1,\u221E]> | all",
        ["column-width"] = "auto | <length [0,\u221E]> | min-content | max-content | fit-content( <length-percentage [0,\u221E]> )",
        ["text-shadow"] = "none | [ <color>? && [ <length>{2} <length [0,\u221E]>? ] ]#",

        // https://drafts.csswg.org/css-ui-4/#outline-offset: inset (the offset of the outline is the inverse of its width)
        ["outline-offset"] = "<length> | inset",

        ["zoom"] = "normal | <number [0,\u221E]> | <percentage [0,\u221E]>",

        // https://drafts.csswg.org/css-content-3/#quotes-property: the editor's draft of webref has a placeholder keyword
        // (bikeshed-this) instead of match-parent
        ["quotes"] = "auto | none | match-parent | [ <string> <string> ]+",

        // https://drafts.csswg.org/css-overflow-5/#scroll-marker-group-property: the mode keyword follows the position, as in
        // Chrome (the grammar of the specification, "[ before | after ] || [ links | tabs ]", accepts the mode alone and first:
        // WPT css/css-overflow/parsing/scroll-markers-invalid.tentative.html)
        ["scroll-marker-group"] = "none | [ before | after ] [ links | tabs ]?",

        // https://svgwg.org/svg2-draft/geometry.html#R, https://svgwg.org/svg2-draft/geometry.html#RX: "A negative value is invalid
        // and must be ignored"; https://svgwg.org/svg2-draft/painting.html#StrokeProperties: the negative widths, dashes and
        // miter limits are invalid
        ["r"] = "<length-percentage [0,\u221E]>",
        ["rx"] = "<length-percentage [0,\u221E]> | auto",
        ["ry"] = "<length-percentage [0,\u221E]> | auto",
        ["stroke-width"] = "[ <length-percentage [0,\u221E]> | <line-width> | <number [0,\u221E]> ]#",
        ["stroke-dasharray"] = "none | [ <length-percentage [0,\u221E]> | <number [0,\u221E]> ]+#",
        ["stroke-miterlimit"] = "<number [0,\u221E]>",

        // https://svgwg.org/svg2-draft/painting.html#LineJoin: the values of SVG 2 (miter-clip is painted as miter), and the
        // ones of https://drafts.fxtf.org/fill-stroke/#stroke-linejoin
        ["stroke-linejoin"] = "miter-clip | [ crop | arcs | miter ] || [ bevel | round | fallback ]",

        // https://drafts.csswg.org/css-images-3/#the-image-rendering: "This property previously accepted the values optimizeSpeed
        // and optimizeQuality [...] a user agent must accept them as valid values but must treat them as having the same behavior
        // as crisp-edges and smooth respectively"
        ["image-rendering"] = "auto | smooth | high-quality | pixelated | crisp-edges | optimizeSpeed | optimizeQuality",

        // https://svgwg.org/svg2-draft/paths.html#PathLengthProperty (tentative): the range of the length is outside of the
        // brackets of the type in webref ("<length> [0,∞]")
        ["path-length"] = "none | <length [0,\u221E]>",

        // https://drafts.csswg.org/scroll-animations-1/#animation-range: the longhands of animation-range are single values
        // (webref references the comma-separated lists of the longhands)
        ["animation-range"] = "[ [ normal | <length-percentage> | <timeline-range-name> <length-percentage>? ] [ normal | <length-percentage> | <timeline-range-name> <length-percentage>? ]? ]#",

        // https://drafts.csswg.org/css-animations-2/#timeline-trigger: the ranges are optional, and the longhands of the ranges
        // are single values (webref references timeline-trigger-range, a comma-separated list, in each item of the list)
        ["timeline-trigger"] = "none | [ <'timeline-trigger-name'> <'timeline-trigger-source'> [ <'timeline-trigger-activation-range'> [ / <'timeline-trigger-active-range'> ]? ]? ]#",

        // https://drafts.fxtf.org/css-masking/#clip-property: the legacy rect() of clip takes 4 lengths or auto, separated by
        // commas or (like the browsers) by whitespace; the <rect()> of webref is the basic shape
        ["clip"] = "rect( [ <length> | auto ]#{4} ) | rect( [ <length> | auto ]{4} ) | auto",

        // https://drafts.csswg.org/css-anchor-position-1/#anchor-pos, https://drafts.csswg.org/css-anchor-position-1/#anchor-size-fn:
        // anchor() and anchor-size() apply to all the inset properties (webref only adds them to the physical ones)
        ["inset-block-start"] = "auto | <length-percentage> | <anchor()> | <anchor-size()>",
        ["inset-block-end"] = "auto | <length-percentage> | <anchor()> | <anchor-size()>",
        ["inset-inline-start"] = "auto | <length-percentage> | <anchor()> | <anchor-size()>",
        ["inset-inline-end"] = "auto | <length-percentage> | <anchor()> | <anchor-size()>",

        // https://drafts.csswg.org/css-anchor-position-1/#position-visibility: "User agents may recognize anchors-valid and
        // anchors-visible as legacy value aliases of anchor-valid and anchor-visible" (see CssPropertyRules.CanonicalizePositionVisibility)
        ["position-visibility"] = "always | [ [ anchor-valid | anchors-valid ] || [ anchor-visible | anchors-visible ] || no-overflow ]",

        // https://drafts.csswg.org/css-color-adjust-1/#color-scheme-prop: the <custom-ident>s exclude normal and only (see
        // <color-scheme-ident>)
        ["color-scheme"] = "normal | [ light | dark | <color-scheme-ident> ]+ && only?",

        // https://drafts.csswg.org/css-writing-modes-4/#svg-writing-mode: the SVG 1.1 values of writing-mode (computed to
        // horizontal-tb or vertical-rl), and https://drafts.csswg.org/css-writing-modes-4/#valdef-text-orientation-sideways-right:
        // the legacy sideways-right value of text-orientation (computed to sideways), as in the browsers
        ["writing-mode"] = "horizontal-tb | vertical-rl | vertical-lr | sideways-rl | sideways-lr | lr | lr-tb | rl | rl-tb | tb | tb-rl",
        ["text-orientation"] = "mixed | upright | sideways | sideways-right",

        // https://drafts.csswg.org/css-fonts-4/#font-synthesis: oblique-only sets font-synthesis-style (webref does not have it)
        ["font-synthesis"] = "none | [ weight || [ style | oblique-only ] || small-caps || position ]",

        // https://drafts.csswg.org/css-borders-4/#corner-shorthands: the grammar of the corner shorthands of the browsers and of
        // WPT (css/css-borders/corner-shape/corner-*-valid.html), which is newer than "<'border-radius'> || <'corner-shape'>" of
        // webref: each corner is normal (no radius, round) or a radius and a shape (both are required), and the corners are
        // separated by slashes (see CssPropertyRules.ExpandCorner)
        ["corner-top-left"] = CornerGrammar,
        ["corner-top-right"] = CornerGrammar,
        ["corner-bottom-right"] = CornerGrammar,
        ["corner-bottom-left"] = CornerGrammar,
        ["corner-start-start"] = CornerGrammar,
        ["corner-start-end"] = CornerGrammar,
        ["corner-end-start"] = CornerGrammar,
        ["corner-end-end"] = CornerGrammar,
        ["corner-top"] = CornerSideGrammar,
        ["corner-right"] = CornerSideGrammar,
        ["corner-bottom"] = CornerSideGrammar,
        ["corner-left"] = CornerSideGrammar,
        ["corner-block-start"] = CornerSideGrammar,
        ["corner-block-end"] = CornerSideGrammar,
        ["corner-inline-start"] = CornerSideGrammar,
        ["corner-inline-end"] = CornerSideGrammar,
        ["corner"] = "[ " + CornerGrammar + " ] [ / [ " + CornerGrammar + " ] ]{0,3}",
    };

    private const string CornerGrammar = "normal | [ <length-percentage [0,∞]>{1,2} && <corner-shape-value> ]";

    private const string CornerSideGrammar = "[ " + CornerGrammar + " ] [ / [ " + CornerGrammar + " ] ]?";

    /// <summary>The initial values that do not match webref (see <see cref="OverriddenGrammars"/>).</summary>
    private static readonly Dictionary<string, string> OverriddenInitialValues = new(StringComparer.Ordinal)
    {
        ["continue"] = "normal",
        ["max-lines"] = "auto",

        // https://drafts.csswg.org/css-multicol/#cw: webref has no initial value for column-width
        ["column-width"] = "auto",

        // https://drafts.csswg.org/css-text-decor-4/#text-emphasis-position-property
        ["text-emphasis-position"] = "auto",
    };

    /// <summary>The properties whose grammar is not in webref (https://compat.spec.whatwg.org/ lists them without grammar).</summary>
    private static readonly CssPropertyDefinition[] SupplementalProperties =
    [
        // https://compat.spec.whatwg.org/#the-webkit-box-orient-property: used by -webkit-line-clamp (https://drafts.csswg.org/css-overflow-4/#webkit-line-clamp)
        new("-webkit-box-orient", "horizontal | vertical | inline-axis | block-axis", "horizontal", false, [], null, AnimationType: CssAnimationTypeKind.Discrete),

        // https://compat.spec.whatwg.org/#property-mappings: the other properties of the legacy -webkit-box layout, with the
        // grammar, initial value and inheritance of the browsers (-webkit-box-direction is not in the Compatibility Standard,
        // but all the browsers implement it)
        new("-webkit-box-align", "stretch | start | center | end | baseline", "stretch", false, [], null, AnimationType: CssAnimationTypeKind.Discrete),
        new("-webkit-box-direction", "normal | reverse", "normal", true, [], null, AnimationType: CssAnimationTypeKind.Discrete),
        new("-webkit-box-flex", "<number>", "0", false, [], null, AnimationType: CssAnimationTypeKind.ByComputedValue),
        new("-webkit-box-ordinal-group", "<integer [1,\u221E]>", "1", false, [], null, AnimationType: CssAnimationTypeKind.ByComputedValue),
        new("-webkit-box-pack", "start | center | end | justify", "start", false, [], null, AnimationType: CssAnimationTypeKind.Discrete),

        // The legacy name alias of clip-path (https://drafts.csswg.org/css-cascade-5/#legacy-name-alias): it is not in the
        // Compatibility Standard, but all the browsers implement it
        new("-webkit-clip-path", "<clip-source> | [ <basic-shape> || <geometry-box> ] | none", null, false, [], "clip-path"),

        // https://drafts.csswg.org/css-grid-3/#grid-lanes-orientation leaves the orientation of a grid lanes container to a "TBD
        // property" (https://github.com/w3c/csswg-drafts/issues/12803): the properties of the WPT tests (css/css-grid/grid-lanes/tentative/parsing)
        new("grid-lanes-direction", "normal | [ row | column ] [ fill-reverse || track-reverse ]?", "normal", false, [], null, AnimationType: CssAnimationTypeKind.Discrete),
        new("grid-lanes-pack", "normal | dense", "normal", false, [], null, AnimationType: CssAnimationTypeKind.Discrete),
        new("grid-lanes", "none | [ <string> || [ <track-list> | <auto-track-list> ] || <'grid-lanes-direction'> ]", "none", false, ["grid-template-columns", "grid-template-rows", "grid-template-areas", "grid-lanes-direction"], null, AnimationType: CssAnimationTypeKind.Shorthand),

        // https://drafts.csswg.org/css-grid-3/#placement-tolerance: flow-tolerance is the former name of fit-tolerance
        // (https://github.com/w3c/csswg-drafts/issues/10884), kept as a legacy name alias as in WebKit (the WPT tests use both)
        new("flow-tolerance", "normal | <length-percentage [0,\u221E]> | infinite", "normal", false, [], "fit-tolerance"),
    ];

    /// <summary>
    /// The longhands of the shorthands that are incomplete in webref: the bidirectional shorthands of the gap decorations
    /// set the column and row properties (https://drafts.csswg.org/css-gaps/#propdef-rule-inset-cap), font-synthesis sets
    /// font-synthesis-position (https://drafts.csswg.org/css-fonts-4/#font-synthesis), and the page-break-* legacy shorthands
    /// (webref lists them as longhands) set the break-* properties.
    /// </summary>
    private static readonly Dictionary<string, string[]> OverriddenLonghands = new(StringComparer.Ordinal)
    {
        ["box-shadow"] = [],
        ["font-synthesis"] = ["font-synthesis-weight", "font-synthesis-style", "font-synthesis-small-caps", "font-synthesis-position"],
        ["rule-inset-cap"] = ["column-rule-inset-cap", "row-rule-inset-cap"],
        ["rule-inset-junction"] = ["column-rule-inset-junction", "row-rule-inset-junction"],
        ["rule-visibility-items"] = ["column-rule-visibility-items", "row-rule-visibility-items"],

        // https://drafts.csswg.org/css-break-4/#page-break-properties: legacy shorthands of the break properties
        ["page-break-before"] = ["break-before"],
        ["page-break-after"] = ["break-after"],
        ["page-break-inside"] = ["break-inside"],

        // https://drafts.csswg.org/css-animations-2/#animation-shorthand: animation also resets animation-timeline and the
        // longhands of animation-range, like the browsers (they do not reset animation-composition and animation-trigger)
        ["animation"] = ["animation-duration", "animation-timing-function", "animation-delay", "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state", "animation-name", "animation-timeline", "animation-range-start", "animation-range-end"],

        // https://drafts.csswg.org/scroll-animations-1/#view-timeline-shorthand: view-timeline also sets view-timeline-inset
        ["view-timeline"] = ["view-timeline-name", "view-timeline-axis", "view-timeline-inset"],

        // https://drafts.csswg.org/css-animations-2/#timeline-trigger (see OverriddenGrammars)
        ["timeline-trigger"] = ["timeline-trigger-name", "timeline-trigger-source", "timeline-trigger-activation-range", "timeline-trigger-active-range"],
    };

    private static readonly FrozenDictionary<string, CssPropertyDefinition> ByName = CreatePropertyMap();
    private static readonly ConcurrentDictionary<string, List<string>> LonghandsCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, GrammarNode?> Grammars = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, GrammarNode?> TypeGrammars = new(StringComparer.Ordinal);
    private static readonly Lazy<FrozenDictionary<string, List<string>>> ShorthandsByLonghand = new(ComputeShorthandsByLonghand);
    // Lazy: Descriptors is declared in another part of the class, whose initialization order is not defined
    private static readonly Lazy<FrozenDictionary<string, CssDescriptorDefinition>> DescriptorsByName = new(() => Descriptors.ToFrozenDictionary(d => d.AtRule + " " + d.Name, StringComparer.Ordinal));
    private static readonly ConcurrentDictionary<(string AtRule, string Name), GrammarNode?> DescriptorGrammars = new();

    /// <summary>Returns the descriptor of an at-rule (e.g. "@font-face", "font-family"), or null when it is not supported.</summary>
    public static CssDescriptorDefinition? GetDescriptor(string atRule, string name)
    {
        // https://drafts.csswg.org/css-fonts-5/#font-stretch-desc: font-stretch is a legacy name alias of font-width
        if (atRule == "@font-face" && name == "font-stretch")
        {
            name = "font-width";
        }

        // https://drafts.csswg.org/css-page-3/#page-margin-safety: page-margin-safety is also a descriptor of the margin rules
        if (name == "page-margin-safety" && atRule.Length > 1 && Om.CssRuleParser.IsMarginRuleName(atRule[1..]))
        {
            atRule = "@page";
        }

        return DescriptorsByName.Value.GetValueOrDefault(atRule + " " + name);
    }

    /// <summary>Returns the grammar of a descriptor, or null when it cannot be parsed.</summary>
    public static GrammarNode? GetGrammar(CssDescriptorDefinition descriptor) => DescriptorGrammars.GetOrAdd((descriptor.AtRule, descriptor.Name), static (_, descriptor) => TryParse(descriptor.Grammar), descriptor);

    public static string[] GetResetOnlyLonghands(string shorthand) => ResetOnlyLonghands.GetValueOrDefault(shorthand, []);

    /// <summary>
    /// The types of the grammar of a shorthand that set a longhand, like a property reference
    /// (https://drafts.csswg.org/css-fonts/#font-prop: &lt;font-variant-css2&gt; sets font-variant, &lt;font-width-css3&gt; sets font-width).
    /// </summary>
    private static readonly Dictionary<string, string> TypeLonghands = new(StringComparer.Ordinal)
    {
        ["font-variant-css2"] = "font-variant",
        ["font-width-css3"] = "font-width",
    };

    /// <summary>Returns the longhand set by a type of the grammar of a shorthand, or null.</summary>
    public static string? GetTypeLonghand(string type) => TypeLonghands.GetValueOrDefault(type);

    private static FrozenDictionary<string, CssPropertyDefinition> CreatePropertyMap()
    {
        var map = PropertyDefinitions.ToDictionary(p => p.Name, StringComparer.Ordinal);
        foreach (var property in SupplementalProperties)
        {
            map[property.Name] = property;
        }

        // The longhands are referenced by their name, not by a legacy alias (e.g. font-stretch is an alias of font-width)
        foreach (var property in PropertyDefinitions)
        {
            if (property.Longhands.Any(l => map.TryGetValue(l, out var longhand) && longhand.AliasOf is not null))
            {
                map[property.Name] = property with { Longhands = [.. property.Longhands.Select(l => map.TryGetValue(l, out var longhand) && longhand.AliasOf is { } target ? target : l)] };
            }
        }

        foreach (var (name, grammar) in OverriddenGrammars)
        {
            if (map.TryGetValue(name, out var definition))
            {
                map[name] = definition with { Grammar = grammar };
            }
        }

        foreach (var (name, initial) in OverriddenInitialValues)
        {
            if (map.TryGetValue(name, out var definition))
            {
                map[name] = definition with { Initial = initial };
            }
        }

        foreach (var (shorthand, longhands) in OverriddenLonghands)
        {
            if (map.TryGetValue(shorthand, out var definition))
            {
                map[shorthand] = definition with { Longhands = longhands };
            }
        }

        foreach (var (shorthand, longhands) in ResetOnlyLonghands)
        {
            if (map.TryGetValue(shorthand, out var definition))
            {
                map[shorthand] = definition with { Longhands = [.. definition.Longhands, .. longhands] };
            }
        }

        // https://drafts.csswg.org/css-cascade/#all-shorthand: all the properties except direction, unicode-bidi and the custom properties
        if (map.TryGetValue("all", out var all))
        {
            var longhands = PropertyDefinitions.Concat(SupplementalProperties).Select(p => map[p.Name]).Where(p => p.AliasOf is null && p.Longhands.Length == 0 && p.Name is not ("all" or "direction" or "unicode-bidi")).Select(p => p.Name).ToArray();
            map["all"] = all with { Longhands = longhands };
        }

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The index of each property name (including the aliases), to store values by property in arrays (see <see cref="GetIndex"/>).</summary>
    private static readonly Lazy<FrozenDictionary<string, int>> IndexByName = new(() =>
    {
        var indexes = new Dictionary<string, int>(ByName.Count, StringComparer.Ordinal);
        foreach (var name in ByName.Keys)
        {
            indexes.Add(name, indexes.Count);
        }

        return indexes.ToFrozenDictionary(StringComparer.Ordinal);
    });

    /// <summary>The number of property names (the indexes returned by <see cref="GetIndex"/> are lower).</summary>
    public static int PropertyNameCount => ByName.Count;

    /// <summary>Returns the index of a property name (in [0, <see cref="PropertyNameCount"/>)), or -1 when it is not a supported property.</summary>
    public static int GetIndex(string name) => IndexByName.Value.TryGetValue(name, out var index) ? index : -1;

    /// <summary>All the supported properties (https://drafts.csswg.org/cssom/#supported-css-property).</summary>
    public static IEnumerable<CssPropertyDefinition> Properties => ByName.Values;

    /// <summary>Returns the property with the name (ASCII lowercase), resolving the legacy aliases.</summary>
    public static CssPropertyDefinition? Get(string name)
    {
        if (!ByName.TryGetValue(name, out var property))
            return null;

        return property.AliasOf is { } alias && ByName.TryGetValue(alias, out var target) ? target : property;
    }

    /// <summary>Returns whether the name is a custom property (https://drafts.csswg.org/css-variables/#custom-property).</summary>
    public static bool IsCustomProperty(string name) => name.Length > 2 && name.StartsWith("--", StringComparison.Ordinal);

    /// <summary>Returns the shorthands that contain the longhand, the longest first.</summary>
    public static List<string> GetShorthands(string longhand) => ShorthandsByLonghand.Value.TryGetValue(longhand, out var shorthands) ? shorthands : [];

    private static FrozenDictionary<string, List<string>> ComputeShorthandsByLonghand()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var property in ByName.Values)
        {
            if (property.AliasOf is not null)
                continue;

            foreach (var longhand in GetLonghandsRecursive(property))
            {
                if (!result.TryGetValue(longhand, out var list))
                {
                    list = [];
                    result[longhand] = list;
                }

                list.Add(property.Name);
            }
        }

        // https://drafts.csswg.org/cssom/#concept-shorthands-preferred-order: the shorthands with the most longhands first
        foreach (var list in result.Values)
        {
            list.Sort((a, b) =>
            {
                var countA = GetLonghandsRecursive(ByName[a]).Count;
                var countB = GetLonghandsRecursive(ByName[b]).Count;
                if (countA != countB)
                    return countB.CompareTo(countA);

                var prefixA = a.StartsWith('-', StringComparison.Ordinal);
                var prefixB = b.StartsWith('-', StringComparison.Ordinal);
                if (prefixA != prefixB)
                    return prefixA ? 1 : -1;

                return string.CompareOrdinal(a, b);
            });
        }

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The longhands of a shorthand, expanding the nested shorthands (e.g. border → border-top-width...).</summary>
    public static List<string> GetLonghandsRecursive(CssPropertyDefinition property) => LonghandsCache.GetOrAdd(property.Name, static (_, property) => ComputeLonghandsRecursive(property), property);

    private static List<string> ComputeLonghandsRecursive(CssPropertyDefinition property)
    {
        var result = new List<string>();
        foreach (var longhand in property.Longhands)
        {
            if (Get(longhand) is { IsShorthand: true } nested && !ReferenceEquals(nested, property))
            {
                result.AddRange(GetLonghandsRecursive(nested));
            }
            else
            {
                result.Add(longhand);
            }
        }

        return result;
    }

    /// <summary>Returns the grammar of the property, or null when it cannot be parsed.</summary>
    public static GrammarNode? GetGrammar(CssPropertyDefinition property) => Grammars.GetOrAdd(property.Name, static (_, property) => TryParse(property.Grammar), property);

    /// <summary>Returns the grammar of a type (e.g. "&lt;bg-layer&gt;" or "rgb()"), or null when it is not defined.</summary>
    public static GrammarNode? GetTypeGrammar(string name) => TypeGrammars.GetOrAdd(name, static name => OverriddenTypes.TryGetValue(name, out var grammar) || Types.TryGetValue(name, out grammar) || SupplementalTypes.TryGetValue(name, out grammar) ? TryParse(grammar) : null);

    /// <summary>
    /// The types whose definition in webref does not match the properties that use them: fill and stroke are defined by
    /// SVG 2, whose &lt;paint&gt; accepts colors, while webref has the &lt;paint&gt; of CSS Fill and Stroke (where the colors
    /// are set by fill-color and stroke-color); the browsers do not accept a fill rule in the path() of offset-path.
    /// </summary>
    private static readonly Dictionary<string, string> OverriddenTypes = new(StringComparer.Ordinal)
    {
        // https://svgwg.org/svg2-draft/painting.html#SpecifyingPaint
        ["<paint>"] = "none | <color> | <url> [ none | <color> ]? | context-fill | context-stroke",

        // https://drafts.csswg.org/motion-1/#typedef-offset-path: <ray()> | <url> | <basic-shape>, but the browsers reject the
        // fill rule of path(), which does not apply to a motion path (WPT css/motion/parsing/offset-path-parsing-invalid.html)
        ["<offset-path>"] = "<ray()> | <url> | <basic-shape-rect> | <circle()> | <ellipse()> | <polygon()> | path( <string> ) | <shape()>",

        // https://drafts.csswg.org/css-inline-3/#typedef-text-edge: "If only one value is specified, both edges are assigned that
        // same keyword if possible; else text is assumed as the missing value": the browsers accept a single cap, ex or
        // alphabetic (WPT css/css-inline/text-box-trim/parsing/text-box-edge-valid.html)
        ["<text-edge>"] = "[ text | ideographic | ideographic-ink | cap | ex | alphabetic ] | [ text | ideographic | ideographic-ink | cap | ex ] [ text | ideographic | ideographic-ink | alphabetic ]",

        // https://drafts.csswg.org/css-sizing-4/#sizing-values, with the legacy names of stretch that the browsers accept in the
        // sizing properties (-webkit-fill-available in Blink, WebKit and Gecko, -moz-available in Gecko: WPT
        // css/css-sizing/stretch/stretch-alias-*.tentative.html); they behave as stretch (see Layout.Size.Parse)
        ["<box-size>"] = "<length-percentage [0,\u221E]> | stretch | contain | min-content | max-content | fit-content | fit-content(<length-percentage [0,\u221E]>) | <calc-size()> | -webkit-fill-available | -moz-available",

        // https://drafts.csswg.org/css-align-3/#typedef-baseline-position: first or last precedes baseline (the browsers reject
        // baseline first), and the flow-relative positions (https://drafts.csswg.org/css-align-3/#valdef-self-position-flow-start)
        ["<baseline-position>"] = "[ first | last ]? baseline",
        ["<self-position>"] = "center | start | end | self-start | self-end | flex-start | flex-end | flow-start | flow-end",
        ["<content-position>"] = "center | start | end | flex-start | flex-end | flow-start | flow-end",

        // https://drafts.fxtf.org/filter-effects/#supported-filter-functions: the negative values are invalid
        ["blur()"] = "blur( <length [0,\u221E]>? )",
        ["brightness()"] = "brightness( [ <number [0,\u221E]> | <percentage [0,\u221E]> ]? )",
        ["contrast()"] = "contrast( [ <number [0,\u221E]> | <percentage [0,\u221E]> ]? )",
        ["grayscale()"] = "grayscale( [ <number [0,\u221E]> | <percentage [0,\u221E]> ]? )",
        ["invert()"] = "invert( [ <number [0,\u221E]> | <percentage [0,\u221E]> ]? )",
        ["opacity()"] = "opacity( [ <number [0,\u221E]> | <percentage [0,\u221E]> ]? )",
        ["saturate()"] = "saturate( [ <number [0,\u221E]> | <percentage [0,\u221E]> ]? )",
        ["sepia()"] = "sepia( [ <number [0,\u221E]> | <percentage [0,\u221E]> ]? )",

        // https://drafts.csswg.org/css-animations-1/#animation-shorthand: the time of the animation duration and of the delay,
        // like the browsers (css-animations-2 has <'animation-delay-start'> and the reset-only animation-timeline)
        ["<single-animation>"] = "[ auto | <time [0s,\u221E]> ] || <easing-function> || <time> || <single-animation-iteration-count> || <single-animation-direction> || <single-animation-fill-mode> || <single-animation-play-state> || [ none | <keyframes-name> ]",

        // https://drafts.csswg.org/css-transitions-1/#single-transition: the first time is the non-negative duration
        ["<single-transition>"] = "[ none | <single-transition-property> ] || <time [0s,\u221E]> || <easing-function> || <time> || <transition-behavior-value>",

        // https://drafts.csswg.org/css-easing-2/#step-easing-functions: the number of steps is at least 1, and at least 2 with jump-none
        ["steps()"] = "steps( [ <integer [1,\u221E]> [ , [ jump-start | jump-end | jump-both | start | end ] ]? ] | [ <integer [2,\u221E]> , jump-none ] )",

        // https://drafts.csswg.org/css-easing-2/#linear-easing-function-parsing: there are at least two linear stops
        ["linear()"] = "linear( [ <number> && <percentage>{0,2} ]#{2,} )",

        // https://drafts.csswg.org/scroll-animations-1/#named-range-keyframes: a keyframe selector can be a timeline range
        // name with a percentage
        ["<keyframe-selector>"] = "from | to | <percentage [0,100]> | <timeline-range-name> <percentage>",

        // https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-polygon: the rounding radius is not negative
        ["polygon()"] = "polygon( <'fill-rule'>? [ round <length [0,\u221E]> ]? , [<length-percentage> <length-percentage>]# )",

        // https://drafts.fxtf.org/css-masking/#the-mask: the boxes of a layer set mask-origin and mask-clip, whose <coord-box>
        // does not accept the <shape-box> margin-box and half-border-box of <geometry-box> (WPT css/css-masking/parsing/mask-invalid.html)
        ["<mask-layer>"] = "<mask-reference> || <position> [ / <bg-size> ]? || <repeat-style> || <coord-box> || [ <coord-box> | no-clip ] || <compositing-operator> || <masking-mode>",

        // https://drafts.csswg.org/css-color-5/#light-dark: light-dark() is also an <image> (css-images does not list it yet);
        // https://drafts.fxtf.org/filter-effects/#FilterCSSImageValue: "the filter() function [...] is an <image>"
        // https://drafts.css-houdini.org/css-paint-api-1/#paint-notation: "the paint() function is an additional notation to be
        // supported by the <image> type"; its comma is only required before the arguments (paint(name) is valid)
        ["<image>"] = "<url> | <image()> | <image-set()> | <-webkit-image-set()> | <cross-fade()> | <element()> | <gradient> | <light-dark-image> | <filter()> | <paint()>",
        ["paint()"] = "paint( <ident> [ , <declaration-value>? ]? )",

        // https://drafts.csswg.org/css-images-4/#image-set-notation: "the image-set() function can not be nested inside of itself,
        // either directly or indirectly", and the resolutions are not negative; -webkit-image-set() is a parse-time alias
        // (https://drafts.csswg.org/css-images-4/#deprecated)
        ["<image-set-option>"] = "[ <url> | <image()> | <cross-fade()> | <element()> | <gradient> | <light-dark-image> | <filter()> | <string> ] [ <resolution [0,\u221E]> || type(<string>) ]?",
        ["-webkit-image-set()"] = "-webkit-image-set( <image-set-option># )",

        // https://compat.spec.whatwg.org/#css-gradient-functions: the -webkit- prefixed gradients, and -webkit-gradient()
        // (implemented by all the browsers)
        ["<gradient>"] = "[ <linear-gradient()> | <repeating-linear-gradient()> | <radial-gradient()> | <repeating-radial-gradient()> | <conic-gradient()> | <repeating-conic-gradient()> | <-webkit-linear-gradient()> | <-webkit-repeating-linear-gradient()> | <-webkit-radial-gradient()> | <-webkit-repeating-radial-gradient()> | <-webkit-gradient()> ]",
    };

    /// <summary>The types that are defined in prose by the specifications (they are not in webref).</summary>
    private static readonly Dictionary<string, string> SupplementalTypes = new(StringComparer.Ordinal)
    {
        // https://drafts.csswg.org/css-lists-3/#typedef-symbolic-glyph: the former name of <symbolic-glyph>, still used by the
        // <content-list> of css-content-3
        ["<symbolic-counter-style>"] = "<symbolic-glyph>",

        // https://drafts.csswg.org/css-page-3/#typedef-page-size-page-size
        ["<page-size>"] = "a5 | a4 | a3 | b5 | b4 | jis-b5 | jis-b4 | letter | legal | ledger",

        // https://drafts.csswg.org/scroll-animations-1/#typedef-timeline-range-name
        ["<timeline-range-name>"] = "cover | contain | entry | exit | entry-crossing | exit-crossing | scroll",

        // https://drafts.csswg.org/css-shapes/#basic-shape-functions: a circle has one radius and an ellipse two, which are
        // not negative (https://drafts.csswg.org/css-shapes/#typedef-shape-radial-radius), or a <radial-extent> keyword
        // (https://drafts.csswg.org/css-images-3/#typedef-radial-size, the sides and the corners:
        // https://drafts.csswg.org/css-shapes-2/#funcdef-basic-shape-circle adds closest-corner and farthest-corner)
        ["circle()"] = "circle( <radial-radius>? [ at <position> ]? )",
        ["ellipse()"] = "ellipse( [ <radial-radius>{2} ]? [ at <position> ]? )",
        ["<radial-radius>"] = "<length-percentage [0,\u221E]> | <radial-extent>",

        // https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-rect
        ["rect()"] = "rect( [ <length-percentage> | auto ]{4} [ round <'border-radius'> ]? )",

        // https://drafts.csswg.org/css-color-adjust-1/#color-scheme-prop: the <custom-ident> of color-scheme (the excluded
        // keywords are in ValueMatcher)
        ["<color-scheme-ident>"] = "<custom-ident>",

        // https://compat.spec.whatwg.org/#css-gradient-functions: the prefixed gradients are the gradients of
        // https://www.w3.org/TR/2011/WD-css3-images-20110217/#gradients: the direction is the starting point (without
        // "to"), the position and the size of a radial gradient are separated by commas, the size is a keyword or two
        // non-negative lengths, and the color stops have at most one position and no color hint
        ["-webkit-linear-gradient()"] = "-webkit-linear-gradient( [ <angle> | <zero> | <side-or-corner> ]? , <-webkit-color-stop>#{2,} )",
        ["-webkit-repeating-linear-gradient()"] = "-webkit-repeating-linear-gradient( [ <angle> | <zero> | <side-or-corner> ]? , <-webkit-color-stop>#{2,} )",
        ["-webkit-radial-gradient()"] = "-webkit-radial-gradient( <position>? , [ [ <radial-shape> || <-webkit-radial-extent> ] | <length-percentage [0,\u221E]>{2} ]? , <-webkit-color-stop>#{2,} )",
        ["-webkit-repeating-radial-gradient()"] = "-webkit-repeating-radial-gradient( <position>? , [ [ <radial-shape> || <-webkit-radial-extent> ] | <length-percentage [0,\u221E]>{2} ]? , <-webkit-color-stop>#{2,} )",
        ["<-webkit-radial-extent>"] = "<radial-extent> | contain | cover",
        ["<-webkit-color-stop>"] = "<color> <length-percentage>?",

        // The legacy WebKit gradient (https://webkit.org/blog/175/introducing-css-gradients/), as parsed by the browsers:
        // two points (and two radii for a radial gradient), then from(), to() and color-stop() color stops
        ["-webkit-gradient()"] = "-webkit-gradient( [ linear , <-webkit-gradient-point> , <-webkit-gradient-point> | radial , <-webkit-gradient-point> , <number [0,\u221E]> , <-webkit-gradient-point> , <number [0,\u221E]> ] [ , <-webkit-gradient-color-stop> ]* )",
        ["<-webkit-gradient-point>"] = "[ left | center | right | <number> | <percentage> ] [ top | center | bottom | <number> | <percentage> ]",
        ["<-webkit-gradient-color-stop>"] = "from( <color> ) | to( <color> ) | color-stop( [ <number> | <percentage> ] , <color> )",
    };

    private static GrammarNode? TryParse(string grammar)
    {
        if (grammar.Length == 0)
            return null;

        try
        {
            return ValueGrammarParser.Parse(grammar);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/cssom/#css-property-to-idl-attribute: the IDL attribute names of a property (camel-cased,
    /// webkit-cased and dashed attributes).
    /// </summary>
    public static IEnumerable<string> GetIdlAttributeNames(string property)
    {
        yield return ToCamelCase(property, lowercaseFirst: false);
        if (property.StartsWith("-webkit-", StringComparison.Ordinal))
        {
            yield return ToCamelCase(property[1..], lowercaseFirst: true);
        }

        if (property.Contains('-', StringComparison.Ordinal))
        {
            yield return property;
        }
    }

    private static string ToCamelCase(string property, bool lowercaseFirst)
    {
        var builder = new System.Text.StringBuilder(property.Length);
        var uppercaseNext = false;
        foreach (var c in property)
        {
            if (c == '-')
            {
                uppercaseNext = true;
                continue;
            }

            builder.Append(uppercaseNext ? char.ToUpperInvariant(c) : c);
            uppercaseNext = false;
        }

        if (lowercaseFirst && builder.Length > 0)
        {
            builder[0] = char.ToLowerInvariant(builder[0]);
        }

        return builder.ToString();
    }

    /// <summary>Returns the property name of an IDL attribute name (the reverse of <see cref="GetIdlAttributeNames"/>).</summary>
    public static string? GetPropertyNameFromIdlAttribute(string attribute)
    {
        if (attribute.Contains('-', StringComparison.Ordinal))
            return Get(attribute) is not null ? attribute : null;

        var builder = new System.Text.StringBuilder(attribute.Length + 4);
        foreach (var c in attribute)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                builder.Append('-').Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        var name = builder.ToString();
        if (name.StartsWith("webkit-", StringComparison.Ordinal))
        {
            name = "-" + name;
        }

        return ByName.ContainsKey(name) ? name : null;
    }

    public static string NormalizeName(string name) => IsCustomProperty(name) ? name : InfraAscii.ToAsciiLowercase(name);
}
