using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The style of an element (or of a pseudo-element): its cascaded values, and its computed values
/// (https://drafts.csswg.org/css-cascade-5/#computed), computed on demand.
/// </summary>
internal sealed partial class ComputedStyle
{
    /// <summary>
    /// The properties that apply to the highlight pseudo-elements (https://drafts.csswg.org/css-pseudo-4/#highlight-styling):
    /// color, background-color, text-decoration and its associated properties, text-shadow, stroke-color, fill-color,
    /// stroke-width, and the custom properties; and fill and stroke, the paints of the SVG texts (stroke-color and fill-color
    /// are the colors of the fill and stroke shorthands of CSS Fill and Stroke).
    /// </summary>
    private static readonly FrozenSet<string> HighlightProperties = new[]
    {
        "color", "background-color", "text-shadow", "stroke-color", "fill-color", "stroke-width", "fill", "stroke",
        "text-decoration-line", "text-decoration-style", "text-decoration-color", "text-decoration-thickness", "text-decoration-inset",
        "text-decoration-skip", "text-decoration-skip-box", "text-decoration-skip-ink", "text-decoration-skip-self", "text-decoration-skip-spaces",
        "text-underline-offset", "text-underline-position",
    }.ToFrozenSet(StringComparer.Ordinal);

    private const int MaxUnitDependencyDepth = 32;

    /// <summary>The result of <see cref="GetUnitCycle"/> when there is no cycle (never modified).</summary>
    private static readonly HashSet<string> NoUnitCycle = new(StringComparer.Ordinal);

    /// <summary>The maximum number of rollbacks of the cascade by the keywords that result from substitutions (e.g. a cycle of revert-rule).</summary>
    private const int MaxSubstitutionRollbacks = 32;

    private Dictionary<string, CascadedValue> _cascaded;

    /// <summary>Whether <see cref="_cascaded"/> is shared with other styles: it is copied before it is modified.</summary>
    private bool _cascadedShared;
    private ComputedValues _computed;
    private RareData? _rareData;
    private LayoutStyle? _layout;

    /// <summary>The styles derived from this style by the box tree (see <see cref="GetDerivedStyle"/>), or null.</summary>
    private List<(object Key, ComputedStyle Style)>? _derivedStyles;
    private bool _logicalPropertiesResolved;
    private double _fontSize = double.NaN;
    private double _lineHeight = double.NaN;

    /// <summary>How the font size was specified (see <see cref="FontSizeOrigin"/>), set when font-size is computed.</summary>
    private FontSizeOrigin _fontSizeOrigin;

    /// <summary>
    /// Whether the specified display is <c>-webkit-box</c> or <c>-webkit-inline-box</c> with <c>-webkit-box-orient: vertical</c> and
    /// a <c>continue</c> that clamps, so it computes to <c>flow-root</c> or <c>inline-block</c> (set when the display is computed):
    /// <c>continue: -webkit-legacy</c> only takes effect on such a box (https://drafts.csswg.org/css-overflow-4/#valdef-continue--webkit-legacy).
    /// </summary>
    internal bool IsLegacyLineClampBox { get; set; }

    /// <param name="cascadedShared">Whether the cascaded values are shared with other styles (they are copied before they are modified).</param>
    /// <param name="animated">Whether the animations and the transitions of the element apply to the style.</param>
    /// <param name="unvisitedStyle">The computed style of the element when the style is its visited style (see <see cref="VisitedOrSelf"/>), or null.</param>
    public ComputedStyle(StyleEngine engine, Element element, string? pseudoElement, ComputedStyle? parent, Dictionary<string, CascadedValue> cascaded, bool cascadedShared = false, bool animated = true, ComputedStyle? unvisitedStyle = null)
    {
        Engine = engine;
        Environment = engine.Environment;
        RegisteredProperties = engine.RegisteredProperties;
        Element = element;
        PseudoElement = pseudoElement;
        Parent = parent;
        SelectStyle = pseudoElement is null && element is Dom.HtmlSelectElement ? this : parent?.SelectStyle;
        IsHighlight = StyleEngine.IsHighlightPseudoElement(pseudoElement);
        _cascaded = cascaded;
        _cascadedShared = cascadedShared;
        InitialCascade = cascaded;
        _hasOverlayAutoRule = pseudoElement is null && element.NodeDocument.HasOverlayAutoRule(element);
        if (unvisitedStyle is not null)
        {
            // The visited style has the animations of the style of the element
            _unvisitedStyle = unvisitedStyle;
            VisitedLink = unvisitedStyle.VisitedLink;
            _effects = unvisitedStyle._effects;
            _smilAnimations = unvisitedStyle._smilAnimations;
            return;
        }

        VisitedLink = GetVisitedLink(engine, element, pseudoElement, parent);
        if (animated)
        {
            _effects = engine.Document.AnimationsOrNull?.GetElementAnimations(element)?.Get(pseudoElement);
            _smilAnimations = pseudoElement is null ? engine.Document.SmilOrNull?.GetCssAnimations(element) : null;
        }
    }

    public StyleEngine Engine { get; }

    /// <summary>
    /// The cascaded values the style was created with (the logical properties are mapped to the physical ones in a copy): the
    /// style engine compares them with the cascaded values of the element after a change, to keep the style when they are the
    /// same (see <c>StyleEngine.Invalidation.cs</c>).
    /// </summary>
    internal Dictionary<string, CascadedValue> InitialCascade { get; }

    /// <summary>The version of the styles of the engine (<see cref="StyleEngine.Version"/>) when the style was computed by the engine.</summary>
    internal int Generation { get; set; }

    /// <summary>
    /// Creates a style of the same element with the cascaded values of this style, some of them being replaced: the style of a
    /// box of the element that does not use all its properties (see <see cref="StyleEngine.CreateFieldsetStyles"/>). The values
    /// win over the logical properties of their group.
    /// </summary>
    internal ComputedStyle CreateOverriddenStyle(ReadOnlySpan<(string Name, string Value)> overrides)
    {
        var cascaded = new Dictionary<string, CascadedValue>(_cascaded, StringComparer.Ordinal);
        foreach (var (name, value) in overrides)
        {
            cascaded[name] = new CascadedValue(new CssLonghandValue(name, value), long.MaxValue, source: null);
        }

        return WithVisitedCascadeOf(new ComputedStyle(Engine, Element, PseudoElement, Parent, cascaded) { HighlightParent = HighlightParent });
    }

    /// <summary>The environment of the document when the style was computed (the styles are recomputed when it changes).</summary>
    public MediaEnvironment Environment { get; }

    /// <summary>The registered custom properties of the document when the style was computed.</summary>
    public Dictionary<string, CustomPropertyRegistration> RegisteredProperties { get; }

    public Element Element { get; }

    /// <summary>The pseudo-element (e.g. "before"), or null for the element itself.</summary>
    public string? PseudoElement { get; }

    /// <summary>
    /// The style of the nearest select element the style is in (the style itself for a select element), or null: the rules of
    /// the user agent of the select element depend on its rendering (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2).
    /// </summary>
    internal ComputedStyle? SelectStyle { get; }

    /// <summary>The style the properties are inherited from: the parent in the flat tree, or the originating element of a pseudo-element.</summary>
    public ComputedStyle? Parent { get; }

    /// <summary>Whether the style is the style of a highlight pseudo-element (e.g. <c>::selection</c>, <c>::highlight(name)</c>).</summary>
    public bool IsHighlight { get; }

    /// <summary>Whether declarations apply to the style (rules, presentational hints or the style attribute): false for a pseudo-element that no rule styles.</summary>
    public bool HasCascadedValues => _cascaded.Count > 0 || _effects is not null || _smilAnimations is not null;

    /// <summary>Whether animations or transitions apply to the style.</summary>
    public bool HasAnimationEffects => _effects is not null;

    /// <summary>
    /// The style of the same highlight pseudo-element of the parent element in the flat tree, which the applicable properties
    /// inherit from (https://drafts.csswg.org/css-pseudo-4/#highlight-cascade), or null for the root element and the other pseudo-elements.
    /// </summary>
    public ComputedStyle? HighlightParent { get; init; }

    /// <summary>Whether the element is the root element (https://drafts.csswg.org/selectors-4/#root-pseudo).</summary>
    public bool IsRoot => PseudoElement is null && Element.ParentNode is Document;

    /// <summary>The style of the root element (for the rem and rlh units), or null for the root element itself.</summary>
    internal ComputedStyle? RootStyle
    {
        get
        {
            if (IsRoot)
                return null;

            var style = this;
            while (style.Parent is { } parent)
            {
                style = parent;
            }

            return style.IsRoot ? style : null;
        }
    }

    /// <summary>The computed font size in pixels.</summary>
    public double FontSize => double.IsNaN(_fontSize) ? _fontSize = CssValueComputer.ParsePixels(GetComputedValue("font-size")) ?? CssValueComputer.MediumFontSize : _fontSize;

    /// <summary>The font size of the parent (the initial font size for the root).</summary>
    public double ParentFontSize => Parent?.FontSize ?? CssValueComputer.MediumFontSize;

    /// <summary>
    /// The value of currentcolor: the computed color, or the inherited color while the color property is computed (e.g. when
    /// it references a registered custom property whose value is currentcolor).
    /// </summary>
    public string CurrentColor => IsComputing("color") && Parent is { } parent ? parent.GetComputedValue("color") : GetComputedValue("color");

    /// <summary>The font size of the root element.</summary>
    public double RootFontSize => RootStyle?.FontSize ?? (IsRoot && !IsComputing("font-size") ? FontSize : CssValueComputer.MediumFontSize);

    /// <summary>The used line height in pixels (normal is computed from the metrics of the first available font).</summary>
    public double LineHeight => double.IsNaN(_lineHeight) ? _lineHeight = ComputeLineHeightPixels() : _lineHeight;

    public double ParentLineHeight => Parent?.LineHeight ?? InitialLineHeight;

    public double RootLineHeight => RootStyle?.LineHeight ?? (IsRoot && !IsComputing("line-height") ? LineHeight : InitialLineHeight);

    /// <summary>
    /// The typed view of the computed values used by the layout (https://drafts.csswg.org/css-cascade-5/#used), created on
    /// first use. The style is immutable once computed, so the view lives as long as the style.
    /// </summary>
    internal LayoutStyle Layout => _layout ??= new LayoutStyle(this);

    /// <summary>
    /// Returns the style of the element with additional declarations that win over the cascaded values, e.g. to compute a
    /// value with respect to a query container (https://drafts.csswg.org/css-conditional-5/#style-container).
    /// </summary>
    internal ComputedStyle WithDeclarations(List<CssLonghandValue> declarations)
    {
        var cascaded = new Dictionary<string, CascadedValue>(_cascaded, StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            cascaded[declaration.Name] = new CascadedValue(declaration, long.MaxValue, source: null);
        }

        return WithVisitedCascadeOf(new ComputedStyle(Engine, Element, PseudoElement, Parent, cascaded) { HighlightParent = HighlightParent });
    }

    /// <summary>
    /// The style an anchor-positioned style was derived from (https://drafts.csswg.org/css-anchor-position-1/#fallback-base-styles):
    /// the computed style of the element, before the position options and the anchor functions resolved by the layout
    /// (<see cref="WithPositionOption"/>); null for the computed style of the element.
    /// </summary>
    internal ComputedStyle? PositionBaseStyle { get; private init; }

    /// <summary>
    /// The properties whose anchor functions were resolved by the layout (see <see cref="WithPositionOption"/>): the resolved values
    /// are computed from the animated values (e.g. the interpolation of two anchor functions), so they replace the animation and
    /// transition origins too.
    /// </summary>
    private HashSet<string>? ResolvedAnchorProperties { get; init; }

    /// <summary>
    /// Returns the style of an absolutely positioned element with declarations of the position fallback origin
    /// (https://drafts.csswg.org/css-anchor-position-1/#position-fallback-origin, between the author origin and the animation origin):
    /// a position option, or the lengths the layout resolved the anchor functions to (https://drafts.csswg.org/css-anchor-position-1/#anchor-resolution).
    /// The declarations win over the cascaded values, in order, except over the important ones.
    /// </summary>
    /// <param name="declarations">The declarations (a null value removes the declaration: the property is unset).</param>
    /// <param name="baseStyle">The computed style of the element (<see cref="PositionBaseStyle"/>).</param>
    /// <param name="resolvesValues">Whether the declarations are the cascaded values with their anchor functions resolved (they replace the important values too).</param>
    internal ComputedStyle WithPositionOption(List<CssLonghandValue> declarations, ComputedStyle baseStyle, bool resolvesValues = false)
    {
        // The logical properties are mapped to the physical ones first: the declarations win over all the cascaded values
        EnsureLogicalPropertiesResolved("top");
        var cascaded = new Dictionary<string, CascadedValue>(_cascaded.Count + declarations.Count, StringComparer.Ordinal);
        foreach (var (name, value) in _cascaded)
        {
            if (LogicalProperties.GetPhysicalProperty(name, this) is null)
            {
                cascaded[name] = value;
            }
        }

        var rank = long.MaxValue - declarations.Count;
        foreach (var declaration in declarations)
        {
            rank++;
            if (cascaded.TryGetValue(declaration.Name, out var existing) && existing.IsImportant && !resolvesValues)
                continue;

            // "use of the revert value acts as if the property was part of the Author Origin, so that it instead reverts back to the
            // User Origin" (the user-agent declarations are not kept by the computed style: the property is unset); revert-layer
            // "has no special behavior": the position fallback origin has no layer, it rolls back to the author origin
            if (!resolvesValues && declaration.Value is "revert-layer" or "revert-rule")
                continue;

            var value = !resolvesValues && declaration.Value == "revert" ? declaration with { Value = "unset" } : declaration;
            cascaded[declaration.Name] = new CascadedValue(value, rank, existing.Source);
        }

        return WithVisitedCascadeOf(new ComputedStyle(Engine, Element, PseudoElement, Parent, cascaded)
        {
            HighlightParent = HighlightParent,
            PositionBaseStyle = baseStyle,
            ResolvedAnchorProperties = resolvesValues ? [.. declarations.Select(declaration => declaration.Name), .. ResolvedAnchorProperties ?? []] : ResolvedAnchorProperties,
        });
    }

    /// <summary>
    /// Returns the style of the element with declarations that replace its cascaded values: the style of a part of a box that
    /// some properties do not apply to (e.g. the symbol of a list marker, which is not spaced by <c>letter-spacing</c>).
    /// </summary>
    internal ComputedStyle WithOverrides(ReadOnlySpan<CssLonghandValue> declarations)
    {
        var cascaded = new Dictionary<string, CascadedValue>(_cascaded, StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            cascaded[declaration.Name] = new CascadedValue(declaration, long.MaxValue, source: null);
        }

        return WithVisitedCascadeOf(new ComputedStyle(Engine, Element, PseudoElement, Parent, cascaded) { HighlightParent = HighlightParent });
    }

    /// <summary>
    /// Returns a style derived from this style by the box tree (e.g. the style of the anonymous boxes whose parent box has this
    /// style, the style of the symbol of a list marker), created by <paramref name="create"/> the first time: the derived styles
    /// are kept with this style, so the box trees built while the style engine keeps this style (it is replaced when it changes)
    /// share them and the values computed from them (their <see cref="Layout"/>).
    /// </summary>
    /// <param name="key">The kind of derived style, compared with <see cref="object.Equals(object?)"/>: the same key always derives the same style from this style.</param>
    /// <param name="state">The argument of <paramref name="create"/>.</param>
    /// <param name="create">Creates the derived style from this style.</param>
    internal ComputedStyle GetDerivedStyle<TState>(object key, TState state, Func<ComputedStyle, TState, ComputedStyle> create)
    {
        if (_derivedStyles is { } derivedStyles)
        {
            foreach (var (derivedKey, derived) in derivedStyles)
            {
                if (derivedKey.Equals(key))
                    return derived;
            }
        }

        var style = create(this, state);
        (_derivedStyles ??= new(1)).Add((key, style));
        return style;
    }

    /// <summary>
    /// Returns the style of the element with declarations that replace its cascaded values (see <see cref="WithOverrides"/>), kept
    /// with this style for these declarations (see <see cref="GetDerivedStyle"/>): the values of the declarations depend on the
    /// state of the element (e.g. the position of the thumb of a slider).
    /// </summary>
    internal ComputedStyle GetDerivedStyleWithOverrides(CssLonghandValue[] declarations)
    {
        var key = new System.Text.StringBuilder("overrides:");
        foreach (var declaration in declarations)
        {
            key.Append(declaration.Name).Append(':').Append(declaration.Value).Append(';');
        }

        return GetDerivedStyle(key.ToString(), declarations, static (style, declarations) => style.WithOverrides(declarations));
    }

    /// <summary>
    /// Returns the style of the element with the same cascaded values that inherits from another style: the style of a box
    /// of the first formatted line, which inherits from the <c>::first-line</c> pseudo-element of its block container
    /// (https://drafts.csswg.org/css-pseudo-4/#first-line-inheritance).
    /// </summary>
    internal ComputedStyle WithParent(ComputedStyle parent)
    {
        _cascadedShared = true;
        return WithVisitedCascadeOf(new ComputedStyle(Engine, Element, PseudoElement, parent, _cascaded, cascadedShared: true) { HighlightParent = HighlightParent }, sameCascadedValues: true);
    }

    /// <summary>
    /// The factor that the used font size of the style is multiplied by (1 for the styles of the elements): the scaling factor
    /// of <c>text-fit</c> on a line (https://drafts.csswg.org/css-text-5/#text-fit: "the used font-size is its computed value
    /// multiplied by the line scaling factor"). The computed values are not changed: the font-relative lengths are the ones of
    /// the computed font size.
    /// </summary>
    internal double UsedFontScale { get; private init; } = 1;

    /// <summary>
    /// Returns the style of the element with the same cascaded values and parent whose used font size is multiplied by a factor
    /// (<see cref="UsedFontScale"/>): the style of a box on a line scaled by <c>text-fit</c>.
    /// </summary>
    internal ComputedStyle WithUsedFontScale(double scale)
    {
        _cascadedShared = true;
        return new ComputedStyle(Engine, Element, PseudoElement, Parent, _cascaded, cascadedShared: true) { HighlightParent = HighlightParent, UsedFontScale = scale };
    }

    /// <summary>
    /// Returns whether a declaration or an animation may apply to a property whose name starts with one of the prefixes. When
    /// none does, the properties that are not inherited have their initial value: the layout skips computing them (e.g. the
    /// margins, borders and paddings of the inline boxes, which most of them do not set).
    /// </summary>
    /// <summary>
    /// Whether the element may have a transform (https://drafts.csswg.org/css-transforms-2/#transform-property): a cascaded
    /// <c>transform</c>, <c>translate</c>, <c>rotate</c>, <c>scale</c>, <c>offset-path</c> or <c>all</c>, or animated values. These properties are not
    /// inherited, so the element has no transform otherwise (a cheap test before the computed values).
    /// </summary>
    internal bool MayHaveTransform => _effects is not null || _cascaded.ContainsKey("transform") || _cascaded.ContainsKey("translate")
        || _cascaded.ContainsKey("rotate") || _cascaded.ContainsKey("scale") || _cascaded.ContainsKey("offset-path") || _cascaded.ContainsKey("all");

    internal bool MayHaveCascadedValueStartingWith(string prefix1, string prefix2, string? prefix3 = null)
    {
        if (_effects is not null)
            return true;

        foreach (var property in _cascaded.Keys)
        {
            if (property.StartsWith(prefix1, StringComparison.Ordinal) || property.StartsWith(prefix2, StringComparison.Ordinal)
                || (prefix3 is not null && property.StartsWith(prefix3, StringComparison.Ordinal)) || property == "all")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether a property that is not inherited may have a value other than its initial value: a declaration of it or of <c>all</c>, or an animation.</summary>
    internal bool MayHaveCascadedValue(string property) => _effects is not null || _smilAnimations is not null || _cascaded.ContainsKey(property) || _cascaded.ContainsKey("all");

    /// <summary>Returns whether a property that is not a logical property has a cascaded value.</summary>
    internal bool HasCascadedValue(string property) => _cascaded.ContainsKey(property);

    /// <summary>
    /// Returns whether the cascaded value of a property (as declared: the logical properties are not mapped) comes from a declaration
    /// of the author origin: a style sheet or a style attribute of a tree (the declarations of the user agent style sheets and the
    /// presentational hints have no tree scope). A declaration that rolls back the cascade (revert, revert-layer, revert-rule) counts
    /// as the declaration it rolls back to.
    /// </summary>
    internal bool HasAuthorCascadedValue(string property) => _cascaded.TryGetValue(property, out var value) && value.TreeScope is not null;

    /// <summary>Returns the cascaded value of a property (the logical properties are mapped to the physical properties).</summary>
    internal bool TryGetCascadedValue(string property, out CascadedValue value)
    {
        EnsureLogicalPropertiesResolved(property);
        return _cascaded.TryGetValue(property, out value);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Resolved values (https://drafts.csswg.org/cssom/#resolved-values)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the resolved value of a longhand or custom property (https://drafts.csswg.org/cssom/#resolved-value): the
    /// computed value, except for the colors (the used value: currentcolor is resolved), line-height (the used value
    /// when it is a number), transform (a matrix), and the used values from the layout of width, height, the margins, the
    /// paddings, the insets of the positioned boxes, the transform origins and the track lists of the grid containers (a
    /// forced layout, see <see cref="LayoutResolvedValues"/>).
    /// </summary>
    public string GetResolvedValue(string property) => GetResolvedValue(property, forceColors: false);

    /// <param name="forceColors">
    /// Whether the colors of all the properties are forced in forced colors mode (the used values, see <see cref="GetUsedValue"/>),
    /// or only the ones of the properties whose resolved value is the used value.
    /// </param>
    private string GetResolvedValue(string property, bool forceColors)
    {
        if (CssPropertyRegistry.IsCustomProperty(property))
            return GetCustomPropertyValue(property) ?? "";

        var value = GetComputedValue(property);
        var physical = LogicalProperties.GetPhysicalProperty(property, this) ?? property;

        if (TryGetAnchorPositionedStyle(physical) is { } positionedStyle)
            return positionedStyle.GetResolvedValue(property, forceColors);

        // https://drafts.csswg.org/css-color-adjust-1/#forced-colors-properties: the colors are forced at used-value time
        if (Environment.ForcedColors && GetForcedColorsKind(physical) is var forcedColorsKind and not ForcedColorsKind.None
            && (forceColors || IsResolvedAsUsedColor(physical)) && IsForcedColorsAdjusted)
        {
            return ForceColors(physical, forcedColorsKind, value);
        }

        if (physical == "line-height" && value != "normal")
        {
            // https://html.spec.whatwg.org/multipage/rendering.html#the-input-element-as-a-text-entry-widget: "the used value of
            // the 'line-height' property must be a length value that is no smaller than what the used value would be for
            // 'line-height: normal'"
            if (PseudoElement is null && Element is HtmlInputElement { InputType: InputType.Text or InputType.Search or InputType.Tel or InputType.Url or InputType.Email or InputType.Password })
            {
                var used = CssValueComputer.TryParseNumber(value, out var factor) ? factor * FontSize : CssValueComputer.ParsePixels(value) ?? 0;
                return CssValueComputer.FormatPixels(Math.Max(used, Fonts.PrimaryFont.GetMetrics(FontSize).NormalLineHeight));
            }

            if (CssValueComputer.TryParseNumber(value, out var number))
                return CssValueComputer.FormatPixels(number * FontSize);
        }

        // https://drafts.csswg.org/css-text-4/#letter-spacing-property: "For legacy reasons, a computed letter-spacing of zero
        // yields a resolved value (getComputedStyle() return value) of normal"
        if (physical == "letter-spacing" && value == "0px")
            return "normal";

        if (physical is "border-top-width" or "border-right-width" or "border-bottom-width" or "border-left-width" && IsBorderNotDrawn(physical))
            return "0px";

        // https://drafts.csswg.org/css-sizing-3/#valdef-width-auto: min-width: auto resolves to zero, except for the flex and grid
        // items and the boxes with an aspect ratio; it also resolves to zero when there is no box
        if (physical is "min-width" or "min-height" && value == "auto" && (!GeneratesBox() || (GetComputedValue("aspect-ratio") == "auto" && !IsFlexOrGridItem())))
            return "0px";

        if (physical == "caret-color" || value.Contains("currentcolor", StringComparison.Ordinal))
            return ResolveColorKeywords(physical, value);

        if (physical is "transform-origin" or "perspective-origin")
            return ResolveOrigin(value);

        if (physical == "transform")
            return ResolveTransform(value);

        if (TryGetLayoutResolvedValue(physical, value) is { } layoutValue)
            return layoutValue;

        // https://drafts.csswg.org/css-animations-2/#animation-duration: auto is 0s for the document timeline (the browsers
        // only resolve it when animation-timeline is the single auto value)
        if (physical == "animation-duration" && value.Contains("auto", StringComparison.Ordinal) && GetComputedValue("animation-timeline") == "auto")
            return string.Join(", ", value.Split(", ").Select(duration => duration == "auto" ? "0s" : duration));

        // https://drafts.csswg.org/css-overflow-3/#overflow-control: visible computes to auto when the other axis is neither
        // visible nor clip (as in the browsers, clip is kept; the computed values keep the specified keywords, to avoid a cycle)
        if (physical is "overflow-x" or "overflow-y" && value == "visible" && GetComputedValue(physical == "overflow-x" ? "overflow-y" : "overflow-x") is not ("visible" or "clip"))
            return "auto";

        return value;
    }

    /// <summary>
    /// Returns the style the layout used for the accepted @position-try properties of an anchor-positioned box
    /// (https://drafts.csswg.org/css-anchor-position-1/#interleaving: "The resulting styles are applied to the element via interleaving,
    /// so they affect computed values"): the values of its position option, with its anchor functions resolved to lengths. Returns null
    /// for the other properties and boxes (a forced layout otherwise).
    /// </summary>
    internal ComputedStyle? TryGetAnchorPositionedStyle(string physical)
    {
        if (PositionBaseStyle is not null || Array.IndexOf(AnchorPositioning.AcceptedProperties, physical) < 0 || !Layout.IsAbsolutelyPositioned)
            return null;

        var anchor = Layout.Anchor;
        if (!anchor.UsesAnchorFunctions && anchor.TryFallbacks is null && anchor.PositionArea is null)
            return null;

        return LayoutResolvedValues.GetAnchorPositionedStyle(this);
    }

    /// <summary>
    /// Returns the resolved value of a property whose used value comes from the layout (https://drafts.csswg.org/cssom/#resolved-values):
    /// width, height, the margins, the paddings, the insets of the positioned boxes and the track lists of the grid containers.
    /// Reading them computes the layout when it is out of date (see <see cref="LayoutResolvedValues"/>), except for the margins
    /// and the paddings in px (their used value is their computed value). Returns null for the other properties, and when the
    /// resolved value is the computed value.
    /// </summary>
    /// <remarks>
    /// Without layout (no browsing context, or the element is not connected), the used values that are known without layout
    /// are returned (see <see cref="BoxSizes"/>), else the computed value.
    /// </remarks>
    private string? TryGetLayoutResolvedValue(string physical, string value)
    {
        switch (physical)
        {
            // https://drafts.csswg.org/cssom/#resolved-values: "block-size, height, inline-size, margin-block-end, ..., width: If the
            // property applies to the element or pseudo-element and the resolved value of the display property is not none or
            // contents, then the resolved value is the used value. Otherwise the resolved value is the computed value."
            case "width" or "height":
            {
                var horizontal = physical == "width";
                return LayoutResolvedValues.GetSize(this, horizontal, out var size) switch
                {
                    LayoutResolution.UsedValue => CssValueComputer.FormatPixels(size),
                    LayoutResolution.NoLayout when !value.EndsWith("px", StringComparison.Ordinal) && BoxSizes.GetUsedSize(this, horizontal) is { } usedSize => CssValueComputer.FormatPixels(usedSize),
                    _ => null,
                };
            }

            case "margin-top" or "margin-right" or "margin-bottom" or "margin-left" or "padding-top" or "padding-right" or "padding-bottom" or "padding-left":
            {
                // A length is its used value, unless the block step sizing inserts space in the margins or the paddings
                // (https://drafts.csswg.org/css-rhythm/#block-step-insert)
                var isAuto = value == "auto";
                if (!isAuto && !value.Contains('%', StringComparison.Ordinal) && Layout.BlockStep is null)
                    return null;

                var isPadding = physical[0] == 'p';
                var side = GetPhysicalSide(physical);
                var resolution = isPadding ? LayoutResolvedValues.GetPadding(this, side, out var used) : LayoutResolvedValues.GetMargin(this, side, out used);
                if (resolution == LayoutResolution.UsedValue)
                    return CssValueComputer.FormatPixels(used);

                // Without layout, the percentages refer to the inline size of the containing block when it is known
                if (resolution == LayoutResolution.NoLayout && !isAuto && BoxSizes.GetMarginPercentageBasis(this) is { } basis && BoxSizes.ResolveLength(value, basis) is { } length)
                    return CssValueComputer.FormatPixels(isPadding ? Math.Max(0, length) : length);

                return null;
            }

            // https://drafts.csswg.org/cssom/#resolved-values: "If the property applies to a positioned element and the resolved
            // value of the display property is not none or contents, and the property is not over-constrained, the resolved value
            // is the used value. Otherwise the resolved value is the computed value." (see BoxFragment.ResolvedInsets)
            case "top" or "right" or "bottom" or "left":
                if (GetComputedValue("position") == "static" || LayoutResolvedValues.GetInset(this, GetPhysicalSide(physical), out var inset) != LayoutResolution.UsedValue)
                    return null;

                return inset is { } usedInset ? CssValueComputer.FormatPixels(usedInset) : "auto";

            // https://drafts.csswg.org/css-grid-2/#resolved-track-list: the resolved value of the track list of a grid container is
            // the list of its used track sizes (recorded by the grid layout); otherwise it is the computed value
            case "grid-template-columns" or "grid-template-rows":
                return LayoutResolvedValues.GetGridTrackList(this, physical == "grid-template-columns", out var trackList) == LayoutResolution.UsedValue ? trackList : null;

            default:
                return null;
        }

        static PhysicalSide GetPhysicalSide(string property)
        {
            return property[property.LastIndexOf('-', StringComparison.Ordinal) + 1] switch
            {
                't' => PhysicalSide.Top,
                'r' => PhysicalSide.Right,
                'b' => PhysicalSide.Bottom,
                _ => PhysicalSide.Left,
            };
        }
    }

    /// <summary>
    /// Returns the used width of a border in px (border-top-width...): the width is 0 when the style of the border is none
    /// or hidden (https://drafts.csswg.org/css-backgrounds/#border-width), but the computed value keeps the specified
    /// length, and it is the value a child inherits.
    /// </summary>
    internal double GetUsedBorderWidth(string property) => IsBorderNotDrawn(property) ? 0 : CssValueComputer.ParsePixels(GetComputedValue(property)) ?? 0;

    private bool IsBorderNotDrawn(string widthProperty) => GetComputedValue(widthProperty.Replace("-width", "-style", StringComparison.Ordinal)) is "none" or "hidden";

    /// <summary>
    /// https://drafts.csswg.org/css-transforms/#transform-origin-property: the resolved value of transform-origin and
    /// perspective-origin is the used value, in px: the percentages are resolved against the reference box of the transforms
    /// (see <see cref="LayoutResolvedValues.GetTransformReferenceBox"/>, a forced layout). Without layout, they are resolved
    /// against the border box when its size is known (see <see cref="BoxSizes.GetBorderBoxSize"/>). They are kept when the
    /// element is not transformable.
    /// </summary>
    private string ResolveOrigin(string value)
    {
        var parts = CssValueComputer.SplitComponents(value);
        if (parts.Length < 2 || (parts[0].EndsWith("px", StringComparison.Ordinal) && parts[1].EndsWith("px", StringComparison.Ordinal)))
            return value;

        var resolution = LayoutResolvedValues.GetTransformReferenceBox(this, out var referenceBox);
        if (resolution == LayoutResolution.ComputedValue)
            return value;

        for (var i = 0; i < 2; i++)
        {
            if (parts[i].EndsWith("px", StringComparison.Ordinal))
                continue;

            var size = resolution == LayoutResolution.UsedValue ? (i == 0 ? referenceBox.Width : referenceBox.Height) : BoxSizes.GetBorderBoxSize(this, horizontal: i == 0);
            if (size is not null && BoxSizes.ResolveLength(parts[i], size.Value) is { } pixels)
            {
                parts[i] = CssValueComputer.FormatPixels(pixels);
            }
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-transforms-2/#serialization-of-the-computed-value: the resolved value of transform is
    /// the product of the matrices of its functions, serialized as a matrix() or a matrix3d(). The percentages of the
    /// translations refer to the reference box (https://drafts.csswg.org/css-transforms-1/#transform-box, see
    /// <see cref="LayoutResolvedValues.GetTransformReferenceBox"/>, a forced layout; without layout, the border box when its
    /// size is known: see <see cref="GetTransformReferenceBox"/>); the computed value is kept when it is unknown.
    /// </summary>
    private string ResolveTransform(string value)
    {
        if (value == "none")
            return value;

        (double Width, double Height)? referenceBox = null;
        if (value.Contains('%', StringComparison.Ordinal))
        {
            referenceBox = LayoutResolvedValues.GetTransformReferenceBox(this, out var size) switch
            {
                LayoutResolution.UsedValue => (size.Width, size.Height),
                LayoutResolution.NoLayout => GetTransformReferenceBox(),
                _ => null,
            };
        }

        var values = CssParser.ParseListOfComponentValues(value);
        return CssTransforms.ToMatrix(values, CssCalcContext.Default, referenceBox, out _) is { } matrix ? CssTransforms.Serialize(matrix) : value;
    }

    /// <summary>
    /// Returns the size of the reference box of the transforms (https://drafts.csswg.org/css-transforms-1/#transform-box), which
    /// the percentages of the translations refer to, or null when it is not known without layout (used when the transforms are
    /// interpolated, while the styles are computed: the layout is not available then).
    /// </summary>
    internal (double Width, double Height)? GetTransformReferenceBox()
    {
        if (GetComputedValue("transform-box") is "border-box" or "stroke-box" or "view-box"
            && BoxSizes.GetBorderBoxSize(this, horizontal: true) is { } width
            && BoxSizes.GetBorderBoxSize(this, horizontal: false) is { } height)
        {
            return (width, height);
        }

        return null;
    }

    /// <summary>
    /// Returns the used color (the value of currentcolor). The color of a highlight pseudo-element may be currentcolor
    /// (https://drafts.csswg.org/css-pseudo-4/#highlight-cascade): it is the color of the originating element (there are no
    /// highlight overlays below it, as nothing is rendered).
    /// </summary>
    private string GetUsedColor()
    {
        // The color of a visited style has the alpha of the color of the style of the element (see GetVisitedDependentValue)
        if (_unvisitedStyle is { } unvisited)
        {
            if (_visitedUsedColor is { } cached)
                return cached;

            var visitedColor = GetOwnUsedColor();
            var unvisitedColor = unvisited.GetOwnUsedColor();
            return _visitedUsedColor = visitedColor == "currentcolor" || unvisitedColor == "currentcolor" ? visitedColor : CombineVisitedColor("color", unvisitedColor, visitedColor) ?? visitedColor;
        }

        return GetOwnUsedColor();
    }

    private string? _visitedUsedColor;

    /// <summary>Returns the used color of the style, whose alpha is not yet the one of the unvisited color for a visited style.</summary>
    private string GetOwnUsedColor()
    {
        var color = GetComputedValue("color");
        if (IsHighlight)
        {
            if (Parent is not { } originating)
                return color;

            if (Environment.ForcedColors && IsForcedColorsAdjusted)
                return GetForcedHighlightColor(color, originating);

            return color == "currentcolor" ? originating.GetUsedColor() : color;
        }

        // https://drafts.csswg.org/css-color-adjust-1/#forced-colors-properties: the color is forced at used-value time
        return Environment.ForcedColors && IsForcedColorsAdjusted ? GetForcedColor(color) : color;
    }

    /// <summary>
    /// Resolves the keywords of the computed value of a color property that depend on the color of the element: the used value
    /// of <c>auto</c> is <c>currentcolor</c> for <c>caret-color</c> (https://drafts.csswg.org/css-ui-4/#caret-color), and
    /// <c>currentcolor</c> is the used color.
    /// </summary>
    private string ResolveColorKeywords(string physical, string value)
    {
        if (physical == "caret-color" && value.Contains("auto", StringComparison.Ordinal))
        {
            value = string.Join(' ', CssValueComputer.SplitComponents(value).Select(part => part == "auto" ? "currentcolor" : part));
        }

        return value.Contains("currentcolor", StringComparison.Ordinal) ? CssValueComputer.ResolveCurrentColor(this, physical, value, GetUsedColor()) : value;
    }

    /// <summary>Returns whether the element may generate a box: neither it nor one of its ancestors has display: none (see <see cref="BoxGeneration"/>).</summary>
    private bool GeneratesBox() => !BoxGeneration.Get(this).IsDisplayNone;

    /// <summary>Returns whether the element is a flex or grid item: the parent box (the nearest ancestor that is not <c>display: contents</c>) is a flex or grid container.</summary>
    private bool IsFlexOrGridItem()
    {
        var parent = Parent;
        while (parent is not null && parent.Layout.Display.IsContents)
        {
            parent = parent.Parent;
        }

        return parent?.Layout.Display is { Box: DisplayBox.Normal, Internal: DisplayInternal.None, Inside: DisplayInside.Flex or DisplayInside.Grid or DisplayInside.GridLanes };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Computed values
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the computed value of a longhand (https://drafts.csswg.org/css-cascade-5/#computed).</summary>
    public string GetComputedValue(string property)
    {
        if (_computed.TryGetValue(property, out var result))
            return result;

        var physical = LogicalProperties.GetPhysicalProperty(property, this);
        if (physical is not null)
        {
            result = GetComputedValue(physical);
            _computed.Set(property, result);
            return result;
        }

        var definition = CssPropertyRegistry.Get(property);
        if (definition is null || definition.IsShorthand)
            return "";

        // The value often depends on the same property of the parent (inheritance, display, font-size...): it is computed
        // for the ancestors first, from the topmost one, so the recursion does not go as deep as the tree.
        if (GetInheritanceSource(property) is { } parent && !parent._computed.ContainsKey(property))
        {
            ComputeAncestors(property);
        }

        var computing = Engine.ComputingProperties;
        if (!computing.Add(this, property))
        {
            // A cyclic dependency between properties (e.g. font-size: 1lh): the initial value is used
            return CssValueComputer.Compute(this, definition, CssValueComputer.GetInitialValue(definition), baseUrl: null);
        }

        try
        {
            result = ComputeProperty(definition);
            if (Environment.ForcedColors)
            {
                result = AdjustForForcedColors(property, result);
            }
        }
        finally
        {
            computing.Remove(this, property);
        }

        _computed.Set(property, result);
        return result;
    }

    /// <summary>Returns whether the computed value of the property is being computed (a dependency cycle when it is needed again).</summary>
    private bool IsComputing(string property) => Engine.ComputingProperties.Contains(this, property);

    private void ComputeAncestors(string property)
    {
        // The ancestors are pushed on a list shared by the styles of the engine: a nested computation pushes its ancestors
        // after them, and removes them before it returns
        var ancestors = Engine.PendingAncestors;
        var start = ancestors.Count;
        for (var ancestor = GetInheritanceSource(property); ancestor is not null && !ancestor._computed.ContainsKey(property); ancestor = ancestor.GetInheritanceSource(property))
        {
            ancestors.Add(ancestor);
        }

        try
        {
            for (var i = ancestors.Count - 1; i >= start; i--)
            {
                ancestors[i].GetComputedValue(property);
            }
        }
        finally
        {
            ancestors.RemoveRange(start, ancestors.Count - start);
        }
    }

    /// <summary>
    /// The style a property inherits from: the parent, except for the properties that apply to the highlight pseudo-elements,
    /// which inherit from the highlight pseudo-element of the parent element (https://drafts.csswg.org/css-pseudo-4/#highlight-cascade),
    /// and for the properties that are not inherited (<c>inherit</c>) in the style of a box of the first formatted line (see
    /// <see cref="WithParent"/>; https://drafts.csswg.org/css-pseudo-4/#first-line-inheritance: "the fragment of a child that occurs
    /// on the first line inherits any standard inherited properties [...] from the ::first-line pseudo-element. For all other
    /// properties, [...] inheritance is from the non-pseudo parent").
    /// </summary>
    private ComputedStyle? GetInheritanceSource(string property)
    {
        if (IsHighlight && HighlightProperties.Contains(property))
            return HighlightParent;

        var parent = Parent;
        if (parent is { PseudoElement: "first-line" } && CssPropertyRegistry.Get(property) is { Inherited: false })
        {
            do
            {
                parent = parent.Parent;
            }
            while (parent is { PseudoElement: "first-line" });
        }

        return parent;
    }

    private string ComputeProperty(CssPropertyDefinition property)
    {
        var name = property.Name;

        // https://drafts.csswg.org/css-pseudo-4/#highlight-styling: the properties that do not apply to the highlight
        // pseudo-elements (e.g. font-size and line-height, used to resolve the font-relative lengths) have the computed values
        // of the originating element, whatever their declarations
        if (IsHighlight && !HighlightProperties.Contains(name) && Parent is { } originating)
            return originating.GetComputedValue(name);

        // Only the colors of a visited style differ from the ones of the style of the element (see ComputedStyle.VisitedLinks.cs)
        if (_unvisitedStyle is { } unvisited && !VisitedProperties.Contains(name))
            return unvisited.GetComputedValue(name);

        // The animation origin (https://drafts.csswg.org/css-cascade-5/#cascade-origin-animation)
        if (_effects is { Count: > 0 } && TryGetAnimatedValue(property, out var animated))
            return animated;

        return ComputeUnanimatedProperty(property);
    }

    /// <summary>Computes the value of a property without the animations (the underlying value of the animations).</summary>
    private string ComputeUnanimatedProperty(CssPropertyDefinition property)
    {
        var value = property.Name == "font-size"
            ? AdjustFontSizeToGenericFamily(ComputeUnanimatedCascadedProperty(property))
            : ComputeUnanimatedCascadedProperty(property);

        // The SMIL animations of the property are the underlying values of the CSS animations and transitions
        return _smilAnimations is null ? value : ApplySmilAnimations(property, value);
    }

    /// <summary>
    /// The default font size of the monospace generic family is 13px in the browsers (16px for the other families): the font
    /// size of an element whose font family is <c>monospace</c> alone follows it unless it is an absolute length. A size
    /// keyword (declared or inherited) is mapped with the monospace default; a size relative to the one of the parent (or
    /// inherited from such a size) is scaled when the element and its parent do not both use the monospace family.
    /// </summary>
    private string AdjustFontSizeToGenericFamily(string value)
    {
        var origin = _fontSizeOrigin;
        if (origin == FontSizeOrigin.AbsoluteLength)
            return value;

        var parent = GetInheritanceSource("font-size");
        var isMonospace = UsesMonospaceDefaultFontSize;
        var isParentMonospace = parent is not null && parent.UsesMonospaceDefaultFontSize;
        if (origin != FontSizeOrigin.Relative)
        {
            // A keyword inherited from an element of the other family is mapped again
            if (!isMonospace && !isParentMonospace)
                return value;

            return CssValueComputer.FormatPixels(CssValueComputer.GetAbsoluteFontSize(origin, isMonospace));
        }

        if (isMonospace == isParentMonospace || CssValueComputer.ParsePixels(value) is not { } pixels)
            return value;

        const double Ratio = CssValueComputer.MonospaceMediumFontSize / CssValueComputer.MediumFontSize;
        return CssValueComputer.FormatPixels(isMonospace ? pixels * Ratio : pixels / Ratio);
    }

    /// <summary>
    /// Whether the property is <c>overlay</c> on an element: its value is the one of the important user agent declarations
    /// (https://drafts.csswg.org/css-position-4/#overlay: "* { overlay: none !important; }", and <c>overlay: auto</c> for the
    /// elements in the top layer, https://drafts.csswg.org/css-position-4/#add-an-element-to-the-top-layer), which win over the
    /// author declarations and the animations, but not over the transitions.
    /// </summary>
    private bool HasUserAgentImportantOverlay(string name) => PseudoElement is null && name == "overlay";

    /// <summary>
    /// Whether the element was in the top layer, not pending removal, when the style was computed (the user agent rule
    /// <c>overlay: auto !important</c> targets it): the before-change style of a transition keeps the value of its time.
    /// </summary>
    private bool _hasOverlayAutoRule;

    /// <summary>Whether the font family is the <c>monospace</c> generic family alone (as in Blink and WebKit: <c>monospace, monospace</c> keeps the default size).</summary>
    private bool UsesMonospaceDefaultFontSize => GetComputedValue("font-family") == "monospace";

    private string ComputeUnanimatedCascadedProperty(CssPropertyDefinition property)
    {
        var name = property.Name;
        if (HasUserAgentImportantOverlay(name))
            return _hasOverlayAutoRule ? "auto" : "none";

        // The properties of the highlight pseudo-elements inherit whether they are inherited properties or not
        var inherited = property.Inherited || IsHighlight;
        if (!TryGetCascadedValue(name, out var cascaded))
        {
            // https://drafts.csswg.org/css-lists-3/#marker-properties: the marker is not indented nor aligned by the properties of
            // its list item (as the important declarations of the user agent style sheet of the browsers:
            // text-indent: 0 !important; text-align: start !important; text-align-last: start !important)
            if (PseudoElement == "marker" && name is "text-indent" or "text-align-all" or "text-align-last")
                return Default(property, inherit: false);

            return Default(property, inherit: inherited);
        }

        // A dependency cycle via the font-relative units of a registered custom property: invalid at computed-value time
        if (name is "font-size" or "line-height" && GetUnitCycle(name).Count > 0)
            return Default(property, inherit: property.Inherited);

        using var scope = CssSubstitution.BeginPropertyComputation(name);
        return ComputeCascadedValue(property, cascaded, inherited, rollbacks: 0);
    }

    private string ComputeCascadedValue(CssPropertyDefinition property, CascadedValue cascaded, bool inherited, int rollbacks)
    {
        var declared = cascaded.Value;
        string? text = declared.ComputationValue ?? declared.Value;
        if (declared.ShorthandName is { } shorthand)
        {
            text = SubstituteShorthand(shorthand, CssRandom.AssignIfConditionCachingKeys(shorthand, declared.ShorthandValue ?? ""), declared.Name, GetSubstitutionContext(cascaded));
        }
        else if (CssSubstitution.MayContainSubstitution(text))
        {
            text = SubstituteLonghand(declared.Name, CssRandom.AssignIfConditionCachingKeys(declared.Name, text), GetSubstitutionContext(cascaded));
        }

        // Invalid at computed-value time (https://drafts.csswg.org/css-variables/#invalid-at-computed-value-time): unset
        if (text is null)
            return Default(property, inherit: inherited);

        switch (InfraAscii.ToAsciiLowercase(text))
        {
            case "initial":
                return Default(property, inherit: false);
            case "inherit":
                // The children of a select element are assigned to slots of its shadow tree, which they inherit from
                // (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2): the display of a slot is contents
                if (PseudoElement is null && Element.ParentNode is Dom.HtmlSelectElement && property.Name == "display")
                    return "contents";

                return Default(property, inherit: true);
            case "revert" or "revert-layer" or "revert-rule" when cascaded.RollBackSubstitution is { } rollBack && rollbacks < MaxSubstitutionRollbacks:
                // A keyword that results from the substitution rolls back the cascade from the declaration
                return ComputeCascadedValue(property, rollBack(InfraAscii.ToAsciiLowercase(text)), inherited, rollbacks + 1);
            case "unset" or "revert" or "revert-layer" or "revert-rule":
                return Default(property, inherit: inherited);
        }

        if (property.Name == "font-size")
        {
            // A size relative to an absolute length is an absolute length too (as in Blink)
            var origin = CssValueComputer.GetFontSizeOrigin(text);
            _fontSizeOrigin = origin == FontSizeOrigin.Relative && GetInheritanceSource("font-size") is { _fontSizeOrigin: FontSizeOrigin.AbsoluteLength } ? FontSizeOrigin.AbsoluteLength : origin;
        }

        return CssValueComputer.Compute(this, property, text, cascaded.BaseUrl);
    }

    /// <summary>
    /// Returns the context of the substitution of a cascaded value: the element, or the frame of the application of the mixin
    /// of the declaration, whose variables are its parameters and its local variables.
    /// </summary>
    private SubstitutionContext GetSubstitutionContext(CascadedValue cascaded) => cascaded.Mixin is { } mixin ? GetMixinFrame(mixin).Context : new SubstitutionContext(this, cascaded.TreeScope, AttrElement: cascaded.OriginatingElement);

    /// <summary>Returns the frame of the application of a mixin for the element (created on first use).</summary>
    private CssFunctionFrame GetMixinFrame(CssMixinApplication mixin)
    {
        var mixinFrames = Rare.MixinFrames ??= new(ReferenceEqualityComparer.Instance);
        if (mixinFrames.TryGetValue(mixin, out var frame))
            return frame;

        // The frame is registered before its initialization: a reference to the application while its arguments are resolved
        // sees the parameters that are not resolved yet as the guaranteed-invalid value
        var callerContext = mixin.Parent is { } parent ? GetMixinFrame(parent).Context : new SubstitutionContext(this, mixin.TreeScope);
        frame = CssFunctionFrame.CreateMixin(mixin, callerContext);
        mixinFrames[mixin] = frame;
        frame.InitializeMixin();
        return frame;
    }

    /// <summary>The value when there is no cascaded value, or with a CSS-wide keyword (https://drafts.csswg.org/css-cascade-5/#defaulting).</summary>
    private string Default(CssPropertyDefinition property, bool inherit)
    {
        if (inherit && GetInheritanceSource(property.Name) is { } parent)
        {
            var inherited = parent.GetComputedValue(property.Name);
            if (property.Name == "font-size")
            {
                _fontSizeOrigin = parent._fontSizeOrigin;
            }

            return inherited;
        }

        if (property.Name == "font-size")
        {
            _fontSizeOrigin = FontSizeOrigin.Medium;
        }

        // The inherited color of the highlight pseudo-elements of the root element is currentcolor
        if (inherit && IsHighlight && property.Name == "color")
            return "currentcolor";

        return CssValueComputer.Compute(this, property, CssValueComputer.GetInitialValue(property), baseUrl: null);
    }

    /// <summary>Substitutes the functions of the value of a longhand and validates the result against its grammar.</summary>
    private string? SubstituteLonghand(string name, string text, SubstitutionContext context)
    {
        // https://drafts.csswg.org/css-values-5/#attr-security: an attr()-tainted value used in a URL makes the declaration invalid
        var substituted = CssSubstitution.Substitute(text, context, out var taintedRanges);
        if (substituted is null || CssSubstitution.UsesTaintedUrl(substituted, taintedRanges))
            return null;

        var longhands = ParseSubstitutedValue(name, substituted);
        return longhands is [var longhand] && longhand.ShorthandName is null ? longhand.Value : null;
    }

    /// <summary>Parses the value of a property (a longhand or a shorthand) after substitution.</summary>
    private static List<CssLonghandValue>? ParseSubstitutedValue(string property, string substituted)
    {
        // The elements that use the same variables have the same substituted values: they are parsed once
        var cache = substituted.Length <= ParsedValueCache.MaxTextLength ? ParsedValueCache.SubstitutedValues : null;
        if (cache is not null && cache.TryGetValue(atRule: null, property, substituted, out var cached))
            return cached;

        var values = ParseSubstitutedValue(substituted, out var originalText);
        var longhands = CssShorthands.ParseValue(property, values, originalText);
        cache?.Add(atRule: null, property, substituted, longhands);
        return longhands;
    }

    /// <summary>
    /// Parses a value after substitution. The comments between its top-level component values (e.g. the empty comment that
    /// separates two substituted lengths, https://drafts.csswg.org/css-syntax-3/#serialization) only separate the tokens: they
    /// are serialized as a space, as the browsers serialize the parsed value.
    /// </summary>
    private static List<CssComponentValue> ParseSubstitutedValue(string substituted, out string text)
    {
        var values = CssParser.ParseListOfComponentValues(substituted, out var preprocessed);
        if (!preprocessed.Contains("/*", StringComparison.Ordinal))
        {
            text = preprocessed.Trim();
            return values;
        }

        var builder = new System.Text.StringBuilder(preprocessed.Length);
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (i > 0 && value.Start > values[i - 1].End && values[i - 1] is not CssPreservedToken { Type: CssTokenType.Whitespace } && value is not CssPreservedToken { Type: CssTokenType.Whitespace })
            {
                builder.Append(' ');
            }

            builder.Append(preprocessed, value.Start, Math.Min(value.End, preprocessed.Length) - value.Start);
        }

        text = builder.ToString().Trim();
        return CssParser.ParseListOfComponentValues(text);
    }

    /// <summary>Substitutes the functions of the value of a shorthand (a pending-substitution value) and returns the value of the longhand.</summary>
    private string? SubstituteShorthand(string shorthand, string text, string longhand, SubstitutionContext context)
    {
        var substituted = CssSubstitution.Substitute(text, context, out var taintedRanges);
        if (substituted is null || CssSubstitution.UsesTaintedUrl(substituted, taintedRanges))
            return null;

        var longhands = ParseSubstitutedValue(shorthand, substituted);
        if (longhands is null)
            return null;

        foreach (var value in longhands)
        {
            if (value.Name == longhand)
                return value.ShorthandName is null ? value.Value : null;
        }

        return null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Custom properties (https://drafts.csswg.org/css-variables/)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the computed value of a custom property, or null for the guaranteed-invalid value. When the value is attr-tainted
    /// (https://drafts.csswg.org/css-values-5/#attr-security), the value being substituted becomes attr-tainted.
    /// </summary>
    public string? GetCustomPropertyValue(string name)
    {
        // The custom properties of a visited style are the ones of the style of the element
        if (_unvisitedStyle is { } unvisited)
            return unvisited.GetCustomPropertyValue(name);

        if (_rareData?.CustomProperties is { } customProperties && customProperties.TryGetValue(name, out var result))
            return result;

        var rareData = Rare;

        // A dependency cycle (https://drafts.csswg.org/css-variables/#cycles): all the custom properties of the cycle are invalid
        var customPropertyStack = rareData.CustomPropertyStack ??= [];
        var index = customPropertyStack.IndexOf(name);
        if (index >= 0)
        {
            var customPropertyCycles = rareData.CustomPropertyCycles ??= new(StringComparer.Ordinal);
            for (var i = index; i < customPropertyStack.Count; i++)
            {
                customPropertyCycles.Add(customPropertyStack[i]);
            }

            return null;
        }

        customPropertyStack.Add(name);
        var outerTainted = rareData.CustomPropertyTainted;
        bool tainted;
        rareData.CustomPropertyTainted = false;
        try
        {
            using var scope = CssSubstitution.BeginPropertyComputation(name);
            result = ComputeCustomProperty(name);
        }
        finally
        {
            customPropertyStack.RemoveAt(customPropertyStack.Count - 1);
            tainted = rareData.CustomPropertyTainted;
            rareData.CustomPropertyTainted = outerTainted;
        }

        // The properties of a cycle are invalid at computed-value time
        if (rareData.CustomPropertyCycles is not null && rareData.CustomPropertyCycles.Contains(name))
        {
            result = GetInvalidValue(name, RegisteredProperties.GetValueOrDefault(name));
            tainted = false;
        }

        if (tainted && result is not null)
        {
            (rareData.TaintedCustomProperties ??= new(StringComparer.Ordinal)).Add(name);
        }

        (rareData.CustomProperties ??= new(StringComparer.Ordinal))[name] = result;
        return result;
    }

    /// <summary>
    /// Returns whether the computed value of a custom property is attr()-tainted (https://drafts.csswg.org/css-values-5/#attr-security):
    /// an attr() function was substituted in it, or in the value it inherits. A var() that references it is then tainted as a whole.
    /// </summary>
    public bool IsCustomPropertyTainted(string name)
    {
        if (_unvisitedStyle is { } unvisited)
            return unvisited.IsCustomPropertyTainted(name);

        GetCustomPropertyValue(name);
        return _rareData?.TaintedCustomProperties is { } taintedCustomProperties && taintedCustomProperties.Contains(name);
    }

    /// <summary>Returns the computed value of a custom property of the parent, which the value inherits along with its attr()-taint.</summary>
    private string? InheritCustomPropertyValue(ComputedStyle parent, string name)
    {
        var value = parent.GetCustomPropertyValue(name);
        if (parent.IsCustomPropertyTainted(name))
        {
            Rare.CustomPropertyTainted = true;
        }
        return value;
    }

    private string? ComputeCustomProperty(string name)
    {
        var registration = RegisteredProperties.GetValueOrDefault(name);

        // The animation origin (https://drafts.csswg.org/css-cascade-5/#cascade-origin-animation)
        if (_effects is { Count: > 0 } && TryGetAnimatedCustomPropertyValue(name, out var animated))
            return animated;

        return ComputeUnanimatedCustomProperty(name, registration);
    }

    /// <summary>Computes the value of a custom property without the animations (the underlying value of the animations).</summary>
    private string? ComputeUnanimatedCustomProperty(string name, CustomPropertyRegistration? registration)
    {
        // The custom properties of the highlight pseudo-elements inherit from the originating element, even when they are
        // registered as not inherited (https://drafts.csswg.org/css-pseudo-4/#highlight-cascade)
        var inherits = (registration?.Inherits ?? true) || IsHighlight;
        if (!_cascaded.TryGetValue(name, out var cascaded))
            return inherits && Parent is { } parent ? InheritCustomPropertyValue(parent, name) : GetRegisteredInitialValue(registration);

        // A dependency cycle via the font-relative units (e.g. font-size: var(--x) with --x: 2em): the property is invalid at
        // computed-value time
        if (GetUnitCycle("font-size").Contains(name) || GetUnitCycle("line-height").Contains(name))
            return GetInvalidValue(name, registration);

        return ComputeCustomProperty(name, cascaded, registration, rollbacks: 0);
    }

    private string? ComputeCustomProperty(string name, CascadedValue cascaded, CustomPropertyRegistration? registration, int rollbacks)
    {
        var text = cascaded.Value.Value;
        if (TryResolveKeyword(name, text, registration, out var keywordValue))
            return keywordValue;

        List<(int Start, int End)>? taintedRanges = null;
        var substituted = CssSubstitution.MayContainSubstitution(text) ? CssSubstitution.Substitute(CssRandom.AssignIfConditionCachingKeys(name, text), GetSubstitutionContext(cascaded), out taintedRanges) : text;

        // A CSS-wide keyword that results from the substitution is treated as the keyword
        // (https://drafts.csswg.org/css-values-5/#substitute-arbitrary-substitution-function): revert, revert-layer and
        // revert-rule roll back the cascade from the declaration
        if (substituted is not null && !ReferenceEquals(substituted, text))
        {
            var keyword = InfraAscii.ToAsciiLowercase(substituted);
            if (cascaded.RollBackSubstitution is { } rollBack && rollbacks < MaxSubstitutionRollbacks && keyword is "revert" or "revert-layer" or "revert-rule")
                return ComputeCustomProperty(name, rollBack(keyword), registration, rollbacks + 1);

            if (TryResolveKeyword(name, substituted, registration, out keywordValue))
                return keywordValue;
        }

        if (registration is null || registration.Syntax.IsUniversal)
        {
            if (taintedRanges is not null)
        {
            Rare.CustomPropertyTainted = true;
        }
            return substituted;
        }

        // A registered custom property: the value must match its syntax, and it is computed
        // (https://drafts.css-houdini.org/css-properties-values-api/#calculation-of-computed-values). An attr()-tainted value
        // used in a URL is invalid at computed-value time (https://drafts.csswg.org/css-values-5/#attr-security).
        if (substituted is not null && CssSubstitution.UsesTaintedUrl(substituted, taintedRanges))
        {
            substituted = null;
        }

        // The random() functions get their random caching key (https://drafts.csswg.org/css-values-5/#random-caching-key)
        if (substituted is not null && substituted.Contains("random(", StringComparison.OrdinalIgnoreCase))
        {
            var values = CssParser.ParseListOfComponentValues(substituted);
            if (CssRandom.AssignCachingKeys(name, values) is var keyed && !ReferenceEquals(keyed, values))
            {
                substituted = CssSerializer.SerializeValue(keyed, property: null);
            }
        }

        if (substituted is null || registration.Syntax.Compute(this, substituted, cascaded.BaseUrl) is not { } computed)
            return GetUnsetValue(name, registration);

        if (taintedRanges is not null)
        {
            Rare.CustomPropertyTainted = true;
        }
        return computed;
    }

    /// <summary>Resolves a CSS-wide keyword value of a custom property (https://drafts.csswg.org/css-variables/#defining-variables).</summary>
    private bool TryResolveKeyword(string name, string text, CustomPropertyRegistration? registration, out string? value)
    {
        var inherits = registration?.Inherits ?? true;
        switch (InfraAscii.ToAsciiLowercase(text.Trim()))
        {
            case "initial":
                value = GetRegisteredInitialValue(registration);
                return true;
            case "inherit":
                value = Parent is { } inheritParent ? InheritCustomPropertyValue(inheritParent, name) : GetRegisteredInitialValue(registration);
                return true;
            case "unset" or "revert" or "revert-layer" or "revert-rule":
                value = inherits && Parent is { } unsetParent ? InheritCustomPropertyValue(unsetParent, name) : GetRegisteredInitialValue(registration);
                return true;
            default:
                value = null;
                return false;
        }
    }


    /// <summary>
    /// The value of a custom property that is invalid at computed-value time (https://drafts.csswg.org/css-variables/#invalid-at-computed-value-time):
    /// the guaranteed-invalid value, or the unset value of a registered custom property whose syntax is not universal.
    /// </summary>
    private string? GetInvalidValue(string name, CustomPropertyRegistration? registration) =>
        registration is null || registration.Syntax.IsUniversal ? null : GetUnsetValue(name, registration);

    /// <summary>The value of a registered custom property that is unset (e.g. invalid at computed-value time): inherited or initial.</summary>
    private string? GetUnsetValue(string name, CustomPropertyRegistration registration) =>
        (registration.Inherits || IsHighlight) && Parent is { } parent ? InheritCustomPropertyValue(parent, name) : GetRegisteredInitialValue(registration);

    private string? GetRegisteredInitialValue(CustomPropertyRegistration? registration)
    {
        if (registration?.InitialValue is not { } initial)
            return null;

        return registration.Syntax.Compute(this, initial, baseUrl: null) ?? initial;
    }

    /// <summary>The names of the custom properties that have a value (declared, inherited or the initial value of a registered property), in lexicographical order.</summary>
    public IReadOnlyList<string> GetCustomPropertyNames()
    {
        if (_rareData?.CustomPropertyNames is { } customPropertyNames)
            return customPropertyNames;

        var names = new SortedSet<string>(RegisteredProperties.Keys, StringComparer.Ordinal);
        for (var style = this; style is not null; style = style.Parent)
        {
            foreach (var name in style._cascaded.Keys)
            {
                if (CssPropertyRegistry.IsCustomProperty(name))
                {
                    names.Add(name);
                }
            }

            style.AddAnimatedCustomPropertyNames(names);
        }

        return Rare.CustomPropertyNames = [.. names.Where(name => GetCustomPropertyValue(name) is not null)];
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Dependency cycles via relative units (https://drafts.css-houdini.org/css-properties-values-api/#dependency-cycles-via-relative-units)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the custom properties of the element that are in a dependency cycle with a property (font-size or line-height):
    /// the property references them with var() (directly or through the custom properties declared on the element) and they
    /// depend on a registered custom property whose value uses units relative to the property (e.g. font-size: var(--x) with
    /// --x: 2em). The property and the custom properties of the cycle are invalid at computed-value time.
    /// </summary>
    private HashSet<string> GetUnitCycle(string property)
    {
        // Without registered custom properties, there is no cycle
        if (RegisteredProperties.Count == 0)
            return NoUnitCycle;

        var unitCycles = Rare.UnitCycles ??= new(StringComparer.Ordinal);
        if (unitCycles.TryGetValue(property, out var cycle))
            return cycle;

        // A reentrant call (while a referenced custom property is computed) finds no cycle
        cycle = new(StringComparer.Ordinal);
        unitCycles[property] = cycle;
        if (RegisteredProperties.Count == 0 || !_cascaded.TryGetValue(property, out var cascaded))
            return cycle;

        // The custom properties declared on the element that the property depends on, with their references (the fallbacks
        // are dependencies too)
        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var pending = new Stack<string>(GetReferences(cascaded.Value.ShorthandValue ?? cascaded.Value.Value));
        while (pending.TryPop(out var name))
        {
            if (references.ContainsKey(name) || !_cascaded.TryGetValue(name, out var declared))
                continue;

            var names = GetReferences(declared.Value.Value);
            references[name] = names;
            foreach (var reference in names)
            {
                pending.Push(reference);
            }
        }

        foreach (var name in references.Keys)
        {
            if (RegisteredProperties.TryGetValue(name, out var registration) && !registration.Syntax.IsUniversal
                && UsesRelativeUnits(CssParser.ParseListOfComponentValues(_cascaded[name].Value.Value), property, depth: 0))
            {
                cycle.Add(name);
            }
        }

        // The custom properties between the property and the registered custom properties are in the cycle too
        if (cycle.Count > 0)
        {
            bool added;
            do
            {
                added = false;
                foreach (var (name, names) in references)
                {
                    if (!cycle.Contains(name) && names.Overlaps(cycle))
                    {
                        added |= cycle.Add(name);
                    }
                }
            }
            while (added);
        }

        return cycle;
    }

    /// <summary>
    /// Returns the names of the custom properties referenced by the var() functions of a value, including the ones of the values
    /// of the attributes and of the fallbacks that its attr() functions use.
    /// </summary>
    private HashSet<string> GetReferences(string text)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        if (CssSubstitution.MayContainSubstitution(text))
        {
            CollectReferences(CssParser.ParseListOfComponentValues(text), references, depth: 0);
        }

        return references;
    }

    private void CollectReferences(List<CssComponentValue> values, HashSet<string> references, int depth)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "attr") && depth < MaxUnitDependencyDepth:
                    CollectAttrReferences(function, references, depth);
                    break;
                case CssFunction function:
                    if (InfraAscii.EqualsIgnoreAsciiCase(function.Name, "var") && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.Ident } name, ..])
                    {
                        references.Add(name.Token.Value);
                    }

                    CollectReferences(function.Value, references, depth);
                    break;
                case CssSimpleBlock block:
                    CollectReferences(block.Value, references, depth);
                    break;
            }
        }
    }

    /// <summary>
    /// Collects the references of an attr() function: the ones of the value of the attribute when it is substituted (type()),
    /// or the ones of the fallback when the attribute is missing or its value is invalid.
    /// </summary>
    private void CollectAttrReferences(CssFunction function, HashSet<string> references, int depth)
    {
        var comma = function.Value.FindIndex(v => v is CssPreservedToken { Type: CssTokenType.Comma });
        var first = comma < 0 ? function.Value : function.Value[..comma];
        var fallback = comma < 0 ? [] : function.Value[(comma + 1)..];
        if (ValueMatcher.ContainsSubstitutionFunction(first))
        {
            CollectReferences(function.Value, references, depth);
            return;
        }

        if (CssSubstitution.ParseAttrArguments(first) is not { } arguments)
            return;

        var attributeValue = Element.GetAttributeValue(CssSubstitution.GetAttrName(Element, arguments));
        var usesFallback = attributeValue is null;
        if (attributeValue is not null)
        {
            switch (arguments.Kind)
            {
                case CssSubstitution.AttrTypeKind.Number or CssSubstitution.AttrTypeKind.Unit:
                    usesFallback = CssSubstitution.ParseAttrNumber(attributeValue) is null || arguments is { Kind: CssSubstitution.AttrTypeKind.Unit, Unit: null };
                    break;
                case CssSubstitution.AttrTypeKind.Type:
                    var values = CssParser.ParseListOfComponentValues(attributeValue);
                    if (ValueMatcher.ContainsSubstitutionFunction(values))
                    {
                        CollectReferences(values, references, depth + 1);
                    }
                    else
                    {
                        usesFallback = !arguments.Syntax!.TryMatch(values, out _);
                    }

                    break;
            }
        }

        if (usesFallback)
        {
            CollectReferences(fallback, references, depth);
        }
    }

    /// <summary>
    /// Returns whether a value uses units relative to a property: em, ex, cap, ch, ic and lh for font-size, lh for line-height,
    /// and their root equivalents on the root element. The unregistered custom properties it references are substituted.
    /// </summary>
    private bool UsesRelativeUnits(List<CssComponentValue> values, string property, int depth)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                    var unit = InfraAscii.ToAsciiLowercase(dimension.Token.Value);
                    if (unit is "lh" || (IsRoot && unit is "rlh") || (property == "font-size" && (unit is "em" or "ex" or "cap" or "ch" or "ic" || (IsRoot && unit is "rem" or "rex" or "rcap" or "rch" or "ric"))))
                        return true;

                    break;
                case CssFunction function:
                    if (InfraAscii.EqualsIgnoreAsciiCase(function.Name, "var") && depth < MaxUnitDependencyDepth && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.Ident } name, ..]
                        && (!RegisteredProperties.TryGetValue(name.Token.Value, out var registration) || registration.Syntax.IsUniversal)
                        && GetCustomPropertyValue(name.Token.Value) is { } referenced && UsesRelativeUnits(CssParser.ParseListOfComponentValues(referenced), property, depth + 1))
                    {
                        return true;
                    }

                    if (UsesRelativeUnits(function.Value, property, depth))
                        return true;

                    break;
                case CssSimpleBlock block when UsesRelativeUnits(block.Value, property, depth):
                    return true;
            }
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Logical properties (https://drafts.csswg.org/css-logical/#box)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The logical and physical properties of a logical property group share their computed value: the declaration with the
    /// highest precedence wins, whether it is logical or physical. The logical properties are mapped using the writing mode
    /// and the direction of the element.
    /// </summary>
    private void EnsureLogicalPropertiesResolved(string property)
    {
        if (_logicalPropertiesResolved || property is "writing-mode" or "direction" or "text-orientation")
            return;

        _logicalPropertiesResolved = true;
        if (!HasFlowRelativeProperty(_cascaded))
            return;

        // The mapping depends on the writing mode of the element: the cascaded values the style was created with are not modified
        // (the style engine compares them with a new cascade, see InitialCascade). The cascaded values shared with other elements
        // are mapped once for each writing mode and direction, and the mapped values are shared too.
        if (!_cascadedShared)
        {
            _cascaded = MapLogicalProperties(_cascaded);
            return;
        }

        var writingMode = GetComputedValue("writing-mode");
        var rightToLeft = LogicalProperties.IsRightToLeft(this);
        var mappedCascades = Engine.GetLogicalCascades(_cascaded);
        foreach (var (mappedWritingMode, mappedRightToLeft, mapped) in mappedCascades)
        {
            if (mappedRightToLeft == rightToLeft && string.Equals(mappedWritingMode, writingMode, StringComparison.Ordinal))
            {
                _cascaded = mapped;
                return;
            }
        }

        _cascaded = MapLogicalProperties(_cascaded);
        mappedCascades.Add((writingMode, rightToLeft, _cascaded));

        static bool HasFlowRelativeProperty(Dictionary<string, CascadedValue> cascaded)
        {
            foreach (var name in cascaded.Keys)
            {
                if (LogicalProperties.IsFlowRelative(name))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Returns a copy of the cascaded values where the logical properties are mapped to the physical properties of the element.</summary>
    private Dictionary<string, CascadedValue> MapLogicalProperties(Dictionary<string, CascadedValue> cascaded)
    {
        var result = new Dictionary<string, CascadedValue>(cascaded, StringComparer.Ordinal);
        foreach (var (name, value) in cascaded)
        {
            if (LogicalProperties.GetPhysicalProperty(name, this) is { } physical && (!result.TryGetValue(physical, out var existing) || value.WinsOver(existing)))
            {
                result[physical] = value;
            }
        }

        return result;
    }

    /// <summary>The state of the styles that use custom properties or mixins: most styles do not.</summary>
    private RareData Rare => _rareData ??= new RareData();

    private sealed class RareData
    {
        public Dictionary<string, string?>? CustomProperties;
        public HashSet<string>? TaintedCustomProperties;
        public Dictionary<CssMixinApplication, CssFunctionFrame>? MixinFrames;
        public bool CustomPropertyTainted;
        public List<string>? CustomPropertyStack;
        public List<string>? CustomPropertyNames;
        public HashSet<string>? CustomPropertyCycles;
        public Dictionary<string, HashSet<string>>? UnitCycles;
    }

    /// <summary>
    /// The computed values of a style, by property: most styles only have a few computed values (e.g. display), which are
    /// stored in a small array, searched linearly. The other styles store a bitmap of the computed properties, by property
    /// index (see <see cref="CssPropertyRegistry.GetIndex"/>), and the values of these properties in a packed array, in the
    /// order of their index: unlike a dictionary, it has no buckets, no hash codes and no names, and unlike an array indexed
    /// by property, it does not have a slot for each of the hundreds of properties.
    /// </summary>
    private struct ComputedValues
    {
        private const int MaxArrayLength = 8;

        private (string Property, string Value)[]? _array;

        /// <summary>The number of values (in <see cref="_array"/> or in <see cref="_values"/>).</summary>
        private int _count;

        /// <summary>The bit of the index of each property that has a value in <see cref="_values"/>, once there are more than <see cref="MaxArrayLength"/> values.</summary>
        private ulong[]? _present;

        /// <summary>The values of the properties of <see cref="_present"/>, in the order of their index.</summary>
        private string[]? _values;

        public readonly bool TryGetValue(string property, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out string value)
        {
            if (_present is not null)
            {
                var index = CssPropertyRegistry.GetIndex(property);
                if (index >= 0 && IsPresent(index))
                {
                    value = _values![GetPosition(index)];
                    return true;
                }

                value = null;
                return false;
            }

            var array = _array;
            for (var i = 0; i < _count; i++)
            {
                ref var entry = ref array![i];
                if (ReferenceEquals(entry.Property, property) || string.Equals(entry.Property, property, StringComparison.Ordinal))
                {
                    value = entry.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public readonly bool ContainsKey(string property) => TryGetValue(property, out _);

        public void Set(string property, string value)
        {
            if (_present is not null)
            {
                // The values are only stored by property index: the names that are not supported properties are never computed
                var index = CssPropertyRegistry.GetIndex(property);
                if (index >= 0)
                {
                    SetByIndex(index, value);
                }

                return;
            }

            for (var i = 0; i < _count; i++)
            {
                if (string.Equals(_array![i].Property, property, StringComparison.Ordinal))
                {
                    _array[i].Value = value;
                    return;
                }
            }

            if (_count == MaxArrayLength)
            {
                var array = _array!;
                _array = null;
                _count = 0;
                _present = new ulong[(CssPropertyRegistry.PropertyNameCount + 63) / 64];
                _values = new string[MaxArrayLength * 2];
                foreach (var (existingProperty, existingValue) in array)
                {
                    Set(existingProperty, existingValue);
                }

                Set(property, value);
                return;
            }

            if (_array is null || _count == _array.Length)
            {
                Array.Resize(ref _array, _array is null ? 4 : MaxArrayLength);
            }

            _array[_count++] = (property, value);
        }

        private readonly bool IsPresent(int index) => (_present![index >> 6] & (1UL << index)) != 0;

        /// <summary>The position of the value of the property in <see cref="_values"/>: the number of present properties whose index is lower.</summary>
        private readonly int GetPosition(int index)
        {
            var present = _present!;
            var word = index >> 6;
            var position = System.Numerics.BitOperations.PopCount(present[word] & ((1UL << index) - 1));
            for (var i = 0; i < word; i++)
            {
                position += System.Numerics.BitOperations.PopCount(present[i]);
            }

            return position;
        }

        private void SetByIndex(int index, string value)
        {
            var position = GetPosition(index);
            if (IsPresent(index))
            {
                _values![position] = value;
                return;
            }

            if (_count == _values!.Length)
            {
                // The arrays grow by half of their length, as the number of computed values is usually far from the number of properties
                Array.Resize(ref _values, Math.Min(CssPropertyRegistry.PropertyNameCount, _count + (_count / 2)));
            }

            Array.Copy(_values, position, _values, position + 1, _count - position);
            _values[position] = value;
            _present![index >> 6] |= 1UL << index;
            _count++;
        }
    }
}
