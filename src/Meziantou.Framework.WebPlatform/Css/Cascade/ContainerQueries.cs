using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>The axes of the size queries.</summary>
internal enum ContainerUnitAxis
{
    Width,
    Height,
    Inline,
    Block,
}

/// <summary>
/// Selects the query containers of the elements and evaluates the container queries
/// (https://drafts.csswg.org/css-conditional-5/#container-queries).
/// </summary>
internal static class ContainerQueries
{
    /// <summary>
    /// Evaluates a &lt;container-condition&gt; for an element, or for one of its pseudo-elements: the query container is
    /// selected among the ancestors of the element in the flat tree (the inclusive ancestors of the originating element of a
    /// pseudo-element) that are eligible for all the features of the query and whose name matches.
    /// </summary>
    public static bool? Evaluate(ContainerCondition condition, StyleEngine engine, Element element, string? pseudoElement)
    {
        // A query that contains an unknown feature selects no query container (https://drafts.csswg.org/css-conditional-5/#container-rule)
        if ((condition.Features & ContainerQueryFeatures.Unknown) != ContainerQueryFeatures.None)
            return null;

        var skipsOriginatingElement = (condition.Features & SizeFeatures) != ContainerQueryFeatures.None && IsSiblingOfOriginatingElement(element, pseudoElement);
        for (var ancestor = pseudoElement is null ? StyleEngine.GetInheritanceParent(element) : element; ancestor is not null; ancestor = StyleEngine.GetInheritanceParent(ancestor))
        {
            if (skipsOriginatingElement && ReferenceEquals(ancestor, element))
                continue;

            var style = engine.GetStyle(ancestor);
            if (!IsEligible(condition, style))
                continue;

            return condition.Query is null ? true : condition.Query.Evaluate(new ContainerQueryContext(style));
        }

        return null;
    }

    private const ContainerQueryFeatures SizeFeatures = ContainerQueryFeatures.Width | ContainerQueryFeatures.Height | ContainerQueryFeatures.InlineSize | ContainerQueryFeatures.BlockSize;

    /// <summary>
    /// Whether the box of a pseudo-element is a sibling of the box of its originating element: the <c>::scroll-marker-group</c>
    /// and the <c>::scroll-button()</c> pseudo-elements of an element other than the root element
    /// (https://drafts.csswg.org/css-overflow-5/#scroll-marker-group-pseudo). They are not in the box of their originating
    /// element, so it is not a query container of their size queries (it is for their style and scroll-state queries, as in
    /// the browsers).
    /// </summary>
    private static bool IsSiblingOfOriginatingElement(Element element, string? pseudoElement)
        => pseudoElement is not null && (pseudoElement == Layout.ScrollControls.MarkerGroup || Layout.ScrollControls.IsScrollButton(pseudoElement))
            && !ReferenceEquals(element, element.NodeDocument.DocumentElement);

    private static bool IsEligible(ContainerCondition condition, ComputedStyle style)
    {
        if (condition.Name is { } name && !HasContainerName(style, name))
            return false;

        var features = condition.Features;
        if ((features & ContainerQueryFeatures.ScrollState) != ContainerQueryFeatures.None && !GetContainerTypes(style).Contains("scroll-state", StringComparer.Ordinal))
            return false;

        if ((features & ContainerQueryFeatures.Anchored) != ContainerQueryFeatures.None && (style.Layout.ContainerType & ContainerType.Anchored) == ContainerType.Normal)
            return false;

        if ((features & ContainerQueryFeatures.Width) != ContainerQueryFeatures.None && !SupportsSizeQueries(style, ContainerUnitAxis.Width))
            return false;

        if ((features & ContainerQueryFeatures.Height) != ContainerQueryFeatures.None && !SupportsSizeQueries(style, ContainerUnitAxis.Height))
            return false;

        if ((features & ContainerQueryFeatures.InlineSize) != ContainerQueryFeatures.None && !SupportsSizeQueries(style, ContainerUnitAxis.Inline))
            return false;

        if ((features & ContainerQueryFeatures.BlockSize) != ContainerQueryFeatures.None && !SupportsSizeQueries(style, ContainerUnitAxis.Block))
            return false;

        return true;
    }

    /// <summary>https://drafts.csswg.org/css-conditional-5/#container-name: the names are compared case-sensitively.</summary>
    private static bool HasContainerName(ComputedStyle style, string name)
    {
        var value = style.GetComputedValue("container-name");
        if (value == "none")
            return false;

        foreach (var token in CssParser.ParseListOfComponentValues(value))
        {
            if (token is CssPreservedToken { Type: CssTokenType.Ident } ident && ident.Token.Value == name)
                return true;
        }

        return false;
    }

    private static string[] GetContainerTypes(ComputedStyle style) => style.GetComputedValue("container-type").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Returns whether the element is a query container for the size queries of an axis
    /// (https://drafts.csswg.org/css-conditional-5/#container-type): size establishes a query container for both axes,
    /// inline-size for the inline axis of the container.
    /// </summary>
    private static bool SupportsSizeQueries(ComputedStyle style, ContainerUnitAxis axis)
    {
        var types = GetContainerTypes(style);
        if (types.Contains("size", StringComparer.Ordinal))
            return true;

        if (!types.Contains("inline-size", StringComparer.Ordinal))
            return false;

        return axis switch
        {
            ContainerUnitAxis.Inline => true,
            ContainerUnitAxis.Width => !BoxSizes.IsVertical(style),
            ContainerUnitAxis.Height => BoxSizes.IsVertical(style),
            _ => false,
        };
    }

    /// <summary>
    /// Returns the size in px of the query container of a container query length unit in a physical axis
    /// (https://drafts.csswg.org/css-conditional-5/#container-lengths): the nearest ancestor that is an eligible query
    /// container for the size queries of the axis. Returns null when there is no eligible query container, or when its size
    /// is not known: the small viewport size is then used.
    /// </summary>
    public static double? GetContainerSize(ComputedStyle style, bool horizontal)
    {
        var element = style.Element;
        var skipsOriginatingElement = IsSiblingOfOriginatingElement(element, style.PseudoElement);
        for (var ancestor = style.PseudoElement is null ? StyleEngine.GetInheritanceParent(element) : element; ancestor is not null; ancestor = StyleEngine.GetInheritanceParent(ancestor))
        {
            if (skipsOriginatingElement && ReferenceEquals(ancestor, element))
                continue;

            var container = style.Engine.GetStyle(ancestor);
            if (SupportsSizeQueries(container, horizontal ? ContainerUnitAxis.Width : ContainerUnitAxis.Height) && BoxSizes.IsEligibleSizeContainer(container))
                return GetContainerContentSize(container, horizontal);
        }

        return null;
    }

    /// <summary>
    /// Returns the size of the content box of a query container in a physical axis, in the coordinates of its computed values
    /// (divided by its effective zoom), or null when it is unknown (or the container is not eligible: no principal box, or a box
    /// that cannot have layout containment).
    /// </summary>
    /// <remarks>
    /// The size of a rendered container is the size computed by its last layout (https://drafts.csswg.org/css-contain-3/#size-container):
    /// the layout lays out the size containers before their contents, and computes again the styles of their descendants when
    /// they were computed with another size (see <see cref="LayoutContext.OnSizeContainerLaidOut"/>), so the size used here is
    /// recorded (<see cref="StyleEngine.RecordQueriedContainerSize"/>). Before the first layout of the container, or without
    /// layout (a document without browsing context), the size known without layout is used (<see cref="BoxSizes"/>).
    /// The block size of a container whose block size depends on its contents (<c>container-type: inline-size</c> with an
    /// <c>auto</c> block size) is unknown.
    /// </remarks>
    internal static double? GetContainerContentSize(ComputedStyle container, bool horizontal)
    {
        if (!BoxSizes.IsEligibleSizeContainer(container))
            return null;

        var engine = container.Engine;
        var usesLayout = container.PseudoElement is null && engine.UsesLayoutContainerSizes;
        double? size;
        if (usesLayout && engine.Document.LayoutEngineOrNull is { } layout && layout.TryGetLaidOutContainerSize(container.Element, out var laidOut, out var hasBlockSize))
        {
            var inlineAxis = horizontal != container.Layout.IsVerticalWritingMode;
            var zoom = container.Layout.EffectiveZoom;
            size = inlineAxis || hasBlockSize ? (horizontal ? laidOut.Width : laidOut.Height) / zoom : null;
        }
        else
        {
            size = BoxSizes.GetContainerContentSize(container, horizontal);
        }

        if (usesLayout)
        {
            engine.RecordQueriedContainerSize(container.Element, horizontal, size);
        }

        return size;
    }

    /// <summary>
    /// Returns the computed style of an element or of one of its pseudo-elements, after computing the layout of its document
    /// when the style may depend on the size of a size container (an ancestor in the flat tree, or the originating element of
    /// a pseudo-element, whose <c>container-type</c> is <c>size</c> or <c>inline-size</c>; the computed values are computed
    /// lazily, so the container-relative lengths of the style may query the container later): the layout computes again the styles
    /// of the descendants of the size containers with their sizes (the style and layout interleaving,
    /// https://drafts.csswg.org/css-contain-3/#size-container). Used when the styles are exposed to the scripts
    /// (<c>getComputedStyle()</c>, <c>matchContainer()</c>). The layout also takes the first snapshot of the scroll-state query
    /// containers that were not laid out before (see <see cref="LayoutEngine.UpdateLayout"/>): their scroll-state queries are
    /// evaluated against their initial state.
    /// </summary>
    internal static ComputedStyle GetStyleWithContainerLayout(Element element, string? pseudoElement)
    {
        var document = element.NodeDocument;
        var engine = document.GetStyleEngine();
        var style = engine.GetStyle(element, pseudoElement);
        if (document.BrowsingContext is null || !IsInContainer(style, ContainerType.Size | ContainerType.InlineSize | ContainerType.ScrollState | ContainerType.Anchored))
            return style;

        // An up-to-date layout means that the styles were computed with the sizes of the containers
        var layout = document.GetLayoutEngine();
        if (!layout.NeedsLayout)
            return style;

        layout.EnsureLayout();
        return engine.GetStyle(element, pseudoElement);
    }

    /// <summary>
    /// Returns the state of a scroll-state query container: its last snapshot taken by the rendering steps
    /// (https://drafts.csswg.org/css-conditional-5/#updating-scroll-state, see <see cref="LayoutEngine.UpdateScrollStateSnapshots"/>).
    /// The state is none before the first snapshot, and without layout (a document without browsing context). Evaluating a
    /// scroll-state feature registers the document for the rendering steps: the snapshots are taken by the next rendering update,
    /// whose styles reflect them.
    /// </summary>
    internal static ScrollStateSnapshot GetScrollStateSnapshot(ComputedStyle container)
    {
        var engine = container.Engine;
        var document = engine.Document;
        if (container.PseudoElement is not null || !engine.UsesLayoutContainerSizes || document.BrowsingContext is null)
            return ScrollStateSnapshot.None;

        var layout = document.GetLayoutEngine();
        layout.OnScrollStateQueried();
        return layout.GetScrollStateSnapshot(container.Element);
    }

    /// <summary>
    /// Returns the position option of <c>position-try-fallbacks</c> applied to an anchored query container by its last layout, or null
    /// when none is applied (https://drafts.csswg.org/css-anchor-position-2/#anchored). The option is recorded by the style engine:
    /// the layout computes again the styles of the descendants of the container when it applies another option (see
    /// <see cref="StyleEngine.IsQueriedWithAnotherFallback"/>). None without layout (a document without browsing context, an engine
    /// with an environment override) and for a pseudo-element.
    /// </summary>
    internal static AnchoredFallback? GetAnchoredFallback(ComputedStyle container)
    {
        var engine = container.Engine;
        var document = engine.Document;
        if (container.PseudoElement is not null || !engine.UsesLayoutContainerSizes || document.BrowsingContext is null)
            return null;

        var fallback = document.GetLayoutEngine().GetAnchoredFallback(container.Element);
        engine.RecordQueriedAnchoredFallback(container.Element, fallback?.Text ?? "");
        return fallback;
    }

    /// <summary>Returns whether an ancestor of a style (the style of the parent element in the flat tree, or of the originating element of a pseudo-element) is a size container.</summary>
    internal static bool IsInSizeContainer(ComputedStyle style) => IsInContainer(style, ContainerType.Size | ContainerType.InlineSize);

    private static bool IsInContainer(ComputedStyle style, ContainerType types) => (style.AncestorContainerTypes & types) != ContainerType.Normal;
}

/// <summary>Evaluates the features of a container query against a query container.</summary>
internal sealed class ContainerQueryContext(ComputedStyle container)
{
    /// <summary>The types of the values of a &lt;style-range&gt;.</summary>
    private const string StyleRangeSyntax = "<number> | <percentage> | <length> | <angle> | <time> | <frequency> | <resolution>";

    /// <summary>The tolerance used to compare numbers (unit conversions are not exact).</summary>
    private const double Epsilon = 1e-9;

    private double? _width;
    private double? _height;
    private bool _widthResolved;
    private bool _heightResolved;

    public ComputedStyle Container { get; } = container;

    /// <summary>
    /// Whether the query is a style() test of an if() condition (https://drafts.csswg.org/css-values-5/#if-notation), evaluated
    /// against the element itself: only the custom properties can be queried, and a value that is invalid at computed-value
    /// time does not match.
    /// </summary>
    public bool IsIfTest { get; init; }

    /// <summary>The custom function whose if() test is evaluated: its local variables and parameters shadow the custom properties of the element.</summary>
    public CssFunctionFrame? Frame { get; init; }

    /// <summary>
    /// Whether the evaluation of the style() test of an if() condition used an attr()-tainted value (the queried custom
    /// property or the value it is compared to): the result of the if() is then attr()-tainted (https://drafts.csswg.org/css-values-5/#attr-security).
    /// </summary>
    public bool UsesTaintedValue { get; private set; }

    private double? Width
    {
        get
        {
            if (!_widthResolved)
            {
                _width = ContainerQueries.GetContainerContentSize(Container, horizontal: true);
                _widthResolved = true;
            }

            return _width;
        }
    }

    private double? Height
    {
        get
        {
            if (!_heightResolved)
            {
                _height = ContainerQueries.GetContainerContentSize(Container, horizontal: false);
                _heightResolved = true;
            }

            return _height;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Size features (https://drafts.csswg.org/css-conditional-5/#size-container)
    // ----------------------------------------------------------------------------------------------------------------

    public bool? EvaluateSizeFeature(ContainerSizeFeature feature, (ContainerQueryComparison Comparison, List<CssComponentValue> Value)[] comparisons)
    {
        var vertical = BoxSizes.IsVertical(Container);
        if (feature == ContainerSizeFeature.Orientation)
        {
            if (Width is not { } width || Height is not { } height)
                return null;

            if (comparisons.Length == 0)
                return true;

            var orientation = height >= width ? "portrait" : "landscape";
            return comparisons[0].Value is [CssPreservedToken { Type: CssTokenType.Ident } ident] && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, orientation);
        }

        double? featureValue = feature switch
        {
            ContainerSizeFeature.Width => Width,
            ContainerSizeFeature.Height => Height,
            ContainerSizeFeature.InlineSize => vertical ? Height : Width,
            ContainerSizeFeature.BlockSize => vertical ? Width : Height,
            _ => Width is { } w && Height is { } h ? w / h : null,
        };

        if (featureValue is not { } value)
            return null;

        // https://drafts.csswg.org/mediaqueries-5/#mq-boolean-context
        if (comparisons.Length == 0)
            return value != 0 && !double.IsNaN(value);

        var result = true;
        foreach (var (comparison, tokens) in comparisons)
        {
            var resolved = feature == ContainerSizeFeature.AspectRatio ? ResolveRatio(tokens) : ResolveLength(tokens);
            if (resolved is not { } other)
                return null;

            result &= Compare(value, comparison, other);
        }

        return result;
    }

    /// <summary>
    /// Resolves a &lt;length&gt; against the query container: the relative units and the custom properties are evaluated with
    /// the computed values of the container. A number is only valid when it is zero.
    /// </summary>
    private double? ResolveLength(List<CssComponentValue> tokens)
    {
        if (Substitute(tokens) is not { } text)
            return null;

        if (CustomPropertySyntax.ComputeGrammar(Container, "<length>", text, baseUrl: null) is { } length)
            return CssValueComputer.ParsePixels(length) ?? (CssValueComputer.TryParseNumber(length, out var unitless) && unitless == 0 ? 0 : null);

        return CustomPropertySyntax.ComputeGrammar(Container, "<number>", text, baseUrl: null) is { } number && CssValueComputer.TryParseNumber(number, out var zero) && zero == 0 ? 0 : null;
    }

    /// <summary>Resolves a &lt;ratio&gt; (https://drafts.csswg.org/css-values-4/#ratio-value): &lt;number [0,∞]&gt; [ / &lt;number [0,∞]&gt; ]?</summary>
    private double? ResolveRatio(List<CssComponentValue> tokens)
    {
        if (Substitute(tokens) is not { } text)
            return null;

        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(text));
        switch (values)
        {
            case [var single]:
                return ResolveNonNegativeNumber(single);

            case [var numerator, CssPreservedToken slash, var denominator] when slash.Token.IsDelim('/'):
                return ResolveNonNegativeNumber(numerator) is { } n && ResolveNonNegativeNumber(denominator) is { } d ? n / d : null;

            default:
                return null;
        }
    }

    private double? ResolveNonNegativeNumber(CssComponentValue value)
    {
        var text = CssSerializer.SerializeValue([value], property: null);
        return CustomPropertySyntax.ComputeGrammar(Container, "<number>", text, baseUrl: null) is { } number && CssValueComputer.TryParseNumber(number, out var result) && result >= 0 ? result : null;
    }

    /// <summary>Substitutes the arbitrary substitution functions of a value with the computed values of the container; null when it is invalid.</summary>
    private string? Substitute(List<CssComponentValue> tokens)
    {
        var text = CssSerializer.SerializeValue(tokens.Count == 1 ? tokens : InsertSpaces(tokens), property: null);
        return CssSubstitution.MayContainSubstitution(text) ? CssSubstitution.Substitute(text, Container) : text;
    }

    private static List<CssComponentValue> InsertSpaces(List<CssComponentValue> values)
    {
        var result = new List<CssComponentValue>();
        foreach (var value in values)
        {
            if (result.Count > 0)
            {
                result.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Whitespace, Value = " " }));
            }

            result.Add(value);
        }

        return result;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scroll-state features (https://drafts.csswg.org/css-conditional-5/#scroll-state-container)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Evaluates a scroll-state feature against the snapshotted state of the container (see <see cref="ContainerQueries.GetScrollStateSnapshot"/>):
    /// in a boolean context, true when the state is not none. The logical values map to the physical edges and axes with the
    /// writing mode and the direction of the query container (for <c>snapped</c> too, as in the browsers).
    /// </summary>
    public bool EvaluateScrollStateFeature(ContainerScrollStateFeature feature, string? value)
    {
        var snapshot = ContainerQueries.GetScrollStateSnapshot(Container);
        var state = feature switch
        {
            ContainerScrollStateFeature.Stuck => snapshot.Stuck,
            ContainerScrollStateFeature.Snapped => snapshot.Snapped,
            ContainerScrollStateFeature.Scrollable => snapshot.Scrollable,
            _ => snapshot.Scrolled,
        };

        if (value is null)
            return state != PhysicalEdges.None;

        if (value == "none")
            return state == PhysicalEdges.None;

        var direction = WritingDirection.FromStyle(Container.Layout);
        var blockAxis = direction.IsHorizontal ? PhysicalEdges.Vertical : PhysicalEdges.Horizontal;
        var inlineAxis = direction.IsHorizontal ? PhysicalEdges.Horizontal : PhysicalEdges.Vertical;

        // https://drafts.csswg.org/css-conditional-5/#snapped: a snap target in both axes matches both
        if (value == "both")
            return state == (PhysicalEdges.Horizontal | PhysicalEdges.Vertical);

        var edges = value switch
        {
            "top" => PhysicalEdges.Top,
            "right" => PhysicalEdges.Right,
            "bottom" => PhysicalEdges.Bottom,
            "left" => PhysicalEdges.Left,
            "block-start" => ToEdge(direction.BlockStart),
            "block-end" => ToEdge(direction.BlockEnd),
            "inline-start" => ToEdge(direction.InlineStart),
            "inline-end" => ToEdge(direction.InlineEnd),
            "x" => PhysicalEdges.Horizontal,
            "y" => PhysicalEdges.Vertical,
            "block" => blockAxis,
            "inline" => inlineAxis,
            _ => PhysicalEdges.None,
        };

        return (state & edges) != PhysicalEdges.None;

        static PhysicalEdges ToEdge(PhysicalSide side) => side switch
        {
            PhysicalSide.Top => PhysicalEdges.Top,
            PhysicalSide.Right => PhysicalEdges.Right,
            PhysicalSide.Bottom => PhysicalEdges.Bottom,
            _ => PhysicalEdges.Left,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Anchored features (https://drafts.csswg.org/css-anchor-position-2/#anchored-container)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Evaluates the <c>fallback</c> feature against the position option applied to the container by its last layout (see
    /// <see cref="ContainerQueries.GetAnchoredFallback"/>): in a boolean context, true when an option of <c>position-try-fallbacks</c>
    /// is applied. A name and try tactics match an option with the same name (not tree-scoped) and the same try tactics in the same
    /// order; a &lt;position-area-query&gt; matches an option that is a &lt;position-area&gt; of the same physical area (<c>any</c>
    /// matches any tracks of its axis).
    /// </summary>
    public bool EvaluateAnchoredFallback(AnchoredFallbackQuery? value)
    {
        var applied = ContainerQueries.GetAnchoredFallback(Container);
        if (value is null)
            return applied is not null;

        if (value.IsNone)
            return applied is null;

        if (applied is null)
            return false;

        if (value.PositionArea is { } query)
        {
            if (applied.PositionArea is not { } area)
                return false;

            var (horizontal, vertical) = PositionAreaValue.Parse(area).Resolve(applied.ContainingBlock, applied.Self);
            var (queryHorizontal, queryVertical) = PositionAreaValue.ResolveQuery(query, applied.ContainingBlock, applied.Self);
            return (queryHorizontal.IsAny || queryHorizontal == horizontal) && (queryVertical.IsAny || queryVertical == vertical);
        }

        return applied.PositionArea is null && string.Equals(applied.RuleName, value.RuleName, StringComparison.Ordinal) && applied.Tactics.SequenceEqual(value.Tactics);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Style features (https://drafts.csswg.org/css-conditional-5/#style-container)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A &lt;style-feature-plain&gt; is true when the computed value of the property on the container matches the value
    /// computed with respect to the container; a &lt;style-feature-boolean&gt; is true when the computed value is not the
    /// initial value. A shorthand matches when all its longhands match.
    /// </summary>
    public bool? EvaluateStyleFeature(string name, string? value)
    {
        if (IsIfTest && !CssPropertyRegistry.IsCustomProperty(name))
            return false;

        if (Frame is not null && Frame.DeclaresVariable(name))
        {
            var result = Frame.EvaluateStyleFeature(name, value, out var tainted);
            UsesTaintedValue |= tainted;
            return result;
        }

        if (IsIfTest)
        {
            UsesTaintedValue |= Container.IsCustomPropertyTainted(name);
        }

        // random() and random-item() are not allowed in a container query (https://github.com/w3c/csswg-drafts/issues/10982)
        if (!IsIfTest && value is not null && ContainsRandom(value))
            return false;

        // The value is computed with respect to the container: its arbitrary substitution functions refer to the container
        if (value is not null && CssSubstitution.MayContainSubstitution(value))
        {
            value = Substitute(value);

            // An unregistered custom property whose value is invalid at computed-value time is the guaranteed-invalid value
            if (value is null)
                return !IsIfTest && CssPropertyRegistry.IsCustomProperty(name) && !Container.RegisteredProperties.ContainsKey(name) && Container.GetCustomPropertyValue(name) is null;
        }

        // random() is not allowed in a container query (https://github.com/w3c/csswg-drafts/issues/10982)
        if (!IsIfTest && value is not null && ContainsRandom(value))
            return false;

        // The value of a registered custom property must match its syntax
        if (value is not null && Container.RegisteredProperties.TryGetValue(name, out var registration) && !registration.Syntax.IsUniversal
            && !ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(value.Trim())) && registration.Syntax.Compute(Container, value, baseUrl: null) is null)
        {
            return false;
        }

        // An empty custom property value is a space, as in a declaration
        var values = CssParser.ParseListOfComponentValues(value ?? "initial", out var source);
        var originalText = source.Trim();
        if (originalText.Length == 0 && CssPropertyRegistry.IsCustomProperty(name))
        {
            originalText = " ";
        }

        if (CssShorthands.ParseValue(name, values, originalText) is not { } longhands)
            return false;

        // The cascade-dependent keywords are invalid in a style feature
        foreach (var longhand in longhands)
        {
            if (longhand.ShorthandName is null && InfraAscii.ToAsciiLowercase(longhand.Value.Trim()) is "revert" or "revert-layer" or "revert-rule")
                return false;
        }

        var query = Container.WithDeclarations(longhands);
        foreach (var longhand in longhands)
        {
            bool matches;
            if (CssPropertyRegistry.IsCustomProperty(longhand.Name))
            {
                // The values of an unregistered custom property are token sequences
                var queryValue = query.GetCustomPropertyValue(longhand.Name);
                var containerValue = Container.GetCustomPropertyValue(longhand.Name);
                matches = queryValue == containerValue
                    || (Container.RegisteredProperties.ContainsKey(longhand.Name) && queryValue is not null && containerValue is not null && AreEquivalentColors(queryValue, containerValue));
            }
            else
            {
                var queryValue = query.GetComputedValue(longhand.Name);
                var containerValue = Container.GetComputedValue(longhand.Name);
                matches = queryValue == containerValue || AreEquivalentColors(queryValue, containerValue);
            }

            if (value is null)
            {
                // Boolean context: true when the value is not the initial value
                if (!matches)
                    return true;
            }
            else if (!matches)
            {
                return false;
            }
        }

        return value is not null;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-color-4/#comparing-color-values: the powerless components are missing; two colors of the
    /// same color space (hsl, hwb and the legacy colors are sRGB colors) are equivalent when their components are equal (a
    /// missing component is zero), and two colors of different color spaces when they are equal in Oklab.
    /// </summary>
    private static bool AreEquivalentColors(string first, string second)
    {
        if (ParseColor(first) is not { } a || ParseColor(second) is not { } b)
            return false;

        if (a.Space != b.Space)
        {
            a = CssColorConversion.Convert(a, "oklab");
            b = CssColorConversion.Convert(b, "oklab");
        }

        for (var i = 0; i < 3; i++)
        {
            if (!AreEqualComponents(a.Channels[i], b.Channels[i]))
                return false;
        }

        return AreEqualComponents(a.Alpha, b.Alpha);

        static CssColorComponents? ParseColor(string value)
        {
            if (!value.Contains('(', StringComparison.Ordinal) || ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is not [var token]
                || CssColor.Parse(token) is not { } color || CssColorConversion.FromColor(color) is not { } components)
            {
                return null;
            }

            // https://drafts.csswg.org/css-color-4/#powerless: the hue of an achromatic lch() or oklch() color is powerless
            var channels = components.Channels;
            if (components.Space is "lch" or "oklch" && channels[1] is { } chroma && chroma <= (components.Space == "lch" ? 0.0015 : 0.000004))
            {
                channels = [channels[0], channels[1], null];
            }

            return components with { Channels = channels };
        }

        static bool AreEqualComponents(double? first, double? second) => Math.Abs((first ?? 0) - (second ?? 0)) <= 1e-6;
    }

    /// <summary>https://drafts.csswg.org/css-conditional-5/#evaluate-a-style-range</summary>
    public bool? EvaluateStyleRange(ContainerStyleRangeOperand[] operands, ContainerQueryComparison[] comparisons)
    {
        var values = new (double Value, string Unit)[operands.Length];
        for (var i = 0; i < operands.Length; i++)
        {
            if (ResolveStyleRangeValue(operands[i]) is not { } resolved)
                return false;

            values[i] = resolved;
        }

        for (var i = 0; i < comparisons.Length; i++)
        {
            var left = values[i];
            var right = values[i + 1];

            // A unitless zero is a zero <length> when it is compared against a <length>
            if (left.Unit != right.Unit)
            {
                if (left is { Unit: "", Value: 0 } && right.Unit == "px")
                {
                    left = (0, "px");
                }
                else if (right is { Unit: "", Value: 0 } && left.Unit == "px")
                {
                    right = (0, "px");
                }
                else
                {
                    return false;
                }
            }

            if (!Compare(left.Value, comparisons[i], right.Value))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Substitutes the value (a custom property name is substituted as var()), then computes it as a &lt;number&gt;,
    /// &lt;percentage&gt;, &lt;length&gt;, &lt;angle&gt;, &lt;time&gt;, &lt;frequency&gt; or &lt;resolution&gt;.
    /// Returns the value in the canonical unit ("" for a number), or null.
    /// </summary>
    private (double Value, string Unit)? ResolveStyleRangeValue(ContainerStyleRangeOperand operand)
    {
        var text = operand.CustomPropertyName is { } name ? "var(" + CssSerializer.SerializeIdentifier(name) + ")" : operand.Value!;
        if (!IsIfTest && ContainsRandom(text))
            return null;

        var substituted = CssSubstitution.MayContainSubstitution(text) ? Substitute(text) : text;
        if (substituted is null || (!IsIfTest && ContainsRandom(substituted)) || CustomPropertySyntax.ComputeGrammar(Container, StyleRangeSyntax, substituted, baseUrl: null) is not { } computed)
            return null;

        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(computed));
        switch (values)
        {
            case [CssPreservedToken { Type: CssTokenType.Number } number]:
                return (number.Token.NumericValue, "");

            case [CssPreservedToken { Type: CssTokenType.Percentage } percentage]:
                return (percentage.Token.NumericValue, "%");

            case [CssPreservedToken { Type: CssTokenType.Dimension } dimension]:
                return CssCalc.TryConvertToCanonicalUnit(dimension.Token.NumericValue, InfraAscii.ToAsciiLowercase(dimension.Token.Value), out var value, out var unit) ? (value, unit) : null;

            default:
                return null;
        }
    }

    private static bool ContainsRandom(string value) => value.Contains("random(", StringComparison.OrdinalIgnoreCase) || value.Contains("random-item(", StringComparison.OrdinalIgnoreCase);

    /// <summary>Substitutes the arbitrary substitution functions of a value of the query, in the custom function of an if() test.</summary>
    private string? Substitute(string text)
    {
        var substituted = CssSubstitution.Substitute(text, Frame is { } frame ? frame.Context : new SubstitutionContext(Container, TreeScope: null), out var taintedRanges);
        UsesTaintedValue |= taintedRanges is not null;
        return substituted;
    }

    private static bool Compare(double left, ContainerQueryComparison comparison, double right)
    {
        if (double.IsNaN(left) || double.IsNaN(right))
            return false;

        var result = left.CompareTo(right);
        if (result != 0 && double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) <= Epsilon * Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right))))
        {
            result = 0;
        }

        return comparison switch
        {
            ContainerQueryComparison.Less => result < 0,
            ContainerQueryComparison.LessOrEqual => result <= 0,
            ContainerQueryComparison.Greater => result > 0,
            ContainerQueryComparison.GreaterOrEqual => result >= 0,
            _ => result == 0,
        };
    }
}
