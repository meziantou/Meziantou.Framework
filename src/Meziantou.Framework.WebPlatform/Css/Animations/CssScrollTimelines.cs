using Meziantou.Framework.WebPlatform.Animations;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>The kind of a &lt;single-animation-timeline&gt; (https://drafts.csswg.org/css-animations-2/#animation-timeline).</summary>
internal enum CssTimelineKind
{
    /// <summary>The default document timeline.</summary>
    Auto,

    /// <summary>No timeline.</summary>
    None,

    /// <summary>A named scroll progress timeline or view progress timeline (a &lt;dashed-ident&gt;).</summary>
    Named,

    /// <summary>An anonymous scroll progress timeline (<c>scroll()</c>).</summary>
    Scroll,

    /// <summary>An anonymous view progress timeline (<c>view()</c>).</summary>
    View,
}

/// <summary>An item of the computed value of <c>animation-timeline</c>.</summary>
internal sealed record CssTimelineReference(CssTimelineKind Kind, string? Name = null, ScrollTimelineScroller Scroller = ScrollTimelineScroller.Nearest, ScrollAxis Axis = ScrollAxis.Block, LengthPercentage? InsetStart = null, LengthPercentage? InsetEnd = null)
{
    public static CssTimelineReference Auto { get; } = new(CssTimelineKind.Auto);

    public static CssTimelineReference None { get; } = new(CssTimelineKind.None);

    /// <summary>
    /// Parses an item of the computed value of <c>animation-timeline</c> (https://drafts.csswg.org/css-animations-2/#animation-timeline):
    /// <c>auto</c>, <c>none</c>, a &lt;dashed-ident&gt;, <c>scroll()</c> or <c>view()</c>.
    /// </summary>
    public static CssTimelineReference Parse(string item)
    {
        if (item == "auto")
            return Auto;

        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(item));
        switch (values)
        {
            case [CssPreservedToken { Type: CssTokenType.Ident } ident] when ident.IsIdent("none"):
                return None;
            case [CssPreservedToken { Type: CssTokenType.Ident } ident] when ident.Value.StartsWith("--", StringComparison.Ordinal):
                return new CssTimelineReference(CssTimelineKind.Named, ident.Value);
            case [CssFunction function] when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "scroll"):
            {
                var scroller = ScrollTimelineScroller.Nearest;
                var axis = ScrollAxis.Block;
                foreach (var argument in ValueMatcher.RemoveWhitespace(function.Value))
                {
                    if (argument is not CssPreservedToken { Type: CssTokenType.Ident } keyword)
                        continue;

                    if (TryParseAxis(keyword.Value, out var parsedAxis))
                    {
                        axis = parsedAxis;
                    }
                    else
                    {
                        scroller = InfraAscii.ToAsciiLowercase(keyword.Value) switch
                        {
                            "root" => ScrollTimelineScroller.Root,
                            "self" => ScrollTimelineScroller.Self,
                            _ => ScrollTimelineScroller.Nearest,
                        };
                    }
                }

                return new CssTimelineReference(CssTimelineKind.Scroll, Scroller: scroller, Axis: axis);
            }

            case [CssFunction function] when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "view"):
            {
                var axis = ScrollAxis.Block;
                var insets = new List<CssComponentValue>(2);
                foreach (var argument in ValueMatcher.RemoveWhitespace(function.Value))
                {
                    if (argument is CssPreservedToken { Type: CssTokenType.Ident } keyword && TryParseAxis(keyword.Value, out var parsedAxis))
                    {
                        axis = parsedAxis;
                    }
                    else
                    {
                        insets.Add(argument);
                    }
                }

                var (start, end) = ViewTimeline.ParseComputedInset(insets);
                return new CssTimelineReference(CssTimelineKind.View, Axis: axis, InsetStart: start, InsetEnd: end);
            }

            default:
                return Auto;
        }
    }

    /// <summary>Parses an &lt;axis&gt; keyword (https://drafts.csswg.org/scroll-animations-1/#scroll-notation).</summary>
    public static bool TryParseAxis(string value, out ScrollAxis axis)
    {
        switch (InfraAscii.ToAsciiLowercase(value))
        {
            case "block":
                axis = ScrollAxis.Block;
                return true;
            case "inline":
                axis = ScrollAxis.Inline;
                return true;
            case "x":
                axis = ScrollAxis.X;
                return true;
            case "y":
                axis = ScrollAxis.Y;
                return true;
            default:
                axis = ScrollAxis.Block;
                return false;
        }
    }
}

/// <summary>
/// The named scroll progress timelines and view progress timelines declared by the styles of the elements of a document
/// (<c>scroll-timeline</c>, <c>view-timeline</c>) and their scopes (<c>timeline-scope</c>), and the lookup of the timeline
/// referenced by a name (https://drafts.csswg.org/scroll-animations-1/#timeline-scoping).
/// </summary>
/// <remarks>
/// The declarations are collected from the computed styles once the styles of all the elements are computed (see
/// <see cref="Dom.Document.FlushAnimationStyles"/>), then the CSS animations that reference a name are associated with their
/// target timeline. The timeline objects are kept while their declaration is (their axis and inset are updated).
/// </remarks>
internal sealed class CssScrollTimelines
{
    private readonly Document _document;
    private Dictionary<TimelineOwner, ElementTimelines> _declarations = [];
    private List<TimelineOwner> _order = [];
    private ScrollTimeline? _inactiveTimeline;

    public CssScrollTimelines(Document document)
    {
        _document = document;
    }

    /// <summary>Whether a CSS animation of the document references a timeline by name: the declarations are then collected by each style update.</summary>
    public bool HasNamedReferences { get; set; }

    /// <summary>The inactive timeline of the CSS animations whose timeline name matches no timeline (https://drafts.csswg.org/scroll-animations-1/#target-timeline).</summary>
    public ScrollTimeline InactiveTimeline => _inactiveTimeline ??= ScrollTimeline.CreateInactive(_document);

    /// <summary>
    /// An element or a pseudo-element (<c>::before</c>, <c>::after</c>) that declares named timelines or limits their scope: the
    /// inheritance parent of a pseudo-element is its originating element, and a pseudo-element has no descendant.
    /// </summary>
    private readonly record struct TimelineOwner(Element Element, string? PseudoElement)
    {
        public TimelineOwner? Parent => PseudoElement is not null ? new TimelineOwner(Element, null) : StyleEngine.GetInheritanceParent(Element) is { } parent ? new TimelineOwner(parent, null) : null;
    }

    /// <summary>The named timelines declared by an element or a pseudo-element, and the names whose scope it limits.</summary>
    private sealed class ElementTimelines
    {
        public Dictionary<string, ScrollTimeline> Timelines { get; } = new(StringComparer.Ordinal);

        /// <summary>The tree scope of the declaration of each name (a shadow root, or null for the document): the names are tree-scoped (https://drafts.csswg.org/css-scoping/#shadow-names).</summary>
        public Dictionary<string, Node?> Scopes { get; } = new(StringComparer.Ordinal);

        public bool TryGetTimeline(string name, Node? referenceScope, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ScrollTimeline? timeline)
        {
            if (Timelines.TryGetValue(name, out timeline) && IsVisible(Scopes.GetValueOrDefault(name), referenceScope))
                return true;

            timeline = null;
            return false;
        }

        public HashSet<string>? Scope { get; set; }

        public bool ScopesAll { get; set; }

        public bool IsScoped(string name) => ScopesAll || Scope?.Contains(name) == true;
    }

    /// <summary>
    /// Returns the timeline referenced by a name from an element or a pseudo-element (https://drafts.csswg.org/scroll-animations-1/#target-timeline),
    /// or null when the name matches no timeline (the timeline is then inactive).
    /// </summary>
    public ScrollTimeline? Find(Element element, string? pseudoElement, string name, Node? referenceScope)
    {
        for (TimelineOwner? current = new TimelineOwner(element, pseudoElement); current is { } owner; current = owner.Parent)
        {
            var declarations = _declarations.GetValueOrDefault(owner);
            if (declarations is not null && declarations.TryGetTimeline(name, referenceScope, out var timeline))
                return timeline;

            if (declarations?.IsScoped(name) == true || (owner.PseudoElement is null && ReferenceEquals(owner.Element, _document.DocumentElement)))
                return FindLastDescendantTimeline(owner, name, referenceScope);
        }

        return null;
    }

    /// <summary>
    /// Whether a name declared in a tree scope is visible from a reference in another tree scope (https://drafts.csswg.org/css-scoping/#shadow-names):
    /// the scope of the declaration is the scope of the reference or the scope of one of its shadow hosts.
    /// </summary>
    private static bool IsVisible(Node? declarationScope, Node? referenceScope)
    {
        if (declarationScope is null or Document)
            return true;

        for (var scope = referenceScope; scope is ShadowRoot shadowRoot; scope = shadowRoot.Host.GetRoot())
        {
            if (ReferenceEquals(scope, declarationScope))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the last timeline with the name declared by a flat tree descendant of an element, in flat tree order, whose scope
    /// is not limited by a descendant of the element (https://drafts.csswg.org/scroll-animations-1/#timeline-scope).
    /// </summary>
    private ScrollTimeline? FindLastDescendantTimeline(TimelineOwner root, string name, Node? referenceScope)
    {
        // (a pseudo-element has no descendant)
        if (root.PseudoElement is not null)
            return null;

        for (var i = _order.Count - 1; i >= 0; i--)
        {
            var candidate = _order[i];
            if (candidate == root || !_declarations[candidate].TryGetTimeline(name, referenceScope, out var timeline))
                continue;

            var inScope = false;
            for (TimelineOwner? current = candidate; current is { } owner; current = owner.Parent)
            {
                if (owner == root)
                {
                    inScope = true;
                    break;
                }

                if (_declarations.GetValueOrDefault(owner)?.IsScoped(name) == true)
                    break;
            }

            if (inScope)
                return timeline;
        }

        return null;
    }

    /// <summary>
    /// Collects the named timelines declared by the computed styles of the elements of the document and of their <c>::before</c>
    /// and <c>::after</c> pseudo-elements, in flat tree order (https://drafts.csswg.org/scroll-animations-1/#scroll-timelines-named,
    /// https://drafts.csswg.org/scroll-animations-1/#view-timelines-named). An element declares one timeline per name: the later
    /// names of a list win, and a scroll progress timeline wins over a view progress timeline.
    /// </summary>
    public void Update(StyleEngine engine)
    {
        var previous = _declarations;
        var declarations = new Dictionary<TimelineOwner, ElementTimelines>();
        var order = new List<TimelineOwner>();
        if (_document.DocumentElement is { } root)
        {
            Collect(engine, root, previous, declarations, order);
        }

        _declarations = declarations;
        _order = order;
    }

    private static void Collect(StyleEngine engine, Element root, Dictionary<TimelineOwner, ElementTimelines> previous, Dictionary<TimelineOwner, ElementTimelines> declarations, List<TimelineOwner> order)
    {
        // (an entry of a pseudo-element collects the ::after of the element, once its children are collected)
        var stack = new Stack<TimelineOwner>();
        stack.Push(new TimelineOwner(root, null));
        var children = new List<Element>();
        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            if (entry.PseudoElement is not null)
            {
                CollectPseudoElement(engine, entry, previous, declarations, order);
                continue;
            }

            var element = entry.Element;
            var style = engine.GetStyle(element);
            if (CollectElement(entry, style, previous.GetValueOrDefault(entry)) is { } elementTimelines)
            {
                declarations[entry] = elementTimelines;
                order.Add(entry);
            }

            if (Layout.BoxGeneration.GeneratesPseudoElements(element, style))
            {
                CollectPseudoElement(engine, new TimelineOwner(element, "before"), previous, declarations, order);
                stack.Push(new TimelineOwner(element, "after"));
            }

            children.Clear();
            foreach (var node in HtmlElement.GetFlatTreeChildren(element))
            {
                if (node is Element child)
                {
                    children.Add(child);
                }
            }

            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(new TimelineOwner(children[i], null));
            }
        }
    }

    /// <summary>Collects the named timelines declared by a <c>::before</c> or <c>::after</c> pseudo-element that has a content.</summary>
    private static void CollectPseudoElement(StyleEngine engine, TimelineOwner owner, Dictionary<TimelineOwner, ElementTimelines> previous, Dictionary<TimelineOwner, ElementTimelines> declarations, List<TimelineOwner> order)
    {
        if (!engine.MayHaveGeneratedContent(owner.Element, owner.PseudoElement!))
            return;

        var style = engine.GetStyle(owner.Element, owner.PseudoElement);
        if (style.GetComputedValue("content") is "none" or "normal")
            return;

        if (CollectElement(owner, style, previous.GetValueOrDefault(owner)) is { } timelines)
        {
            declarations[owner] = timelines;
            order.Add(owner);
        }
    }

    private static ElementTimelines? CollectElement(TimelineOwner owner, ComputedStyle style, ElementTimelines? previous)
    {
        var element = owner.Element;
        var scrollNames = style.GetComputedValue("scroll-timeline-name");
        var viewNames = style.GetComputedValue("view-timeline-name");
        var scope = style.GetComputedValue("timeline-scope");
        if (scrollNames == "none" && viewNames == "none" && scope == "none")
            return null;

        var result = new ElementTimelines();
        if (scope == "all")
        {
            result.ScopesAll = true;
        }
        else if (scope != "none")
        {
            result.Scope = new HashSet<string>(SplitItems(scope), StringComparer.Ordinal);
        }

        var viewScope = style.TryGetCascadedValue("view-timeline-name", out var viewCascaded) ? viewCascaded.TreeScope : null;
        var scrollScope = style.TryGetCascadedValue("scroll-timeline-name", out var scrollCascaded) ? scrollCascaded.TreeScope : null;
        if (viewNames != "none")
        {
            var names = SplitItems(viewNames);
            var axes = SplitItems(style.GetComputedValue("view-timeline-axis"));
            var insets = ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(style.GetComputedValue("view-timeline-inset"))));
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                if (name == "none")
                    continue;

                var axis = CssTimelineReference.TryParseAxis(axes[i % axes.Count], out var parsedAxis) ? parsedAxis : ScrollAxis.Block;
                var inset = insets.Count == 0 ? (null, null) : ViewTimeline.ParseComputedInset(insets[i % insets.Count]);
                ScrollTimeline timeline;
                if (previous?.Timelines.GetValueOrDefault(name) is ViewTimeline existing)
                {
                    existing.Axis = axis;
                    existing.Inset = inset;
                    timeline = existing;
                }
                else
                {
                    timeline = ViewTimeline.Create(element, owner.PseudoElement, axis, inset);
                }

                result.Timelines[name] = timeline;
                result.Scopes[name] = viewScope;
            }
        }

        if (scrollNames != "none")
        {
            var names = SplitItems(scrollNames);
            var axes = SplitItems(style.GetComputedValue("scroll-timeline-axis"));
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                if (name == "none")
                    continue;

                var axis = CssTimelineReference.TryParseAxis(axes[i % axes.Count], out var parsedAxis) ? parsedAxis : ScrollAxis.Block;
                ScrollTimeline timeline;
                if (previous?.Timelines.GetValueOrDefault(name) is { } existing && existing is not ViewTimeline)
                {
                    existing.Axis = axis;
                    timeline = existing;
                }
                else
                {
                    timeline = ScrollTimeline.Create(element, owner.PseudoElement, ScrollTimelineScroller.Source, axis);
                }

                result.Timelines[name] = timeline;
                result.Scopes[name] = scrollScope;
            }
        }

        return result;
    }

    private static List<string> SplitItems(string value)
    {
        var result = new List<string>();
        foreach (var item in ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value))))
        {
            result.Add(item is [CssPreservedToken { Type: CssTokenType.Ident } ident] ? ident.Value : CssSerializer.SerializeValue(item, property: null));
        }

        return result;
    }
}
