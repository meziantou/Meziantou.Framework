using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A scroll marker (https://drafts.csswg.org/css-overflow-5/#scroll-marker): the <c>::scroll-marker</c> pseudo-element of an
/// element (<see cref="PseudoElement"/> is <see cref="ScrollControls.Marker"/>), or a link of a scroll marker group container
/// (<see cref="PseudoElement"/> is null).
/// </summary>
internal readonly record struct ScrollMarkerReference(Element Element, string? PseudoElement);

/// <summary>
/// A scroll marker group (https://drafts.csswg.org/css-overflow-5/#scroll-marker-grouping) and its state: the scroll markers
/// of the <c>::scroll-marker-group</c> of a scroll container, or the links of an element with <c>scroll-target-group: auto</c>.
/// </summary>
internal sealed class ScrollMarkerGroup(Element container, bool isPseudoElement)
{
    /// <summary>The scroll container whose <c>::scroll-marker-group</c> contains the markers, or the scroll marker group container element.</summary>
    public Element Container { get; } = container;

    /// <summary>Whether the group is a <c>::scroll-marker-group</c> pseudo-element.</summary>
    public bool IsPseudoElement { get; } = isPseudoElement;

    /// <summary>Whether the group operates in tabs mode (https://drafts.csswg.org/css-overflow-5/#scroll-marker-modes).</summary>
    public bool IsTabs { get; internal set; }

    /// <summary>The scroll markers of the group, in tree order.</summary>
    public List<ScrollMarkerReference> Markers { get; } = [];

    /// <summary>The active marker (https://drafts.csswg.org/css-overflow-5/#active-scroll-marker), which matches <c>:target-current</c>, or null.</summary>
    public ScrollMarkerReference? Active { get; internal set; }

    /// <summary>The snapshot that saw the group (the groups that are not in the last layout are forgotten).</summary>
    internal int SnapshotPass { get; set; }
}

// The state of the scroll navigation controls (https://drafts.csswg.org/css-overflow-5/#scroll-navigation): the active marker of
// each scroll marker group (https://drafts.csswg.org/css-overflow-5/#active-scroll-marker), which the :target-current,
// :target-before and :target-after pseudo-classes match, and whether each scroll button can scroll (:enabled and :disabled).
//
// The state depends on the layout and on the scroll positions, and the styles depend on it: as the states of the scroll-state
// query containers (see LayoutEngine.ScrollStateQueries.cs), it is a snapshot taken by the rendering steps after the styles and
// the layout are updated ("run snapshot post-layout state steps"); a change starts a new generation of styles. The groups and
// the buttons that a layout pass lays out for the first time are snapshotted at the end of that pass, so that a forced style
// update (getComputedStyle()) sees their initial state.
internal sealed partial class LayoutEngine
{
    private Dictionary<(Element Container, bool IsPseudoElement), ScrollMarkerGroup>? _scrollMarkerGroups;
    private Dictionary<ScrollMarkerReference, ScrollMarkerGroup>? _scrollMarkerGroupsByMarker;

    /// <summary>The scroll buttons of the last snapshot and whether they are enabled (their originating element can be scrolled in their direction).</summary>
    private Dictionary<(Element Element, string Button), bool>? _scrollButtonStates;
    private int _scrollMarkerSnapshotPass;

    /// <summary>Whether a selector matched the state of a scroll marker or of a scroll button: a change of the snapshot then changes the styles.</summary>
    private bool _scrollMarkerStateQueried;

    /// <summary>The number of changes of the snapshot that a style may depend on: the styles are computed again when it changes (see <see cref="StyleEngine"/>).</summary>
    internal int ScrollMarkerSnapshotVersion { get; private set; }

    /// <summary>Whether the document has scroll navigation controls whose state follows the scroll positions (the scrolls then request a rendering update).</summary>
    private bool HasScrollMarkerState => _scrollMarkerGroups is { Count: > 0 } || _scrollButtonStates is { Count: > 0 };

    /// <summary>
    /// Returns whether a scroll marker matches <c>:target-current</c>, <c>:target-before</c> or <c>:target-after</c>
    /// (https://drafts.csswg.org/css-overflow-5/#active-scroll-marker) in the last snapshot: it is the active marker of its
    /// group, or it precedes or follows the active marker in the group.
    /// </summary>
    internal bool MatchesScrollMarkerState(PseudoClass pseudoClass, Element element, string? pseudoElement)
    {
        OnScrollMarkerStateQueried();
        var marker = new ScrollMarkerReference(element, pseudoElement);
        if (_scrollMarkerGroupsByMarker is null || !_scrollMarkerGroupsByMarker.TryGetValue(marker, out var group) || group.Active is not { } active)
            return false;

        if (pseudoClass == PseudoClass.TargetCurrent)
            return active == marker;

        var index = group.Markers.IndexOf(marker);
        var activeIndex = group.Markers.IndexOf(active);
        if (index < 0 || activeIndex < 0)
            return false;

        return pseudoClass == PseudoClass.TargetBefore ? index < activeIndex : index > activeIndex;
    }

    /// <summary>
    /// Returns whether a scroll button is enabled in the last snapshot (https://drafts.csswg.org/css-overflow-5/#scroll-buttons:
    /// <c>:disabled</c> "matches a given button when their originating element can't be scrolled in their associated direction").
    /// </summary>
    internal bool IsScrollButtonEnabled(Element element, string button)
    {
        OnScrollMarkerStateQueried();
        return _scrollButtonStates is not null && _scrollButtonStates.TryGetValue((element, button), out var enabled) && enabled;
    }

    private void OnScrollMarkerStateQueried()
    {
        if (_scrollMarkerStateQueried)
            return;

        _scrollMarkerStateQueried = true;
        RequestRenderingUpdates();
    }

    /// <summary>Returns whether the <c>::scroll-marker</c> of an element is generated: it is in the <c>::scroll-marker-group</c> of a scroll container. The layout is computed when it is out of date.</summary>
    internal bool HasScrollMarker(Element element, string pseudoElement)
    {
        EnsureLayout();
        return pseudoElement == ScrollControls.Marker && _boxTree?.ScrollControls is { } controls && controls.MarkerScrollers.ContainsKey(new ScrollMarkerReference(element, pseudoElement));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The columns of the multi-column containers (https://drafts.csswg.org/css-multicol-2/#column-pseudo)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The number of columns of the last layout of the multi-column containers whose columns may have scroll markers.</summary>
    private Dictionary<Element, int>? _columnCounts;

    /// <summary>
    /// Returns the number of columns of a multi-column container in its last layout, for the <c>::column::scroll-marker</c>
    /// pseudo-elements of the next box tree; 0 before its first layout, or when no <c>::column::scroll-marker</c> rule may apply to it.
    /// </summary>
    internal int GetColumnCount(Element element) => _columnCounts is not null && _columnCounts.TryGetValue(element, out var count) ? count : 0;

    /// <summary>
    /// Records the number of columns of the multi-column containers whose columns may have scroll markers, after a layout pass.
    /// When a container does not have the number of columns its markers were generated for, the markers of the box tree are
    /// collected again: returns true, the document must be laid out again.
    /// </summary>
    private bool UpdateColumnScrollMarkers()
    {
        if (_boxTree?.ScrollControls is not { ColumnMarkerCounts.Count: > 0 } controls)
            return false;

        var changed = false;
        var counts = new Dictionary<Element, int>(ReferenceEqualityComparer.Instance);
        foreach (var (element, generated) in controls.ColumnMarkerCounts)
        {
            var count = GetColumnsFragment(element)?.ColumnRects?.Length ?? 0;
            counts[element] = count;
            changed |= count != generated;
        }

        _columnCounts = counts;
        if (!changed)
            return false;

        BoxTreeBuilder.RepopulateScrollMarkers(_boxTree);
        return true;
    }

    /// <summary>
    /// Returns the first fragment of the box of an element that lays out its columns: its principal box; the anonymous fieldset
    /// content box of a fieldset.
    /// </summary>
    private BoxFragment? GetColumnsFragment(Element element)
    {
        if (GetLaidOutFragments(element) is not [BoxFragment fragment, ..])
            return null;

        if (BlockFormattingContext.IsFieldset(fragment.Box))
        {
            foreach (var child in fragment.ChildrenSpan)
            {
                if (child is BoxFragment { Box.IsFieldsetContent: true } content)
                    return content;
            }
        }

        return fragment;
    }

    /// <summary>Returns the box of a column of a multi-column container in its last layout, in the coordinates of the document, or null.</summary>
    internal PhysicalRect? GetColumnRect(Element element, int index)
    {
        if (GetColumnsFragment(element) is not { } fragment || fragment.ColumnRects is not { } rects || index < 0 || index >= rects.Length)
            return null;

        // The columns are contents of the container: they move with its scroll position when it is a scroll container
        var rect = fragment.WritingDirection.ToPhysical(rects[index], fragment.Size);
        if (fragment.ScrollState is { } scrollState)
        {
            rect = rect.Translate(-scrollState.Offset);
        }

        return MapToRoot(fragment, rect).Bounds;
    }

    /// <summary>
    /// Returns the index of the column of a multi-column container in which a descendant element starts (the column box that
    /// contains the start corner of its first fragment), or null when the element has no box or is not in a column.
    /// </summary>
    private int? GetColumnOf(Element container, Element element)
    {
        if (GetColumnsFragment(container) is not { } fragment || fragment.ColumnRects is not { } rects || GetLaidOutFragments(element) is not [var first, ..])
            return null;

        const double Epsilon = 0.01;
        var corner = first.GetRectInRoot();
        for (var i = 0; i < rects.Length; i++)
        {
            if (GetColumnRect(container, i) is { } column && corner.X >= column.X - Epsilon && corner.X < column.Right - Epsilon && corner.Y >= column.Y - Epsilon && corner.Y < column.Bottom - Epsilon)
                return i;
        }

        return null;
    }

    /// <summary>
    /// Returns the first element, in tree order, of the contents of a multi-column container that starts in a column or in one of
    /// the columns after it: where the sequential focus navigation continues after the activation of the scroll marker of the
    /// column (as in Chrome: the first focusable area of the column is the next one).
    /// </summary>
    internal Element? GetFirstElementOfColumn(Element container, int index)
    {
        for (var node = container.FirstChild; node is not null; node = node.GetNextInTreeOrder(container))
        {
            if (node is Element element && GetColumnOf(container, element) is { } column && column >= index)
                return element;
        }

        return null;
    }

    /// <summary>Returns the group of a scroll marker in the last snapshot, or null.</summary>
    internal ScrollMarkerGroup? GetScrollMarkerGroup(ScrollMarkerReference marker)
        => _scrollMarkerGroupsByMarker is not null && _scrollMarkerGroupsByMarker.TryGetValue(marker, out var group) ? group : null;

    /// <summary>Returns the group of the <c>::scroll-marker-group</c> of a scroll container in the last snapshot, or null.</summary>
    internal ScrollMarkerGroup? GetScrollMarkerGroup(Element scroller)
        => _scrollMarkerGroups is not null && _scrollMarkerGroups.TryGetValue((scroller, true), out var group) ? group : null;

    /// <summary>
    /// Sets the current scroll target of a scroll container (https://drafts.csswg.org/css-overflow-5/#current-scroll-target),
    /// which selects the active marker of the groups whose targets it scrolls: they are updated by the next rendering update.
    /// </summary>
    internal void SetCurrentScrollTarget(ScrollState state, Element? target, int? column = null)
    {
        // (every scroll operation sets it: the box is not scrolled to its initial scroll target anymore)
        state.IsScrolledByOperation = true;
        if (ReferenceEquals(state.CurrentScrollTarget, target) && state.CurrentScrollTargetColumn == column)
            return;

        state.CurrentScrollTarget = target;
        state.CurrentScrollTargetColumn = column;
        if (HasScrollMarkerState)
        {
            _document.EventLoop?.RequestRenderingUpdate();
        }
    }

    /// <summary>Requests the rendering update that takes the next snapshot (the active marker of a group changed without scroll).</summary>
    internal void RequestScrollMarkerSnapshot() => RequestRenderingUpdates();

    /// <summary>
    /// Updates the state of the scroll navigation controls from the layout and the scroll positions: the "run snapshot
    /// post-layout state steps" of the rendering steps (https://drafts.csswg.org/cssom-view/#post-layout-snapshot), or, when
    /// <paramref name="onlyNew"/> is true, the end of a layout pass: only the groups and the buttons that were not in the
    /// previous snapshot get their state (the markers of the other groups are updated, not their active marker). Returns
    /// whether a state that the styles may depend on changed: the styles and the layout must then be updated again.
    /// </summary>
    internal bool UpdateScrollMarkerSnapshots(bool onlyNew)
    {
        var controls = _boxTree?.ScrollControls;
        if (controls is null && _scrollMarkerGroups is null && _scrollButtonStates is null)
            return false;

        var changed = false;
        var pass = ++_scrollMarkerSnapshotPass;
        if (controls is not null)
        {
            var markers = new List<ScrollMarkerReference>();
            foreach (var group in controls.Groups.Values)
            {
                changed |= UpdateScrollMarkerGroup(group.Scroller, isPseudoElement: true, group.IsTabs, group.Markers, pass, onlyNew);
            }

            // https://drafts.csswg.org/css-overflow-5/#scroll-target-group: the scroll marker group containers
            foreach (var container in controls.TargetGroupContainers)
            {
                if (!IsScrollTargetGroupContainer(container))
                    continue;

                markers.Clear();
                CollectScrollMarkerLinks(container, markers);
                changed |= UpdateScrollMarkerGroup(container, isPseudoElement: false, isTabs: false, markers, pass, onlyNew);
            }
        }

        // The groups that are not in the layout anymore
        if (_scrollMarkerGroups is { Count: > 0 } groups)
        {
            List<(Element Container, bool IsPseudoElement)>? removed = null;
            foreach (var (key, group) in groups)
            {
                if (group.SnapshotPass != pass)
                {
                    (removed ??= []).Add(key);
                    changed |= group.Active is not null;
                }
            }

            if (removed is not null)
            {
                foreach (var key in removed)
                {
                    groups.Remove(key);
                }
            }

            var byMarker = _scrollMarkerGroupsByMarker ??= [];
            byMarker.Clear();
            foreach (var group in groups.Values)
            {
                foreach (var marker in group.Markers)
                {
                    byMarker[marker] = group;
                }
            }
        }
        else
        {
            _scrollMarkerGroupsByMarker?.Clear();
        }

        changed |= UpdateScrollButtonStates(controls, onlyNew);
        if (!changed || !_scrollMarkerStateQueried)
            return false;

        ScrollMarkerSnapshotVersion++;
        return true;
    }

    private bool UpdateScrollMarkerGroup(Element container, bool isPseudoElement, bool isTabs, List<ScrollMarkerReference> markers, int pass, bool onlyNew)
    {
        var groups = _scrollMarkerGroups ??= [];
        var isNew = !groups.TryGetValue((container, isPseudoElement), out var group);
        if (group is null)
        {
            group = new ScrollMarkerGroup(container, isPseudoElement);
            groups.Add((container, isPseudoElement), group);
        }

        group.SnapshotPass = pass;
        group.IsTabs = isTabs;
        var changed = false;
        if (!group.Markers.SequenceEqual(markers))
        {
            // :target-before and :target-after depend on the order of the markers
            changed = group.Active is not null;
            group.Markers.Clear();
            group.Markers.AddRange(markers);
        }

        var active = group.Active;
        if (!onlyNew || isNew)
        {
            active = ChooseActiveScrollMarker(group);
        }
        else if (active is { } previous && !group.Markers.Contains(previous))
        {
            active = null;
        }

        if (active == group.Active)
            return changed;

        // https://drafts.csswg.org/css-overflow-5/#active-scroll-marker: "If active marker was the last-focused element of the
        // group, focus active marker": the focus follows the active marker
        if (isPseudoElement && active is { PseudoElement: { } nextPseudoElement } next && group.Active is { } old && _document.FocusedScrollControl is { IsMarker: true } focused
            && ReferenceEquals(focused.Element, old.Element) && focused.PseudoElement == old.PseudoElement && ReferenceEquals(focused.Scroller, container))
        {
            _document.FocusedScrollControl = new ScrollControlReference(next.Element, nextPseudoElement, container);
        }

        group.Active = active;
        return true;
    }

    /// <summary>Whether an element is a scroll marker group container (https://drafts.csswg.org/css-overflow-5/#scroll-target-group): <c>scroll-target-group: auto</c>.</summary>
    private bool IsScrollTargetGroupContainer(Element element)
        => element.IsConnected && ReferenceEquals(element.NodeDocument, _document) && ComputedStyleDeclarations.IsRendered(element)
            && _document.GetStyleEngine().GetStyle(element).GetComputedValue("scroll-target-group") == "auto";

    /// <summary>
    /// Collects the scroll marker elements of a scroll marker group container, in tree order: the links whose scroll target is an
    /// element of the document, "for which this is the nearest ancestor scroll marker group container".
    /// </summary>
    private void CollectScrollMarkerLinks(Element container, List<ScrollMarkerReference> markers)
    {
        for (var node = container.FirstChild; node is not null; node = node.GetNextInTreeOrder(container))
        {
            if (node is not Element element || !IsScrollMarkerElement(element) || GetScrollTarget(element) is null)
                continue;

            var nearest = element.ParentElement;
            while (nearest is not null && !ReferenceEquals(nearest, container) && !IsScrollTargetGroupContainer(nearest))
            {
                nearest = nearest.ParentElement;
            }

            if (ReferenceEquals(nearest, container))
            {
                markers.Add(new ScrollMarkerReference(element, PseudoElement: null));
            }
        }
    }

    /// <summary>https://drafts.csswg.org/css-overflow-5/#scroll-marker: "The HTML a element and SVG a element are scroll markers, whose scroll target is the indicated part".</summary>
    private static bool IsScrollMarkerElement(Element element) => element.IsHtml(TagId.A) || element is SvgAElement;

    /// <summary>
    /// Returns the scroll target of a link (https://drafts.csswg.org/css-overflow-5/#scroll-target): the element indicated by
    /// the fragment of its URL, when the URL is the URL of the document with a fragment; null otherwise.
    /// </summary>
    internal Element? GetScrollTarget(Element link)
    {
        if (!link.HasAttributeWithLocalName("href") || HyperlinkElementUtils.GetUrl(link) is not { Fragment: { Length: > 0 } fragment } url)
            return null;

        return BrowsingContext.UrlEquals(url, _document.UrlRecord, excludeFragments: true) ? HtmlElementStates.GetIndicatedElement(_document, fragment) : null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The active marker (https://drafts.csswg.org/css-overflow-5/#active-scroll-marker)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A scroll target of a group: the element a marker scrolls to (or a column of the multi-column container, for a
    /// <c>::column::scroll-marker</c>), with its rectangle in the coordinates of the document, its <c>scroll-snap-align</c> and
    /// its ancestor scroll containers (the nearest first, the viewport last).
    /// </summary>
    private sealed record ScrollMarkerTarget(ScrollMarkerReference Marker, Element Element, int? Column, PhysicalRect Rect, ScrollSnapAlign Align, List<ScrollState> Scrollers);

    /// <summary>An item of the targets of a scroll container: a scroll target (<see cref="Child"/> is null), or a scroll container that contains scroll targets.</summary>
    private sealed record ScrollMarkerCandidate(Element Element, int? Column, PhysicalRect Rect, ScrollSnapAlign Align, ScrollMarkerTarget? Target, ScrollState? Child);

    /// <summary>
    /// Determines the active marker of a group ("Example algorithm to determine the active marker for a given scroll marker
    /// group"): from the nearest common ancestor scroll container of the scroll targets, the target (or the scroll container
    /// that contains targets) selected by the position of each scroll container, down to a scroll target.
    /// </summary>
    private ScrollMarkerReference? ChooseActiveScrollMarker(ScrollMarkerGroup group)
    {
        var targets = new List<ScrollMarkerTarget>();
        foreach (var marker in group.Markers)
        {
            if (ScrollControls.IsColumnMarker(marker.PseudoElement))
            {
                // The scroll target of a ::column::scroll-marker is its column (https://drafts.csswg.org/css-multicol-2/#column-pseudo),
                // aligned by the scroll-snap-align of ::column; the container scrolls its columns when it is a scroll container
                var column = ScrollControls.GetColumnIndex(marker.PseudoElement!);
                if (GetColumnRect(marker.Element, column) is { } columnRect)
                {
                    var columnAlign = _document.GetStyleEngine().GetStyle(marker.Element, ScrollControls.Column).Layout.Overflow.ScrollSnapAlign;
                    targets.Add(new ScrollMarkerTarget(marker, marker.Element, column, columnRect, columnAlign, GetScrollContainers(marker.Element, includeSelf: true)));
                }

                continue;
            }

            var element = marker.PseudoElement is null ? GetScrollTarget(marker.Element) : marker.Element;
            if (element is null || !element.IsConnected || GetLaidOutFragments(element) is not { Count: > 0 } fragments)
                continue;

            // "If multiple scroll marker elements are associated with active, set selected marker to be the marker that is
            // earliest in tree order among them"
            if (targets.Exists(target => ReferenceEquals(target.Element, element) && target.Column is null))
                continue;

            targets.Add(new ScrollMarkerTarget(marker, element, Column: null, ScrollIntoView.GetTargetRect(fragments), fragments[0].Box.Style.Layout.Overflow.ScrollSnapAlign, GetScrollContainers(element, includeSelf: false)));
        }

        if (targets.Count == 0)
            return null;

        // "Let scroller be the nearest common ancestor scroll container of all of the scroll marker elements in group"
        ScrollState? active = null;
        foreach (var scroller in targets[0].Scrollers)
        {
            if (targets.TrueForAll(target => target.Scrollers.Contains(scroller)))
            {
                active = scroller;
                break;
            }
        }

        var candidates = new List<ScrollMarkerCandidate>();
        while (active is not null)
        {
            // "Let targets be the set of the scroll target elements whose nearest ancestor scroll container is scroller and the
            // scroll container elements which contain scroll target elements targeted by the scroll marker group whose nearest
            // ancestor scroll container is scroller"
            candidates.Clear();
            foreach (var target in targets)
            {
                var index = target.Scrollers.IndexOf(active);
                if (index == 0)
                {
                    candidates.Add(new ScrollMarkerCandidate(target.Element, target.Column, target.Rect, target.Align, target, Child: null));
                }
                else if (index > 0)
                {
                    var child = target.Scrollers[index - 1];
                    if (child is { Element: { } childElement, Fragment: { } childFragment } && !candidates.Exists(candidate => ReferenceEquals(candidate.Child, child)))
                    {
                        candidates.Add(new ScrollMarkerCandidate(childElement, Column: null, ScrollIntoView.GetTargetRect([childFragment]), childFragment.Box.Style.Layout.Overflow.ScrollSnapAlign, Target: null, child));
                    }
                }
            }

            if (candidates.Count == 0 || ChooseScrollMarkerCandidate(active, candidates) is not { } chosen)
                return null;

            if (chosen.Target is { } selected)
                return selected.Marker;

            active = chosen.Child;
        }

        return null;
    }

    /// <summary>Returns the box fragments of the principal box of an element in the last layout, without computing the layout.</summary>
    private List<BoxFragment>? GetLaidOutFragments(Element element)
    {
        List<BoxFragment>? result = null;
        for (var part = _boxTree?.GetPrincipalBox(element); part is not null; part = part.Continuation)
        {
            for (var fragment = GetFirstFragment(part); fragment is not null; fragment = fragment.NextFragmentOfBox)
            {
                if (fragment is BoxFragment boxFragment)
                {
                    (result ??= []).Add(boxFragment);
                }
            }
        }

        return result;
    }

    /// <summary>The scrolling boxes of the ancestor scroll containers of an element in the flat tree, from the nearest; the last one is the viewport.</summary>
    private List<ScrollState> GetScrollContainers(Element element, bool includeSelf)
    {
        var result = new List<ScrollState>();
        for (var ancestor = includeSelf ? element : StyleEngine.GetInheritanceParent(element); ancestor is not null; ancestor = StyleEngine.GetInheritanceParent(ancestor))
        {
            if (TryGetScrollState(ancestor) is { Fragment: not null } state)
            {
                result.Add(state);
            }
        }

        result.Add(ViewportScrollState);
        return result;
    }

    private ScrollMarkerCandidate? ChooseScrollMarkerCandidate(ScrollState scroller, List<ScrollMarkerCandidate> candidates)
    {
        if (scroller.Fragment is not { } fragment)
            return null;

        // "If scroller has a non-null current scroll target: let active be the first item in targets encountered by a reverse
        // tree order walk starting from the current scroll target, or the first item in tree order in targets if no target is
        // found in the previous walk"
        if (scroller.CurrentScrollTarget is { IsConnected: true } scrollTarget && ReferenceEquals(scrollTarget.NodeDocument, _document))
        {
            // The column of a multi-column container that was scrolled into view, or the column in which the target starts
            if (scroller.CurrentScrollTargetColumn is { } targetColumn && candidates.Find(candidate => ReferenceEquals(candidate.Element, scrollTarget) && candidate.Column == targetColumn) is { } columnCandidate)
                return columnCandidate;

            foreach (var candidate in candidates)
            {
                if (candidate.Column == 0 && !ReferenceEquals(candidate.Element, scrollTarget) && candidate.Element.IsShadowIncludingInclusiveAncestorOf(scrollTarget)
                    && GetColumnOf(candidate.Element, scrollTarget) is { } startColumn && candidates.Find(other => ReferenceEquals(other.Element, candidate.Element) && other.Column == startColumn) is { } startCandidate)
                {
                    // The column box contains its content: the scroll markers of the elements of the column that precede the target
                    // are encountered first by the reverse tree order walk (as in Blink, WPT
                    // css/css-overflow/scroll-markers/targeted-column-scroll-marker-selection-004.html)
                    ScrollMarkerCandidate? inColumn = null;
                    foreach (var other in candidates)
                    {
                        if (other.Column is null && candidate.Element.IsShadowIncludingInclusiveAncestorOf(other.Element) && !ReferenceEquals(other.Element, candidate.Element)
                            && (ReferenceEquals(other.Element, scrollTarget) || IsBefore(other.Element, scrollTarget)) && (inColumn is null || IsBefore(inColumn.Element, other.Element))
                            && GetColumnOf(candidate.Element, other.Element) == startColumn)
                        {
                            inColumn = other;
                        }
                    }

                    return inColumn ?? startCandidate;
                }
            }

            ScrollMarkerCandidate? best = null;
            ScrollMarkerCandidate? first = null;
            foreach (var candidate in candidates)
            {
                if (first is null || IsBefore(candidate.Element, first.Element))
                {
                    first = candidate;
                }

                if ((ReferenceEquals(candidate.Element, scrollTarget) || IsBefore(candidate.Element, scrollTarget)) && (best is null || IsBefore(best.Element, candidate.Element)))
                {
                    best = candidate;
                }
            }

            return best ?? first;
        }

        // "Let primary be the primary scrolling axis, assumed to be the block direction of the container's writing-mode. [...] For
        // each axis of primary, followed by secondary", the targets selected in the first axis are the candidates of the second one
        var position = GetTargetScrollPosition(scroller);
        var positions = new Dictionary<ScrollMarkerCandidate, PhysicalOffset>(ReferenceEqualityComparer.Instance);
        var scrollport = ScrollIntoView.GetScrollport(_document, fragment, scroller);
        foreach (var candidate in candidates)
        {
            // "determine the scroll-into-view position of target in axis", with the alignment of its scroll-snap-align, start by
            // default (https://drafts.csswg.org/css-overflow-5/#scroll-marker-activation)
            positions[candidate] = scroller.Offset + ScrollIntoView.GetScrollDelta(candidate.Rect, scrollport, scroller.WritingDirection, ToScrollPosition(candidate.Align.Block), ToScrollPosition(candidate.Align.Inline));
        }

        var primaryIsVertical = scroller.WritingDirection.IsHorizontal;
        var primary = ChooseScrollMarkerCandidates(scroller, candidates, positions, position, primaryIsVertical);
        if (primary.Count == 1)
            return primary[0];

        var secondary = ChooseScrollMarkerCandidates(scroller, primary.Count == 0 ? candidates : primary, positions, position, !primaryIsVertical);
        return secondary.Count > 0 ? secondary[^1] : null;

        static bool IsBefore(Element first, Element second) => !ReferenceEquals(first, second) && ReferenceEquals(first.GetRoot(), second.GetRoot()) && first.IsPreceding(second);
    }

    /// <summary>The alignment of the scroll-into-view position of a scroll target: the one of its <c>scroll-snap-align</c>, <c>start</c> by default.</summary>
    internal static ScrollLogicalPosition ToScrollPosition(SnapAlignment alignment) => alignment switch
    {
        SnapAlignment.End => ScrollLogicalPosition.End,
        SnapAlignment.Center => ScrollLogicalPosition.Center,
        _ => ScrollLogicalPosition.Start,
    };

    /// <summary>
    /// Selects the candidates of an axis: the target positions that the scroll range cannot reach (near its ends) are distributed
    /// over a part of the range, then "the largest target position where target position is equal to or before position in the
    /// axis", or the nearest position after it when it is closer and less than half a scrollport away (as Chrome). All the
    /// candidates of the selected position are returned; an axis that does not scroll selects all the candidates.
    /// </summary>
    private static List<ScrollMarkerCandidate> ChooseScrollMarkerCandidates(ScrollState scroller, List<ScrollMarkerCandidate> candidates, Dictionary<ScrollMarkerCandidate, PhysicalOffset> positions, PhysicalOffset position, bool vertical)
    {
        var min = vertical ? scroller.MinOffset.Y : scroller.MinOffset.X;
        var max = vertical ? scroller.MaxOffset.Y : scroller.MaxOffset.X;
        if (min == max)
            return candidates;

        // "Let distribute range be min(1/8 * scrollport size, scroll range / 2)"
        var scrollportSize = vertical ? scroller.Scrollport.Height : scroller.Scrollport.Width;
        var distributeRange = Math.Min(scrollportSize / 8, (max - min) / 2);
        var targetPositions = new double[candidates.Count];
        var minPosition = double.PositiveInfinity;
        var maxPosition = double.NegativeInfinity;
        for (var i = 0; i < candidates.Count; i++)
        {
            var offset = positions[candidates[i]];
            targetPositions[i] = vertical ? offset.Y : offset.X;
            minPosition = Math.Min(minPosition, targetPositions[i]);
            maxPosition = Math.Max(maxPosition, targetPositions[i]);
        }

        // "redistribute unreachable target positions"
        var afterStart = max - distributeRange;
        for (var i = 0; i < candidates.Count; i++)
        {
            var targetPosition = targetPositions[i];
            if (targetPosition < min + distributeRange)
            {
                targetPositions[i] = ((targetPosition - minPosition) / (min + distributeRange - minPosition) * distributeRange) + min;
            }
            else if (targetPosition > afterStart)
            {
                targetPositions[i] = ((targetPosition - afterStart) / (maxPosition - afterStart) * distributeRange) + afterStart;
            }
        }

        // The positions are compared by their distance from the scroll origin (the scroll range is negative when the overflow
        // extends leftwards or upwards), from the nearest
        var order = new int[candidates.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        var nonNegativeRange = max > 0;
        Array.Sort(order, (a, b) =>
        {
            var result = nonNegativeRange ? targetPositions[a].CompareTo(targetPositions[b]) : targetPositions[b].CompareTo(targetPositions[a]);
            return result != 0 ? result : a.CompareTo(b);
        });

        var intended = Math.Abs(vertical ? position.Y : position.X);
        var afterWindow = scrollportSize / 2;
        var before = new List<ScrollMarkerCandidate>();
        var after = new List<ScrollMarkerCandidate>();
        double? bestBefore = null;
        double? bestAfter = null;
        foreach (var index in order)
        {
            var candidate = candidates[index];
            var candidatePosition = Math.Abs(targetPositions[index]);
            if (bestBefore is null)
            {
                // "If there is no such position, set the selected position to the first one"
                bestBefore = candidatePosition;
                before.Add(candidate);
                continue;
            }

            if (candidatePosition <= intended)
            {
                if (candidatePosition > bestBefore)
                {
                    bestBefore = candidatePosition;
                    before.Clear();
                    before.Add(candidate);
                }
                else if (candidatePosition == bestBefore)
                {
                    before.Add(candidate);
                }
            }
            else if (candidatePosition < intended + afterWindow)
            {
                if (bestAfter is null || candidatePosition < bestAfter)
                {
                    bestAfter = candidatePosition;
                    after.Clear();
                    after.Add(candidate);
                }
                else if (candidatePosition == bestAfter)
                {
                    after.Add(candidate);
                }
            }
        }

        if (after.Count > 0 && bestAfter - intended < intended - bestBefore)
            return after;

        return before;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scroll buttons (https://drafts.csswg.org/css-overflow-5/#scroll-buttons)
    // ----------------------------------------------------------------------------------------------------------------

    private bool UpdateScrollButtonStates(ScrollControlBoxes? controls, bool onlyNew)
    {
        var changed = false;
        var previous = _scrollButtonStates;
        Dictionary<(Element Element, string Button), bool>? states = null;
        if (controls is not null)
        {
            foreach (var (button, _) in controls.Buttons)
            {
                var key = (button.Element, button.PseudoElement);
                var wasEnabled = false;
                var isNew = previous is null || !previous.TryGetValue(key, out wasEnabled);
                var enabled = onlyNew && !isNew ? wasEnabled : IsScrollButtonEnabledNow(button.Element, button.PseudoElement);
                changed |= enabled != wasEnabled;
                (states ??= [])[key] = enabled;
            }
        }

        _scrollButtonStates = states;
        return changed;
    }

    /// <summary>The scrolling box a scroll button of an element scrolls: the viewport for the root element.</summary>
    internal ScrollState? GetScrollButtonScrollState(Element element)
    {
        var state = ReferenceEquals(element, _document.DocumentElement) ? _viewportScrollState : TryGetScrollState(element);
        return state is { Fragment: not null } ? state : null;
    }

    /// <summary>
    /// Whether a scroll button can scroll its originating element: the position its activation scrolls to (see
    /// <see cref="GetScrollByPagePosition"/>) differs from the current position in its direction, both rounded to the nearest
    /// pixel (as Chrome: a button of a snap container is disabled when the scroll would snap back).
    /// </summary>
    private bool IsScrollButtonEnabledNow(Element element, string button)
    {
        if (GetScrollButtonScrollState(element) is not { } state)
            return false;

        var side = ScrollControls.GetDirection(button, state.WritingDirection);
        var target = GetScrollByPagePosition(state, side);
        var current = state.Offset;
        return side switch
        {
            PhysicalSide.Top => Math.Round(target.Y, MidpointRounding.AwayFromZero) < Math.Round(current.Y, MidpointRounding.AwayFromZero),
            PhysicalSide.Bottom => Math.Round(target.Y, MidpointRounding.AwayFromZero) > Math.Round(current.Y, MidpointRounding.AwayFromZero),
            PhysicalSide.Left => Math.Round(target.X, MidpointRounding.AwayFromZero) < Math.Round(current.X, MidpointRounding.AwayFromZero),
            _ => Math.Round(target.X, MidpointRounding.AwayFromZero) > Math.Round(current.X, MidpointRounding.AwayFromZero),
        };
    }

    /// <summary>
    /// Returns the position a scroll by one page towards a physical side ends at (https://drafts.csswg.org/css-overflow-5/#scroll-buttons:
    /// "scroll their originating element by one "page" in their associated direction, similar to pressing PgUp/PgDn keys. (Usually,
    /// this will be about 85% of the scrollport size.) Like those keys, this scroll has an intended direction and end position"):
    /// the page step of Chrome (87.5% of the scrollport, or the scrollport less 40px when it is larger), snapped as a relative
    /// scroll (https://drafts.csswg.org/css-scroll-snap-1/#relative-scroll) and clamped to the scrolling area.
    /// </summary>
    internal PhysicalOffset GetScrollByPagePosition(ScrollState state, PhysicalSide side, bool recordSnapTargets = false)
    {
        var length = side is PhysicalSide.Top or PhysicalSide.Bottom ? state.Scrollport.Height : state.Scrollport.Width;
        var step = Math.Max(1, Math.Max(length * 0.875, length - 40));
        var delta = side switch
        {
            PhysicalSide.Top => new PhysicalOffset(0, -step),
            PhysicalSide.Bottom => new PhysicalOffset(0, step),
            PhysicalSide.Left => new PhysicalOffset(-step, 0),
            _ => new PhysicalOffset(step, 0),
        };

        var position = state.Offset + delta;
        var strategy = SnapSelectionStrategy.ForDisplacement(state.Offset, delta);
        if (recordSnapTargets)
            return GetSnapPosition(state, strategy);

        if (GetSnapContainer(state) is { } container && container.FindSnapPosition(strategy) is { Found: true } snapped)
        {
            position = snapped.Position;
        }

        return state.Clamp(position);
    }

    /// <summary>
    /// Scrolls a scrolling box by one page towards a physical side (the activation behavior of a scroll button): a scroll
    /// without target, with the scroll behavior of the associated element.
    /// </summary>
    internal void ScrollByPage(ScrollState state, PhysicalSide side, Element associatedElement)
    {
        var position = GetScrollByPagePosition(state, side, recordSnapTargets: true);
        SetCurrentScrollTarget(state, target: null);
        if (position == state.Offset && state.SmoothScroll is null)
        {
            UpdateScrollSnapChangeTargets(state);
            return;
        }

        _ = PerformScroll(state, position, ScrollBehavior.Auto, associatedElement);
    }
}
