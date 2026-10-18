using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The <c>::scroll-marker-group</c> box of a scroll container and its scroll markers (see <see cref="ScrollControlBoxes"/>).</summary>
internal sealed class ScrollMarkerGroupBox(Element scroller, ContainerBox box, ContainerBox contents, bool isTabs)
{
    /// <summary>The scroll container: the originating element of the group.</summary>
    public Element Scroller { get; } = scroller;

    /// <summary>The principal box of the group.</summary>
    public ContainerBox Box { get; } = box;

    /// <summary>The box the boxes of the scroll markers are children of (the table grid box of a group that is a table, else <see cref="Box"/>).</summary>
    public ContainerBox Contents { get; } = contents;

    /// <summary>Whether the group operates in tabs mode (https://drafts.csswg.org/css-overflow-5/#scroll-marker-modes), in links mode otherwise.</summary>
    public bool IsTabs { get; } = isTabs;

    /// <summary>The <c>::scroll-marker</c> pseudo-elements of the group (of the elements, and of the columns of the multi-column containers), in tree order.</summary>
    public List<ScrollMarkerReference> Markers { get; } = [];
}

/// <summary>
/// The boxes of the scroll navigation controls of a box tree (https://drafts.csswg.org/css-overflow-5/#scroll-navigation): the
/// <c>::scroll-marker-group</c> boxes with their <c>::scroll-marker</c> pseudo-elements, and the <c>::scroll-button()</c> boxes.
/// </summary>
internal sealed class ScrollControlBoxes
{
    /// <summary>The scroll marker groups, by scroll container.</summary>
    public Dictionary<Element, ScrollMarkerGroupBox> Groups { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>The scroll container of the group of each generated <c>::scroll-marker</c>.</summary>
    public Dictionary<ScrollMarkerReference, Element> MarkerScrollers { get; } = [];

    /// <summary>
    /// The multi-column containers whose columns may have scroll markers (a <c>::column::scroll-marker</c> rule may apply to
    /// them), with the number of columns the markers were generated for: the number of columns of the previous layout (see
    /// <see cref="LayoutEngine.UpdateColumnScrollMarkers"/>).
    /// </summary>
    public Dictionary<Element, int> ColumnMarkerCounts { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>The generated scroll buttons (the element is the scroller), with their boxes.</summary>
    public List<(ScrollControlReference Button, ContainerBox Box)> Buttons { get; } = [];

    /// <summary>
    /// The elements that generate boxes whose <c>scroll-target-group</c> is <c>auto</c> (https://drafts.csswg.org/css-overflow-5/#scroll-target-group):
    /// the scroll marker group containers. The set may keep elements whose style changed since (see <see cref="BoxTreeBuilder.RebuildContents"/>).
    /// </summary>
    public HashSet<Element> TargetGroupContainers { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Whether the scroll markers were collected again during a layout pass (the contents of a size container were built again,
    /// see <see cref="BoxTreeBuilder.RebuildContents"/>): the groups that were already laid out must be laid out again.
    /// </summary>
    public bool PopulatedDuringLayout { get; set; }
}

// The boxes of the scroll navigation controls (https://drafts.csswg.org/css-overflow-5/#scroll-navigation).
//
// The ::scroll-marker-group of a scroll container and its ::scroll-button() pseudo-elements generate boxes as siblings of the
// box of their originating element: the group before or after it (scroll-marker-group: before | after), the buttons after it
// (in the order block-start, inline-start, inline-end, block-end; before the group when it is after). The specification says
// that the buttons are "immediately preceding siblings": they follow the scroller as in Chrome, the only implementation, and as
// the web-platform-tests check (css-pseudo/relative-box-order-of-pseudo-elements.html). The boxes of the root element are
// children of its box: the group before is the first child, the buttons and the group after are the last children.
//
// The ::scroll-marker of an element generates a box in the group of the nearest scroll container of the containing block chain
// of the box of the element (an absolutely positioned box whose containing block is outside of the scroll container is not
// scrolled by it). They are collected once the tree is built, in tree order.
internal sealed partial class BoxTreeBuilder
{
    private ScrollControlBoxes? _scrollControls;

    private ScrollControlBoxes GetScrollControls() => _scrollControls ??= new ScrollControlBoxes();

    /// <summary>
    /// Appends the boxes of the scroll navigation controls of an element other than the root element next to its boxes, which
    /// were just appended to <paramref name="parent"/>: the <c>::scroll-marker-group</c> before or after the principal box of a
    /// scroll container, and the <c>::scroll-button()</c> boxes after the boxes of the element (the contents of an element with
    /// <c>display: contents</c>).
    /// </summary>
    private void BuildScrollControls(Element element, ContainerBox parent)
    {
        var style = _engine.GetStyle(element);
        RegisterScrollTargetGroup(element, style);
        var hasButtons = _engine.MayHaveAuthorPseudoElementRules(element, ScrollControls.ButtonRuleName);
        var hasGroup = style.TryGetCascadedValue("scroll-marker-group", out _) || style.HasAnimationEffects;
        if (!hasButtons && !hasGroup)
            return;

        var display = style.Layout.Display;
        if (display.IsNone)
            return;

        // The boxes of an element of the top layer are children of the viewport (see BuildTopLayer): it has no sibling
        if (parent.Kind == ContainerBoxKind.Viewport || (!Document.IsTopLayerPicker(element) && element.NodeDocument.IsRenderedInTopLayer(element, style.GetComputedValue("overlay"))))
            return;

        if (display.IsContents)
        {
            // An element without box is not a scroll container: it has no group; its buttons follow its contents
            if (hasButtons)
            {
                BuildScrollButtons(element, parent);
            }

            return;
        }

        if (_boxes.GetValueOrDefault((element, null)) is not ContainerBox box || !ReferenceEquals(box.Parent, parent))
            return;

        var position = hasGroup ? GetScrollMarkerGroupPosition(style) : null;
        // (the scroll container of a fieldset is its anonymous fieldset content box, which is not generated yet)
        if (position is not null && !ScrollContainers.IsScrollContainer(box) && !(BlockFormattingContext.IsFieldset(box) && style.Layout.IsScrollContainer))
        {
            position = null;
        }

        if (position == "before")
        {
            BuildScrollMarkerGroup(element, style, parent, reference: box);
        }

        if (hasButtons)
        {
            BuildScrollButtons(element, parent);
        }

        if (position == "after")
        {
            BuildScrollMarkerGroup(element, style, parent, reference: null);
        }
    }

    /// <summary>Records an element whose <c>scroll-target-group</c> is <c>auto</c>: its links are scroll markers (see <see cref="LayoutEngine.UpdateScrollMarkerSnapshots"/>).</summary>
    private void RegisterScrollTargetGroup(Element element, ComputedStyle style)
    {
        if (style.TryGetCascadedValue("scroll-target-group", out _) && !style.Layout.Display.IsNone && style.GetComputedValue("scroll-target-group") == "auto")
        {
            GetScrollControls().TargetGroupContainers.Add(element);
        }
    }

    /// <summary>The position keyword of the computed <c>scroll-marker-group</c> (https://drafts.csswg.org/css-overflow-5/#scroll-marker-group-property), or null for <c>none</c>.</summary>
    private static string? GetScrollMarkerGroupPosition(ComputedStyle style)
    {
        var value = style.GetComputedValue("scroll-marker-group");
        if (value.StartsWith("before", StringComparison.Ordinal))
            return "before";

        return value.StartsWith("after", StringComparison.Ordinal) ? "after" : null;
    }

    /// <summary>
    /// Builds the scroll navigation controls of the root element that precede its contents: its <c>::scroll-marker-group</c>
    /// when it is before ("When originating from the root element, the generated pseudo-element's box is the first child of the
    /// originating element"). The viewport is the scroll container.
    /// </summary>
    private void BuildRootScrollControlsBefore(Element element, ComputedStyle style, ContainerBox contents)
    {
        RegisterScrollTargetGroup(element, style);
        if ((style.TryGetCascadedValue("scroll-marker-group", out _) || style.HasAnimationEffects) && GetScrollMarkerGroupPosition(style) == "before")
        {
            BuildScrollMarkerGroup(element, style, contents, reference: null);
        }
    }

    /// <summary>Builds the scroll navigation controls of the root element that follow its contents: its scroll buttons, then its <c>::scroll-marker-group</c> when it is after.</summary>
    private void BuildRootScrollControlsAfter(Element element, ComputedStyle style, ContainerBox contents)
    {
        if (_engine.MayHaveAuthorPseudoElementRules(element, ScrollControls.ButtonRuleName))
        {
            BuildScrollButtons(element, contents);
        }

        if ((style.TryGetCascadedValue("scroll-marker-group", out _) || style.HasAnimationEffects) && GetScrollMarkerGroupPosition(style) == "after")
        {
            BuildScrollMarkerGroup(element, style, contents, reference: null);
        }
    }

    /// <summary>
    /// Appends the box of the <c>::scroll-marker-group</c> of a scroll container to the parent box (before
    /// <paramref name="reference"/> when it is not null). Its markers are collected once the tree is built (see
    /// <see cref="PopulateScrollMarkerGroups"/>).
    /// </summary>
    private void BuildScrollMarkerGroup(Element element, ComputedStyle elementStyle, ContainerBox parent, ContainerBox? reference)
    {
        var style = _engine.GetStyle(element, ScrollControls.MarkerGroup);
        var display = style.Layout.Display;
        if (display.IsNone || display.IsContents)
            return;

        // The markers are collected after the fixups of the parent box: the group must not be an inline box, which is split
        // around its block-level children by its block container
        display = BlockifyForLayoutParent(display, parent);
        if (display.IsInlineBox)
        {
            display = new Display(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.FlowRoot);
        }

        var box = CreatePrincipalBox(style, display, element, ScrollControls.MarkerGroup, parent, out var contents);
        if (reference is not null)
        {
            parent.InsertBefore(box, reference);
        }

        _boxes[(element, ScrollControls.MarkerGroup)] = box;
        var isTabs = elementStyle.GetComputedValue("scroll-marker-group").EndsWith("tabs", StringComparison.Ordinal);
        GetScrollControls().Groups[element] = new ScrollMarkerGroupBox(element, box, contents, isTabs);
    }

    /// <summary>
    /// Appends the boxes of the <c>::scroll-button()</c> pseudo-elements of an element whose <c>content</c> is not <c>none</c>
    /// (https://drafts.csswg.org/css-overflow-5/#scroll-buttons), "in order, block-start, inline-start, inline-end, and block-end".
    /// </summary>
    private void BuildScrollButtons(Element element, ContainerBox parent)
    {
        foreach (var button in ScrollControls.Buttons)
        {
            if (BuildScrollControlBox(element, button, parent) is { } box)
            {
                GetScrollControls().Buttons.Add((new ScrollControlReference(element, button, element), box));
            }
        }
    }

    /// <summary>
    /// Appends the box of a <c>::scroll-button()</c> or <c>::scroll-marker</c> pseudo-element, generated when its <c>content</c>
    /// is not <c>none</c>, with its generated content. Returns whether the pseudo-element is generated (it has no box when its
    /// display is <c>contents</c>).
    /// </summary>
    private bool BuildScrollControl(Element element, string pseudoElement, ContainerBox parent, out ContainerBox? box)
    {
        box = null;
        var style = _engine.GetStyle(element, pseudoElement);
        var content = style.GetComputedValue("content");
        if (content is "none" or "normal" or "")
            return false;

        var display = style.Layout.Display;
        if (display.IsNone)
            return false;

        if (display.IsContents)
        {
            AppendContent(parent, style, element, pseudoElement, content);
            return true;
        }

        // The layout parent of the pseudo-element is not its originating element: the computed display is not blockified
        display = BlockifyForLayoutParent(display, parent);

        // https://drafts.csswg.org/css-overflow-5/#scroll-buttons: the buttons "are styled identically to the button element":
        // the button layout (https://html.spec.whatwg.org/multipage/rendering.html#button-layout, see BuildElement)
        if (ScrollControls.IsScrollButton(pseudoElement) && (display.Inside is not (DisplayInside.Flex or DisplayInside.Grid) || display.Internal != DisplayInternal.None))
        {
            display = new Display(DisplayBox.Normal, display.Outside == DisplayOutside.Inline && display.Internal == DisplayInternal.None ? DisplayOutside.Inline : DisplayOutside.Block, DisplayInside.FlowRoot);
        }

        box = CreatePrincipalBox(style, display, element, pseudoElement, parent, out var contents);
        _boxes[(element, pseudoElement)] = box;
        AppendContent(contents, style, element, pseudoElement, content);
        FixupContainer(contents);
        if (!ReferenceEquals(box, contents))
        {
            FixupContainer(box);
        }

        return true;
    }

    private ContainerBox? BuildScrollControlBox(Element element, string pseudoElement, ContainerBox parent)
        => BuildScrollControl(element, pseudoElement, parent, out var box) ? box : null;

    /// <summary>
    /// https://drafts.csswg.org/css-display/#blockify: the display of a box whose parent box is a flex or grid container is
    /// blockified. The computed display of the scroll navigation pseudo-elements is not, as their parent box is not the box of
    /// their originating element.
    /// </summary>
    private static Display BlockifyForLayoutParent(Display display, ContainerBox parent)
    {
        if (!parent.Display.IsFlexContainer && !parent.Display.IsGridContainer)
            return display;

        if (display.Box != DisplayBox.Normal || display.Outside != DisplayOutside.Inline || display.Internal != DisplayInternal.None)
            return display;

        return display with { Outside = DisplayOutside.Block, Inside = display.Inside == DisplayInside.FlowRoot ? DisplayInside.Flow : display.Inside };
    }

    /// <summary>
    /// Collects the <c>::scroll-marker</c> pseudo-elements in the <c>::scroll-marker-group</c> boxes
    /// (https://drafts.csswg.org/css-overflow-5/#scroll-marker-pseudo): "When the computed content value of a ::scroll-marker
    /// pseudo-element is not none and its nearest ancestor scroll container has a computed scroll-marker-group property that is
    /// not none, the pseudo-element generates a box attached as a child of the ::scroll-marker-group pseudo-element's generated
    /// box on its nearest ancestor scroll container. These boxes are added in the tree order of their originating element."
    /// The groups are emptied first (the markers are collected again when the contents of a size container are built again).
    /// </summary>
    private void PopulateScrollMarkerGroups(ContainerBox viewport)
    {
        if (_scrollControls is not { Groups.Count: > 0 } controls)
            return;

        foreach (var group in controls.Groups.Values)
        {
            foreach (var marker in group.Markers)
            {
                _boxes.Remove((marker.Element, marker.PseudoElement));
            }

            group.Markers.Clear();
            group.Contents.DetachChildren();
        }

        controls.MarkerScrollers.Clear();
        controls.ColumnMarkerCounts.Clear();
        CollectScrollMarkers(viewport, controls);
        foreach (var group in controls.Groups.Values)
        {
            FixupContainer(group.Contents);
        }
    }

    private void CollectScrollMarkers(ContainerBox parent, ScrollControlBoxes controls)
    {
        for (var child = parent.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is not ContainerBox box)
                continue;

            // The pseudo-elements have no scroll marker (::before::scroll-marker is not supported), and no element is in their boxes
            if (box.PseudoElement is not null)
                continue;

            if (box.Element is { } element && ReferenceEquals(_boxes.GetValueOrDefault((element, null)), box))
            {
                BuildScrollMarker(element, box, controls);
                BuildColumnScrollMarkers(element, box, controls);
            }

            CollectScrollMarkers(box, controls);
        }
    }

    private void BuildScrollMarker(Element element, ContainerBox box, ScrollControlBoxes controls)
    {
        if (!_engine.MayHaveAuthorPseudoElementRules(element, ScrollControls.Marker))
            return;

        if (FindScrollMarkerGroup(box, controls) is not { } group)
            return;

        if (BuildScrollControl(element, ScrollControls.Marker, group.Contents, out _))
        {
            var marker = new ScrollMarkerReference(element, ScrollControls.Marker);
            group.Markers.Add(marker);
            controls.MarkerScrollers[marker] = group.Scroller;
        }
    }

    /// <summary>
    /// Appends the scroll markers of the columns of a multi-column container (<c>::column::scroll-marker</c>,
    /// https://drafts.csswg.org/css-multicol-2/#column-pseudo) to the group of the nearest scroll container of the columns: the
    /// container itself when it is a scroll container. The number of columns is the one of the previous layout: the layout
    /// collects the markers and runs again when it changes (see <see cref="LayoutEngine.UpdateColumnScrollMarkers"/>).
    /// </summary>
    private void BuildColumnScrollMarkers(Element element, ContainerBox box, ScrollControlBoxes controls)
    {
        // (the columns of a fieldset are the ones of its anonymous fieldset content box)
        if (BlockFormattingContext.IsFieldset(box) && BlockFormattingContext.FindFieldsetContent(box) is { } fieldsetContent)
        {
            box = fieldsetContent;
        }

        if (!MultiColumnFormattingContext.IsMultiColumnContainer(box) || !_engine.MayHaveAuthorPseudoElementRules(element, ScrollControls.ColumnMarkerRuleName))
            return;

        var count = element.NodeDocument.LayoutEngineOrNull?.GetColumnCount(element) ?? 0;
        controls.ColumnMarkerCounts[element] = count;
        var group = ScrollContainers.IsScrollContainer(box) ? controls.Groups.GetValueOrDefault(element) : FindScrollMarkerGroup(box, controls);
        if (group is null)
            return;

        for (var i = 0; i < count; i++)
        {
            var name = ScrollControls.GetColumnMarker(i);
            if (!BuildScrollControl(element, name, group.Contents, out _))
                break;

            var marker = new ScrollMarkerReference(element, name);
            group.Markers.Add(marker);
            controls.MarkerScrollers[marker] = group.Scroller;
        }
    }

    /// <summary>Collects again the scroll markers of the groups of a box tree: the number of columns of a multi-column container whose columns have scroll markers changed.</summary>
    public static void RepopulateScrollMarkers(BoxTree tree)
    {
        if (tree.Viewport is not { } viewport || tree.ScrollControls is not { } controls || viewport.FirstChild is not ContainerBox { Element: { } root })
            return;

        var builder = new BoxTreeBuilder(root, tree.Boxes) { _scrollControls = controls };
        builder.PopulateScrollMarkerGroups(viewport);
    }

    /// <summary>
    /// Returns the group of the nearest ancestor scroll container of a box, in its containing block chain: the scroll container
    /// that scrolls the box (the viewport, whose group is the one of the root element, when there is none). Null when that
    /// scroll container has no <c>::scroll-marker-group</c>.
    /// </summary>
    private ScrollMarkerGroupBox? FindScrollMarkerGroup(ContainerBox box, ScrollControlBoxes controls)
    {
        var current = box;
        while (true)
        {
            var container = PositionedLayout.IsAbsolutelyPositioned(current) ? PositionedLayout.FindContainingBlock(current) : PositionedLayout.GetPositioningParent(current);
            if (container is null || ReferenceEquals(container, current))
                return null;

            if (container.Kind == ContainerBoxKind.Viewport)
                return controls.Groups.GetValueOrDefault(_root);

            if (container is { ScrollingElement: { } scroller } && ScrollContainers.IsScrollContainer(container))
                return controls.Groups.GetValueOrDefault(scroller);

            current = container;
        }
    }

    /// <summary>Forgets the scroll navigation controls whose boxes are not in the tree anymore (the contents of a size container were built again).</summary>
    private static void RemoveDetachedScrollControls(ScrollControlBoxes controls, ContainerBox viewport)
    {
        List<Element>? detached = null;
        foreach (var (scroller, group) in controls.Groups)
        {
            if (!IsInTree(group.Box, viewport))
            {
                (detached ??= []).Add(scroller);
            }
        }

        if (detached is not null)
        {
            foreach (var scroller in detached)
            {
                controls.Groups.Remove(scroller);
            }
        }

        controls.Buttons.RemoveAll(button => !IsInTree(button.Box, viewport));

        static bool IsInTree(ContainerBox box, ContainerBox viewport)
        {
            for (var current = box; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, viewport))
                    return true;
            }

            return false;
        }
    }
}
