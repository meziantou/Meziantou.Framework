using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The anchor box of an anchor element, as seen by an absolutely positioned box (https://drafts.csswg.org/css-anchor-position-1/#determining):
/// the bounding rectangle of the border boxes of its fragments (transforms included) in the coordinates of the fragment of the host
/// of the positioned box, at the scroll positions 0, and the scroll containers between the anchor and the containing block.
/// </summary>
internal sealed class AnchorBox
{
    public AnchorBox(ContainerBox box, PhysicalRect unscrolledRect, ContainerBox[] scrollContainers, bool includesViewportScroll, Fragment containingBlock)
    {
        ContainingBlock = containingBlock;
        Box = box;
        UnscrolledRect = unscrolledRect;
        ScrollContainers = scrollContainers;
        IncludesViewportScroll = includesViewportScroll;
    }

    /// <summary>The fragment of the containing block of the positioned box.</summary>
    public Fragment ContainingBlock { get; }

    /// <summary>The fragment the anchor box was measured in (its fragment, or the fragment of the block container of an inline box), or null.</summary>
    public BoxFragment? Fragment { get; init; }

    /// <summary>The rectangle of the anchor box in the coordinates of <see cref="Fragment"/> (in its scrolled contents when <see cref="InScrolledContents"/>).</summary>
    public PhysicalRect RectInFragment { get; init; }

    /// <summary>Whether <see cref="RectInFragment"/> is in the scrolled contents of <see cref="Fragment"/> (a scroll container).</summary>
    public bool InScrolledContents { get; init; }

    /// <summary>The principal box of the anchor element.</summary>
    public ContainerBox Box { get; }

    /// <summary>The rectangle of the anchor box when all the scroll containers are at their initial scroll position.</summary>
    public PhysicalRect UnscrolledRect { get; }

    /// <summary>The scroll containers that are ancestors of the anchor up to (and excluding) the containing block of the positioned box, from the nearest one.</summary>
    public ContainerBox[] ScrollContainers { get; }

    /// <summary>Whether the scroll position of the viewport moves the anchor relative to the containing block (a fixed positioned box whose containing block is the viewport).</summary>
    public bool IncludesViewportScroll { get; }

    /// <summary>The key of the anchor element (its element and pseudo-element).</summary>
    public (Element Element, string? PseudoElement) Key => (Box.Element!, Box.PseudoElement);

    /// <summary>Returns the sum of the current scroll offsets of <see cref="ScrollContainers"/> (https://drafts.csswg.org/css-anchor-position-1/#remembered-scroll-offset).</summary>
    public PhysicalOffset GetScrollOffset(LayoutEngine engine)
    {
        var offset = IncludesViewportScroll ? engine.ViewportScrollState.Offset : PhysicalOffset.Zero;
        foreach (var container in ScrollContainers)
        {
            if (container.Element is { } element && engine.TryGetScrollState(element) is { } state)
            {
                offset += state.Offset;
            }
        }

        // The sticky offsets and the default scroll shifts of the anchor and of its ancestors are scroll-dependent positioning changes
        // ("The remembered scroll offset also accounts for other scroll-dependent positioning changes, such as position: sticky")
        for (Fragment? fragment = Box.LastLayoutFragment; fragment is not null && !ReferenceEquals(fragment, ContainingBlock); fragment = fragment.Parent)
        {
            if (fragment is BoxFragment boxFragment)
            {
                offset -= boxFragment.AnchorScrollShift + boxFragment.StickyOffset;
            }
        }

        return offset;
    }

    /// <summary>Returns the nearest scroll container ancestor of the anchor whose axis is scrollable (https://drafts.csswg.org/css-anchor-position-1/#compensate-for-scroll), or null.</summary>
    public ContainerBox? GetNearestScrollContainer(bool horizontal)
    {
        foreach (var container in ScrollContainers)
        {
            var (overflowX, overflowY) = Layout.ScrollContainers.GetUsedOverflow(container);
            if (Layout.ScrollContainers.IsScrollable(horizontal ? overflowX : overflowY))
                return container;
        }

        return null;
    }
}

/// <summary>
/// Finds the anchor elements of an absolutely positioned box while its containing block lays it out (https://drafts.csswg.org/css-anchor-position-1/#target):
/// the target anchor element of an anchor specifier (the acceptable anchor elements with the name, the nearest ancestor first, else
/// the last one in tree order), the default anchor element, and their anchor boxes.
/// </summary>
internal sealed class AnchorQuery
{
    private readonly LayoutContext _context;
    private readonly OutOfFlowCandidate _candidate;
    private readonly BoxFragment _hostFragment;
    private readonly List<BoxFragment>? _laidOut;
    private readonly bool _includesViewportScroll;
    private Dictionary<ContainerBox, AnchorBox?>? _anchorBoxes;

    /// <summary>Whether the anchor box being computed is in a fixed positioned box of the viewport.</summary>
    private bool _inFixedBox;

    public AnchorQuery(LayoutContext context, OutOfFlowCandidate candidate, BoxFragment hostFragment, List<BoxFragment>? laidOut)
    {
        _context = context;
        _candidate = candidate;
        _hostFragment = hostFragment;
        _laidOut = laidOut;

        // The fixed positioned boxes of the viewport do not scroll with the document: the scroll position of the viewport moves
        // their anchors relative to them
        _includesViewportScroll = candidate.ContainingBlock.Kind == ContainerBoxKind.Viewport && candidate.Box.Style.Layout.Position == PositionKind.Fixed;
    }

    public LayoutContext Context => _context;

    /// <summary>The candidate of the absolutely positioned box (its containing block).</summary>
    public OutOfFlowCandidate Candidate => _candidate;

    /// <summary>The absolutely positioned box.</summary>
    public ContainerBox Box => _candidate.Box;

    // ----------------------------------------------------------------------------------------------------------------
    // Target anchor element
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the default anchor element of the box (https://drafts.csswg.org/css-anchor-position-1/#default-anchor-element) for a
    /// style of the box (its computed style, or the style of a position option), or null.
    /// </summary>
    public ContainerBox? GetDefaultAnchor(ComputedStyle style)
    {
        var anchor = style.Layout.Anchor;
        var kind = anchor.PositionAnchor;

        // "normal: If position-area is none, behaves as none. Otherwise, behaves as auto."
        if (kind == PositionAnchorKind.Normal)
        {
            kind = anchor.PositionArea is null ? PositionAnchorKind.None : PositionAnchorKind.Auto;
        }

        switch (kind)
        {
            case PositionAnchorKind.Auto:
                return GetImplicitAnchor();
            case PositionAnchorKind.Name:
                return FindNamedAnchor(anchor.PositionAnchorName!.Value);
            case PositionAnchorKind.MatchParent:
            {
                // "Uses the same default anchor element as the parent (or originating element, if this is a pseudo-element), if any,
                // and if that would be an acceptable anchor element"
                if (GetParentElement() is not { } parent || _context.Engine.BoxTree?.GetPrincipalBox(parent) is not { } parentBox)
                    return null;

                var parentAnchor = parentBox.Style.Layout.Anchor;
                var parentKind = parentAnchor.PositionAnchor == PositionAnchorKind.Normal ? (parentAnchor.PositionArea is null ? PositionAnchorKind.None : PositionAnchorKind.Auto) : parentAnchor.PositionAnchor;
                return parentKind switch
                {
                    PositionAnchorKind.Name => FindNamedAnchor(parentAnchor.PositionAnchorName!.Value),
                    PositionAnchorKind.Auto => GetImplicitAnchor(parent, pseudoElement: null),
                    _ => null,
                };
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns the implicit anchor element of the box (https://drafts.csswg.org/css-anchor-position-1/#implicit): the popover trigger
    /// of a showing popover (https://html.spec.whatwg.org/multipage/popover.html#show-popover), the originating element of a
    /// pseudo-element; null when it is not an acceptable anchor element.
    /// </summary>
    private ContainerBox? GetImplicitAnchor() => Box.Element is { } element ? GetImplicitAnchor(element, Box.PseudoElement) : null;

    private ContainerBox? GetImplicitAnchor(Element element, string? pseudoElement)
    {
        var implicitAnchor = pseudoElement is not null ? element : (element as HtmlElement)?.ImplicitAnchorElement;
        if (implicitAnchor is null || _context.Engine.BoxTree?.GetPrincipalBox(implicitAnchor) is not { } box)
            return null;

        return IsAcceptableAnchor(box) && GetAnchorBox(box) is not null ? box : null;
    }

    /// <summary>
    /// Returns the target anchor element of an &lt;anchor-name&gt; (https://drafts.csswg.org/css-anchor-position-1/#target): "If an
    /// ancestor of query el satisfies the following conditions, return the nearest such element to query el. Otherwise, return the
    /// last element el in tree order that satisfies the conditions": an anchor element with the name (loosely matched), and an
    /// acceptable anchor element.
    /// </summary>
    public ContainerBox? FindNamedAnchor(TreeScopedName name)
    {
        var candidates = GetAnchorNameIndex().GetValueOrDefault(name.Name);
        if (candidates is null)
            return null;

        // The ancestors (in the flat tree) first
        for (var ancestor = GetParentElement(); ancestor is not null; ancestor = HtmlElement.GetFlatTreeParent(ancestor) as Element)
        {
            foreach (var candidate in candidates)
            {
                if (ReferenceEquals(candidate.Element, ancestor) && candidate.PseudoElement is null && IsTarget(candidate, name))
                    return candidate;
            }
        }

        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            if (IsTarget(candidates[i], name))
                return candidates[i];
        }

        return null;
    }

    private bool IsTarget(ContainerBox candidate, TreeScopedName name)
        => HasName(candidate, name) && IsInScope(candidate, name.Name) && IsLookupInScope(candidate, name) && IsAcceptableAnchor(candidate) && GetAnchorBox(candidate) is not null;

    /// <summary>Returns the parent element of the box in the flat tree, or the originating element of a pseudo-element.</summary>
    private Element? GetParentElement() => Box.Element is { } element ? Box.PseudoElement is not null ? element : HtmlElement.GetFlatTreeParent(element) as Element : null;

    /// <summary>Returns whether a box has an anchor name that loosely matches a reference (https://drafts.csswg.org/css-scoping-1/#css-tree-scoped-name).</summary>
    private static bool HasName(ContainerBox box, TreeScopedName reference)
    {
        if (box.Style.Layout.Anchor.AnchorNames is not { } names)
            return false;

        foreach (var name in names)
        {
            if (string.Equals(name.Name, reference.Name, StringComparison.Ordinal) && IsInclusiveAncestorTree(name.Scope, reference.Scope))
                return true;
        }

        return false;
    }

    /// <summary>Returns whether a tree (a document or a shadow root) is a shadow-including inclusive ancestor of another tree.</summary>
    private static bool IsInclusiveAncestorTree(Node ancestor, Node tree)
    {
        for (Node? current = tree; current is not null; current = current is ShadowRoot shadowRoot ? shadowRoot.Host.GetRoot() : null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether an anchor element is in scope for the box (https://drafts.csswg.org/css-anchor-position-1/#anchor-scope): the
    /// nearest inclusive ancestor of the anchor whose <c>anchor-scope</c> scopes the name limits the name to its descendants.
    /// </summary>
    private bool IsInScope(ContainerBox anchor, string name)
    {
        if (anchor.Element is not { } element)
            return false;

        var styleEngine = _context.Document.GetStyleEngine();
        for (Element? current = element; current is not null; current = HtmlElement.GetFlatTreeParent(current) as Element)
        {
            var style = ReferenceEquals(current, element) ? anchor.Style : styleEngine.GetStyle(current);
            if (!style.MayHaveCascadedValue("anchor-scope"))
                continue;

            var anchorStyle = style.Layout.Anchor;
            if (!anchorStyle.AnchorScopeAll && (anchorStyle.AnchorScope is not { } scoped || Array.IndexOf(scoped, name) < 0))
                continue;

            // The names are in scope for the descendants of the element (and of its pseudo-elements) in the flat tree
            return Box.Element is { } positioned && (ReferenceEquals(positioned, current) ? Box.PseudoElement is not null : IsFlatTreeAncestor(current, positioned));
        }

        return true;
    }

    /// <summary>
    /// Returns whether the lookup of a name by the box can match an anchor element (https://drafts.csswg.org/css-anchor-position-1/#anchor-scope:
    /// <c>anchor-scope</c> "limits descendants to only match these anchor names to anchor elements within this subtree"): the nearest
    /// ancestor of the box (in the flat tree, the originating element of a pseudo-element) whose <c>anchor-scope</c> scopes the name
    /// must contain the anchor element. The scoped names are strictly matched tree-scoped names: they only scope the names
    /// referenced from the same tree (e.g. an <c>anchor-scope</c> declared in a <c>::part()</c> rule scopes the names that the styles
    /// of the outer tree reference, also from the slotted elements).
    /// </summary>
    private bool IsLookupInScope(ContainerBox anchor, TreeScopedName name)
    {
        var styleEngine = _context.Document.GetStyleEngine();
        for (var current = GetParentElement(); current is not null; current = HtmlElement.GetFlatTreeParent(current) as Element)
        {
            var style = styleEngine.GetStyle(current);
            if (!style.MayHaveCascadedValue("anchor-scope"))
                continue;

            var anchorStyle = style.Layout.Anchor;
            if ((!anchorStyle.AnchorScopeAll && (anchorStyle.AnchorScope is not { } scoped || Array.IndexOf(scoped, name.Name) < 0)) || !ReferenceEquals(anchorStyle.AnchorScopeTree, name.Scope))
                continue;

            return anchor.Element is { } element && (ReferenceEquals(element, current) || IsFlatTreeAncestor(current, element));
        }

        return true;
    }

    private static bool IsFlatTreeAncestor(Element ancestor, Element element)
    {
        for (var current = HtmlElement.GetFlatTreeParent(element); current is not null; current = HtmlElement.GetFlatTreeParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether an element is an acceptable anchor element for the box (https://drafts.csswg.org/css-anchor-position-1/#acceptable-anchor-element):
    /// it is laid out strictly before the box. Either it and the box have the same original containing block and it is in a lower top
    /// layer, or in the same top layer and not absolutely positioned or earlier in the tree order (as the boxes of a containing block are
    /// laid out in that order, these are the boxes already laid out by the containing block); or the element generating its containing
    /// block is an acceptable anchor element.
    /// </summary>
    public bool IsAcceptableAnchor(ContainerBox anchor)
    {
        var positioned = Box;
        var containingBlock = _candidate.ContainingBlock;
        var current = anchor;
        while (true)
        {
            // (a column spanner is laid out by its multi-column container, https://drafts.csswg.org/css-multicol-1/#column-span)
            ContainerBox next;
            for (var box = current; ; box = MultiColumnFlow.GetColumnSpannerContainer(box) ?? PositionedLayout.GetPositioningParent(box))
            {
                if (box is null || ReferenceEquals(box, positioned))
                    return false;

                // An in-flow descendant of the containing block is laid out before its absolutely positioned boxes
                if (!ReferenceEquals(box, current) && ReferenceEquals(box, containingBlock))
                    return true;

                if (box.Kind == ContainerBoxKind.Viewport)
                    return containingBlock.Kind == ContainerBoxKind.Viewport;

                if (box.Kind != ContainerBoxKind.TableGrid && PositionedLayout.IsAbsolutelyPositioned(box))
                {
                    var boxContainingBlock = PositionedLayout.FindContainingBlock(box);
                    if (ReferenceEquals(boxContainingBlock, containingBlock))
                        return IsLaidOut(box);

                    next = boxContainingBlock;
                    break;
                }
            }

            if (next.Kind == ContainerBoxKind.Viewport)
                return false;

            current = next;
        }
    }

    /// <summary>Returns whether an absolutely positioned box with the same containing block as the box was laid out before it.</summary>
    private bool IsLaidOut(ContainerBox box)
    {
        if (_laidOut is null)
            return false;

        foreach (var fragment in _laidOut)
        {
            if (ReferenceEquals(fragment.Box, box))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the anchor elements by anchor name, in tree order (the principal boxes whose element has <c>anchor-name</c>), built once
    /// per layout pass.
    /// </summary>
    private Dictionary<string, List<ContainerBox>> GetAnchorNameIndex()
    {
        if (_context.AnchorNameIndex is { } index)
            return index;

        index = new Dictionary<string, List<ContainerBox>>(StringComparer.Ordinal);
        if (_context.Engine.BoxTree?.Viewport is { } viewport)
        {
            var stack = new Stack<ContainerBox>();
            stack.Push(viewport);
            while (stack.TryPop(out var box))
            {
                if (box.Element is not null && box.Kind != ContainerBoxKind.TableGrid && box.PreviousContinuation is null && box.Style.Layout.MayHaveAnchorName
                    && box.Style.Layout.Anchor.AnchorNames is { } names)
                {
                    foreach (var name in names)
                    {
                        if (!index.TryGetValue(name.Name, out var list))
                        {
                            list = [];
                            index.Add(name.Name, list);
                        }

                        if (list.Count == 0 || !ReferenceEquals(list[^1], box))
                        {
                            list.Add(box);
                        }
                    }
                }

                for (var child = box.LastChild; child is not null; child = child.PreviousSibling)
                {
                    if (child is ContainerBox container)
                    {
                        stack.Push(container);
                    }
                }
            }
        }

        _context.AnchorNameIndex = index;
        return index;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Anchor boxes
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the anchor box of an anchor element (https://drafts.csswg.org/css-anchor-position-1/#determining), or null when the
    /// element has no fragment in the containing block of the box (it is not laid out, e.g. in skipped contents).
    /// </summary>
    public AnchorBox? GetAnchorBox(ContainerBox anchor)
    {
        _anchorBoxes ??= new(ReferenceEqualityComparer.Instance);
        if (_anchorBoxes.TryGetValue(anchor, out var result))
            return result;

        result = ComputeAnchorBox(anchor);
        _anchorBoxes.Add(anchor, result);
        return result;
    }

    private AnchorBox? ComputeAnchorBox(ContainerBox anchor)
    {
        _inFixedBox = false;
        PhysicalRect? rect = null;
        List<ContainerBox>? scrollContainers = null;

        // The fragments of an inline box are in the fragment of its block container (the parent of the anonymous block boxes of
        // its parts when it is split around block-level boxes)
        var laidOutBox = anchor;
        var isSplitInline = anchor is { IsInlineBox: true, Continuation: not null };
        if (isSplitInline)
        {
            laidOutBox = PositionedLayout.GetHost(anchor);
        }
        else
        {
            while (laidOutBox.IsInlineBox && laidOutBox.Parent is { } parent)
            {
                laidOutBox = parent;
            }
        }

        var scrolled = PhysicalOffset.Zero;
        BoxFragment? measuredFragment = null;
        var rectInFragment = default(PhysicalRect);
        var measuredInScrolledContents = false;
        if (laidOutBox.LastLayoutFragment is { } fragment && fragment.LayoutPass == _context.Pass && TryMapToHost(fragment, fragment.LocalRect, inScrolledContents: false, ref scrollContainers, out scrolled) is { } mapped)
        {
            if (ReferenceEquals(laidOutBox, anchor) || !anchor.IsInlineBox)
            {
                rect = mapped;
                measuredFragment = fragment;
                rectInFragment = fragment.LocalRect;
            }
            else if ((isSplitInline ? FindSplitInlineFragments(fragment, anchor) : FindFragments(fragment, anchor)) is { } inlineRect)
            {
                // The rectangle of the fragments of the inline box in the coordinates of the block container (a scroll container
                // scrolls them)
                scrollContainers = null;
                var inScrolledContents = fragment.IsScrollContainer && !ReferenceEquals(fragment, _hostFragment);
                if (inScrolledContents)
                {
                    scrollContainers = [laidOutBox];
                }

                rect = TryMapToHost(fragment, inlineRect, inScrolledContents, ref scrollContainers, out scrolled);
                measuredFragment = fragment;
                rectInFragment = inlineRect;
                measuredInScrolledContents = inScrolledContents;
            }
        }

        if (rect is null)
        {
            // The fragments of the box are not the ones it was laid out in (e.g. the slices of a fragmented box): search the
            // fragment tree of the containing block
            scrollContainers = null;
            scrolled = PhysicalOffset.Zero;
            measuredFragment = null;
            rect = isSplitInline && ReferenceEquals(_hostFragment.Box, laidOutBox)
                ? FindSplitInlineFragments(_hostFragment, anchor)
                : FindFragments(_hostFragment, anchor, includeSelf: false, offset: PhysicalOffset.Zero, ref scrollContainers);
            if (_laidOut is not null)
            {
                foreach (var laidOut in _laidOut)
                {
                    if (FindFragments(laidOut, anchor, includeSelf: true, offset: laidOut.Offset, ref scrollContainers) is { } laidOutRect)
                    {
                        rect = rect is { } existing ? existing.Union(laidOutRect) : laidOutRect;
                    }
                }
            }
        }

        if (rect is not { } anchorRect)
            return null;

        var inFixedBox = _inFixedBox;
        _inFixedBox = false;

        // The rectangle at the scroll positions 0 (the scroll offsets were subtracted inside the transforms of the scroll containers)
        return new AnchorBox(anchor, anchorRect.Translate(scrolled), scrollContainers is null ? [] : [.. scrollContainers], _includesViewportScroll && !inFixedBox, _hostFragment)
        {
            Fragment = measuredFragment,
            RectInFragment = rectInFragment,
            InScrolledContents = measuredInScrolledContents,
        };
    }

    /// <summary>
    /// Maps a rectangle in the coordinates of a fragment to the coordinates of the fragment of the host (transforms and scroll offsets
    /// included), and collects the scroll containers on the way; returns null when the fragment is not in the fragment tree of the host.
    /// </summary>
    /// <param name="fragment">The fragment.</param>
    /// <param name="rect">The rectangle, in the coordinates of the fragment.</param>
    /// <param name="inScrolledContents">Whether the rectangle is in the scrolled contents of the fragment (a scroll container).</param>
    /// <param name="scrollContainers">The scroll containers, from the nearest one.</param>
    /// <param name="scrolled">The sum of the scroll offsets subtracted from the rectangle.</param>
    private PhysicalRect? TryMapToHost(BoxFragment fragment, PhysicalRect rect, bool inScrolledContents, ref List<ContainerBox>? scrollContainers, out PhysicalOffset scrolled)
    {
        Fragment current = fragment;
        List<ContainerBox>? containers = null;
        scrolled = PhysicalOffset.Zero;
        if (inScrolledContents)
        {
            var offset = GetScrollOffset(fragment.Box);
            rect = rect.Translate(-offset);
            scrolled += offset;
        }

        // The transforms of the boxes of a 3D rendering context are accumulated (https://drafts.csswg.org/css-transforms-2/#accumulated-3d-transformation-matrix-computation),
        // the rectangle is flattened when it leaves the context
        double[]? accumulated = null;
        while (!ReferenceEquals(current, _hostFragment))
        {
            if (current is BoxFragment boxFragment && (accumulated is not null || boxFragment.Box.Style.Layout.HasTransform) && LayoutTransforms.GetMatrix(boxFragment) is var matrix && (matrix is not null || accumulated is not null))
            {
                if (LayoutTransforms.ParticipatesIn3DRenderingContext(boxFragment) && current.Parent is { } contextParent && !ReferenceEquals(contextParent, _hostFragment))
                {
                    // (the offset is part of the accumulated matrix)
                    var local = matrix is null ? accumulated! : accumulated is null ? matrix : CssTransforms.Multiply(matrix, accumulated);
                    accumulated = CssTransforms.Multiply([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, current.Offset.X, current.Offset.Y, 0, 1], local);
                    current = current.Parent;
                    continue;
                }

                rect = LayoutTransforms.TransformRect(rect, matrix is null ? accumulated! : accumulated is null ? matrix : CssTransforms.Multiply(matrix, accumulated));
                accumulated = null;
            }

            rect = rect.Translate(current.Offset);
            if (current.Parent is not { } parent)
            {
                // A box laid out before by the same containing block (not yet a child of its fragment)
                if (current is not BoxFragment laidOut || _laidOut is null || !_laidOut.Contains(laidOut))
                    return null;

                if (laidOut.Box.Style.Layout.Position == PositionKind.Fixed && _hostFragment.Box.Kind == ContainerBoxKind.Viewport)
                {
                    _inFixedBox = true;
                }

                break;
            }

            if (!ReferenceEquals(parent, _hostFragment) && parent is BoxFragment { IsScrollContainer: true } scrollContainer)
            {
                (containers ??= []).Add(scrollContainer.Box);
                var offset = GetScrollOffset(scrollContainer.Box);
                rect = rect.Translate(-offset);
                scrolled += offset;
            }

            // A fixed positioned box of the viewport does not scroll with the document: neither does its contents
            if (ReferenceEquals(parent, _hostFragment) && current is BoxFragment { Box.Style.Layout.Position: PositionKind.Fixed } && _hostFragment.Box.Kind == ContainerBoxKind.Viewport)
            {
                _inFixedBox = true;
            }

            current = parent;
        }

        if (containers is not null)
        {
            (scrollContainers ??= []).AddRange(containers);
        }

        return rect;
    }

    /// <summary>Returns the current scroll offset of a scroll container.</summary>
    private PhysicalOffset GetScrollOffset(ContainerBox scrollContainer)
        => scrollContainer.Element is { } element && _context.Engine.TryGetScrollState(element) is { } state ? state.Offset : PhysicalOffset.Zero;

    /// <summary>
    /// Returns the bounding rectangle of an inline box split around block-level boxes (https://drafts.csswg.org/css2/#anonymous-block-level)
    /// in the descendants of the fragment of its block container, in its coordinates: its parts, and the block-level boxes split out of
    /// it, each one in the anonymous block box that holds it in the inline box, as wide as the content box of the block container (as in
    /// the browsers, the inline box spans these lines).
    /// </summary>
    private static PhysicalRect? FindSplitInlineFragments(BoxFragment container, ContainerBox inline)
    {
        var parts = new HashSet<ContainerBox>(ReferenceEqualityComparer.Instance);
        for (var part = inline; part is not null; part = part.Continuation)
        {
            parts.Add(part);
        }

        var content = container.ContentBoxRect;
        var isHorizontal = container.WritingDirection.IsHorizontal;
        PhysicalRect? result = null;
        Collect(container, PhysicalOffset.Zero);
        return result;

        void Collect(Fragment fragment, PhysicalOffset offset)
        {
            foreach (var child in fragment.ChildrenSpan)
            {
                var childOffset = offset + child.Offset;
                if (child is BoxFragment childBox)
                {
                    if (parts.Contains(childBox.Box))
                    {
                        Add(new PhysicalRect(childOffset, child.Size));
                        continue;
                    }

                    if (IsSplitOutOf(childBox.Box))
                    {
                        var margin = childBox.MarginBoxRect.Translate(childOffset);
                        Add(isHorizontal ? new PhysicalRect(content.X, margin.Y, content.Width, margin.Height) : new PhysicalRect(margin.X, content.Y, margin.Width, content.Height));
                        continue;
                    }

                    if (childBox.IsScrollContainer || childBox.Box.IsOutOfFlow)
                        continue;
                }

                if (child.ChildrenSpan.Length > 0)
                {
                    Collect(child, childOffset);
                }
            }
        }

        void Add(PhysicalRect rect) => result = result is { } existing ? existing.Union(rect) : rect;

        // An in-flow block-level box split out of the inline box
        bool IsSplitOutOf(ContainerBox box)
        {
            if (!box.IsBlockLevel || box.IsOutOfFlow)
                return false;

            for (var parent = box.SplitInlineParent; parent is not null; parent = parent.Parent is { IsInlineBox: true } inlineParent ? inlineParent : null)
            {
                if (parts.Contains(parent))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Returns the bounding rectangle of the fragments of a box (and of its continuations) in the descendants of a fragment, in its coordinates.</summary>
    private static PhysicalRect? FindFragments(Fragment root, ContainerBox box)
    {
        List<ContainerBox>? ignored = null;
        return FindFragments(root, box, includeSelf: false, PhysicalOffset.Zero, ref ignored);
    }

    private static PhysicalRect? FindFragments(Fragment fragment, ContainerBox box, bool includeSelf, PhysicalOffset offset, ref List<ContainerBox>? scrollContainers)
    {
        PhysicalRect? result = null;
        if (includeSelf && fragment is BoxFragment self && IsFragmentOf(self, box))
            return GetTransformedRect(self).Translate(offset);

        foreach (var child in fragment.ChildrenSpan)
        {
            var childOffset = offset + child.Offset;
            if (child is BoxFragment childBox && IsFragmentOf(childBox, box))
            {
                // (each fragment of a box fragmented by a multi-column container is transformed with its own reference box)
                var childRect = GetTransformedRect(childBox).Translate(childOffset);
                result = result is { } existing ? existing.Union(childRect) : childRect;
                continue;
            }

            if (child.ChildrenSpan.Length > 0 && FindFragments(child, box, includeSelf: false, childOffset, ref scrollContainers) is { } nested)
            {
                result = result is { } existing ? existing.Union(nested) : nested;
                if (child is BoxFragment { IsScrollContainer: true } scrollContainer)
                {
                    (scrollContainers ??= []).Add(scrollContainer.Box);
                }
            }
        }

        return result;

        static bool IsFragmentOf(BoxFragment fragment, ContainerBox box)
        {
            for (var part = box; part is not null; part = part.Continuation)
            {
                if (ReferenceEquals(fragment.Box, part))
                    return true;
            }

            return false;
        }

        static PhysicalRect GetTransformedRect(BoxFragment fragment)
            => fragment.Box.Style.Layout.HasTransform && LayoutTransforms.GetMatrix(fragment) is { } matrix ? LayoutTransforms.TransformRect(fragment.LocalRect, matrix) : fragment.LocalRect;
    }
}
