using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Dom;

// The state of the scroll navigation controls of a document (https://drafts.csswg.org/css-overflow-5/#scroll-navigation): the
// pseudo-elements are not nodes, so the pointing device and the focus designate them next to the element they are associated
// with (the element of the hit box for the pointing device; the scroll container, their DOM anchor, for the focus).
public partial class Document
{
    /// <summary>The scroll navigation control designated by the pointing device (it matches <c>:hover</c>), or null.</summary>
    internal ScrollControlReference? DesignatedScrollControl
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            IncrementElementStateVersion();
        }
    }

    /// <summary>The scroll navigation control that is being actively pointed at (it matches <c>:active</c>), or null.</summary>
    internal ScrollControlReference? ActivelyPointedScrollControl
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            IncrementElementStateVersion();
        }
    }

    /// <summary>
    /// The focused scroll navigation control (https://drafts.csswg.org/css-overflow-5/#scroll-focus): the focused area of the
    /// document is then its scroll container (https://drafts.csswg.org/css-overflow-5/#scroll-focus-active-element: "the
    /// activeElement is the scroll container the control is associated with"), which does not match <c>:focus</c>. It is reset
    /// when the focused area of the document changes.
    /// </summary>
    internal ScrollControlReference? FocusedScrollControl
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            IncrementElementStateVersion();
        }
    }

    /// <summary>The scroll navigation control that the focusing steps in progress focus: its scroll container is the focus target.</summary>
    internal ScrollControlReference? PendingFocusedScrollControl { get; set; }
}

/// <summary>
/// The user interactions with the scroll navigation controls (https://drafts.csswg.org/css-overflow-5/#scroll-navigation):
/// the <c>::scroll-marker</c> and <c>::scroll-button()</c> pseudo-elements are "focusable and activatable". There is no input
/// device: as for the elements (see <see cref="UserAgentDefaultActions"/>), the trusted pointer, mouse and keyboard events that
/// simulate the user input update the state of the controls and run their default actions. A pointer or mouse event designates
/// the control whose box is hit at the point of the event (the target of the event is its originating element: the
/// pseudo-elements are not in the event path).
/// </summary>
internal static class ScrollControlActions
{
    /// <summary>The boxes of the scroll navigation controls of the last layout of a document, or null when it has none.</summary>
    private static ScrollControlBoxes? GetBoxes(Document document)
        => document.LayoutEngineOrNull?.BoxTree?.ScrollControls is { } controls && (controls.Groups.Count > 0 || controls.Buttons.Count > 0) ? controls : null;

    /// <summary>
    /// Returns the scroll navigation control at the point of a pointer or mouse event (the topmost box at that point is the box
    /// of the control, or a box of its contents), or null.
    /// </summary>
    public static ScrollControlReference? GetControlAt(Document document, MouseEvent @event)
    {
        if (@event.IsPositionless || GetBoxes(document) is null || !CssomViewGeometry.IsInViewport(document, @event.ClientX, @event.ClientY))
            return null;

        if (HitTesting.HitTest(document.GetLayoutEngine(), new PhysicalOffset(@event.ClientX, @event.ClientY)) is not { Target: null } hit)
            return null;

        return GetControl(document, hit.Fragment.Box);
    }

    /// <summary>
    /// Whether the box of a scroll navigation pseudo-element is inert (https://html.spec.whatwg.org/multipage/interaction.html#inert):
    /// its scroll container is inert, or its <c>interactivity</c> is <c>inert</c> (https://drafts.csswg.org/css-ui-4/#inertness).
    /// As in Chrome, "a ::scroll-marker gets its inertness from its ::scroll-marker-group instead of its originating element
    /// unless the inertness is applied directly to the ::scroll-marker itself".
    /// </summary>
    internal static bool IsInert(Document document, Element element, string pseudoElement)
    {
        var scroller = element;
        var engine = document.GetStyleEngine();
        var style = engine.GetStyle(element, pseudoElement);
        if (ScrollControls.IsMarker(pseudoElement))
        {
            if (GetBoxes(document) is not { } boxes || !boxes.MarkerScrollers.TryGetValue(new ScrollMarkerReference(element, pseudoElement), out scroller))
                return false;

            if (style.TryGetCascadedValue("interactivity", out _) && style.GetComputedValue("interactivity") == "inert")
                return true;

            style = engine.GetStyle(scroller, ScrollControls.MarkerGroup);
        }

        return style.GetComputedValue("interactivity") == "inert" || FocusModel.IsInert(scroller);
    }

    /// <summary>Returns the scroll navigation control a box belongs to: the box of the control, its text or an anonymous box of its contents.</summary>
    internal static ScrollControlReference? GetControl(Document document, LayoutBox? box)
    {
        for (; box is not null; box = box.Parent)
        {
            if (box.Element is not { } element)
                continue;

            if (!ScrollControls.IsScrollControl(box.PseudoElement))
                return null;

            if (!ScrollControls.IsMarker(box.PseudoElement))
                return new ScrollControlReference(element, box.PseudoElement!, element);

            return GetBoxes(document) is { } boxes && boxes.MarkerScrollers.TryGetValue(new ScrollMarkerReference(element, box.PseudoElement), out var scroller)
                ? new ScrollControlReference(element, box.PseudoElement!, scroller)
                : null;
        }

        return null;
    }

    /// <summary>
    /// Updates the control designated by the pointing device before a trusted pointer or mouse event is dispatched (see
    /// <see cref="UserActionStates.BeforeDispatch"/>): it matches <c>:hover</c>, and <c>:active</c> between the press and the
    /// release of a button.
    /// </summary>
    public static void BeforeDispatch(MouseEvent @event, Node target)
    {
        var document = target.NodeDocument;
        if (GetBoxes(document) is null && document.DesignatedScrollControl is null && document.ActivelyPointedScrollControl is null)
            return;

        switch (@event.Type)
        {
            case "pointerout" or "pointerleave" or "mouseout" or "mouseleave":
                // The pointer leaves the element: the control at its position is the one of the element it enters, if any
                document.DesignatedScrollControl = @event.RelatedTarget is Node ? GetControlAt(document, @event) : null;
                break;

            case "pointerover" or "pointerenter" or "mouseover" or "mouseenter" or "pointermove" or "mousemove":
                document.DesignatedScrollControl = GetControlAt(document, @event);
                break;

            case "pointerdown" or "mousedown":
                document.DesignatedScrollControl = GetControlAt(document, @event);
                document.ActivelyPointedScrollControl = document.DesignatedScrollControl;
                break;

            case "pointerup" or "mouseup" or "pointercancel":
                document.DesignatedScrollControl = GetControlAt(document, @event);
                if (@event.Buttons == 0 || @event.Type == "pointercancel")
                {
                    document.ActivelyPointedScrollControl = null;
                }

                break;
        }
    }

    /// <summary>
    /// The default action of a press of the primary button on a scroll navigation control: a scroll marker or an enabled scroll
    /// button is focused (they are focusable; "disabled ::scroll-button() shouldn't be focusable"), and, as a press outside of the
    /// selectable content, it empties the selection. Returns false when no focusable control is pressed: the click focus of the
    /// elements applies.
    /// </summary>
    public static bool OnMouseDown(MouseEvent @event, Node target)
    {
        var document = target.NodeDocument;
        if (GetControlAt(document, @event) is not { } control || !IsFocusable(control))
            return false;

        document.GetSelection()?.RemoveAllRanges();
        if (document.BrowsingContext?.Top.Document is { } topDocument)
        {
            topDocument.LastFocusWasPointer = true;
        }

        FocusModel.FocusScrollControl(control, focusVisible: null, FocusTrigger.Click);
        return true;
    }

    /// <summary>The default action of a click on a scroll navigation control: its activation behavior.</summary>
    public static void OnClick(MouseEvent @event, Node target)
    {
        if (GetControlAt(target.NodeDocument, @event) is { } control)
        {
            Activate(control, byKeyboard: false);
        }
    }

    /// <summary>
    /// The default action of a key pressed while a scroll navigation control is focused (the keyboard events are dispatched to
    /// its scroll container): Enter and the space key activate it; the arrow keys activate the next or the previous marker of
    /// the group of a scroll marker ("activated by explicit invocation or arrow key focus"), in the writing mode of the scroll
    /// container, cycling. Returns whether the key was handled.
    /// </summary>
    public static bool OnKeyDown(KeyboardEvent @event, Node target)
    {
        var document = target.NodeDocument;
        if (document.FocusedScrollControl is not { } control || !ReferenceEquals(control.Scroller, target) || @event.CtrlKey || @event.AltKey || @event.MetaKey)
            return false;

        if (@event.Key is "Enter" or " ")
        {
            Activate(control, byKeyboard: true);
            return true;
        }

        if (!control.IsMarker || document.BrowsingContext is null)
            return false;

        PhysicalSide side;
        switch (@event.Key)
        {
            case "ArrowLeft":
                side = PhysicalSide.Left;
                break;
            case "ArrowRight":
                side = PhysicalSide.Right;
                break;
            case "ArrowUp":
                side = PhysicalSide.Top;
                break;
            case "ArrowDown":
                side = PhysicalSide.Bottom;
                break;
            default:
                return false;
        }

        var engine = document.GetLayoutEngine();
        engine.EnsureLayout();
        if (engine.GetScrollMarkerGroup(control.Scroller) is not { Markers.Count: > 0 } group)
            return true;

        var index = group.Markers.IndexOf(new ScrollMarkerReference(control.Element, control.PseudoElement));
        if (index < 0)
            return true;

        var writingDirection = WritingDirection.FromStyle(document.GetStyleEngine().GetStyle(control.Scroller).Layout);
        var forward = side == writingDirection.InlineEnd || side == writingDirection.BlockEnd;
        var next = group.Markers[(index + (forward ? 1 : group.Markers.Count - 1)) % group.Markers.Count];
        Activate(new ScrollControlReference(next.Element, next.PseudoElement!, control.Scroller), byKeyboard: true);
        return true;
    }

    /// <summary>
    /// Whether a scroll navigation control is a focusable area: its box is generated, its scroll container is not inert, and a
    /// scroll button is enabled (https://drafts.csswg.org/css-overflow-5/#scroll-buttons).
    /// </summary>
    public static bool IsFocusable(ScrollControlReference control)
    {
        var document = control.Scroller.NodeDocument;
        if (!control.Scroller.IsConnected || document.BrowsingContext is null || GetBoxes(document) is not { } boxes || IsInert(document, control.Element, control.PseudoElement))
            return false;

        if (control.IsMarker)
            return boxes.MarkerScrollers.TryGetValue(new ScrollMarkerReference(control.Element, control.PseudoElement), out var scroller) && ReferenceEquals(scroller, control.Scroller);

        if (!control.IsButton)
            return false;

        foreach (var (button, _) in boxes.Buttons)
        {
            if (button == control)
                return document.GetLayoutEngine().IsScrollButtonEnabled(control.Element, control.PseudoElement);
        }

        return false;
    }

    /// <summary>
    /// The activation behavior of a scroll marker (https://drafts.csswg.org/css-overflow-5/#scroll-marker-activation) or of a
    /// scroll button (https://drafts.csswg.org/css-overflow-5/#scroll-buttons).
    /// </summary>
    public static void Activate(ScrollControlReference control, bool byKeyboard)
    {
        var document = control.Scroller.NodeDocument;
        if (!control.Scroller.IsConnected || document.BrowsingContext is null)
            return;

        var engine = document.GetLayoutEngine();
        engine.EnsureLayout();
        if (control.IsButton)
        {
            ActivateButton(engine, control, byKeyboard);
        }
        else if (control.IsMarker)
        {
            ActivateMarker(engine, control, byKeyboard);
        }
    }

    /// <summary>
    /// "Scroll their originating element by one "page" in their associated direction, similar to pressing PgUp/PgDn keys": a
    /// relative scroll with the scroll behavior of the scroll container; the button is focused (as in Chrome).
    /// </summary>
    private static void ActivateButton(LayoutEngine engine, ScrollControlReference control, bool byKeyboard)
    {
        if (!IsFocusable(control) || engine.GetScrollButtonScrollState(control.Element) is not { } state)
            return;

        var side = ScrollControls.GetDirection(control.PseudoElement, state.WritingDirection);
        engine.ScrollByPage(state, side, control.Element);
        FocusModel.FocusScrollControl(control, focusVisible: byKeyboard ? true : null, byKeyboard ? FocusTrigger.Other : FocusTrigger.Click);
    }

    /// <summary>
    /// "Let element be the scroll target of the control. Let block be "start". Let inline be "start". Let container be the
    /// nearest common ancestor scroll container of the scroll markers in the scroll marker group associated with this scroll
    /// marker. Scroll the element into view with block, inline, and container." The alignment of the <c>scroll-snap-align</c> of
    /// the target is used when it is not <c>none</c> (as in Chrome). Then, in tabs mode, "retain focus on the scroll marker
    /// element"; in links mode, "set the sequential focus navigation starting point to the originating element, losing focus
    /// from the scroll marker".
    /// </summary>
    private static void ActivateMarker(LayoutEngine engine, ScrollControlReference control, bool byKeyboard)
    {
        var document = control.Scroller.NodeDocument;
        if (!IsFocusable(control) || engine.GetScrollButtonScrollState(control.Scroller) is not { } state)
            return;

        // The scroll target of a ::column::scroll-marker is its column, aligned by the scroll-snap-align of ::column
        var target = control.Element;
        int? column = ScrollControls.IsColumnMarker(control.PseudoElement) ? ScrollControls.GetColumnIndex(control.PseudoElement) : null;
        var columnRect = column is { } columnIndex ? engine.GetColumnRect(target, columnIndex) : null;
        if (column is not null && columnRect is null)
            return;

        var targetStyle = column is null ? document.GetStyleEngine().GetStyle(target) : document.GetStyleEngine().GetStyle(target, ScrollControls.Column);
        var align = targetStyle.Layout.Overflow.ScrollSnapAlign;
        _ = ScrollIntoView.Run(engine, target, ScrollBehavior.Auto, LayoutEngine.ToScrollPosition(align.Block), LayoutEngine.ToScrollPosition(align.Inline), container: null, lastScrollingBox: state, targetRect: columnRect);

        // The scroll does not move a target that is already aligned: the marker is the active one whatever the position
        engine.SetCurrentScrollTarget(state, target, column);
        engine.RequestScrollMarkerSnapshot();
        var isTabs = engine.BoxTree?.ScrollControls is { } boxes && boxes.Groups.TryGetValue(control.Scroller, out var group) && group.IsTabs;
        if (isTabs)
        {
            FocusModel.FocusScrollControl(control, focusVisible: byKeyboard ? true : null, byKeyboard ? FocusTrigger.Other : FocusTrigger.Click);
        }
        else if (document.BrowsingContext?.Top.Document is { } topDocument && FocusModel.GetCurrentlyFocusedArea(topDocument) is Element focused && ReferenceEquals(focused.NodeDocument, document))
        {
            // The focused area of the document is its viewport
            FocusModel.RunFocusingSteps(document);
        }

        if (!isTabs && target.IsConnected)
        {
            // The navigation continues at the originating element: it is the next area when it is focusable (at the first
            // element of the column, for the scroll marker of a column)
            var start = column is { } startColumn ? engine.GetFirstElementOfColumn(target, startColumn) : target;
            document.SequentialFocusNavigationStartingPoint = start is not null ? new(start, FocusNavigationStartingPointKind.BeforeNode) : new(target, FocusNavigationStartingPointKind.AfterNode);
        }
    }

    /// <summary>A stop of the sequential focus navigation among the scroll navigation controls of a scroll container.</summary>
    internal readonly record struct TabStop(ScrollControlReference Control, bool BeforeScroller);

    /// <summary>
    /// Returns the scroll navigation controls of a document that are sequentially focusable, grouped by scroll container in the
    /// focus navigation order (https://drafts.csswg.org/css-overflow-5/#scroll-focus-order): the markers of the
    /// <c>::scroll-marker-group</c> when it is before, the <c>::scroll-button()</c> pseudo-elements (then the scroll container
    /// and its contents), then the markers of the group when it is after. In links mode "all ::scroll-marker pseudo-elements are
    /// tab stops, in order"; in tabs mode "only the active ::scroll-marker is a tab stop" (the first one when none is active).
    /// </summary>
    public static List<TabStop> GetTabStops(Document document)
    {
        var result = new List<TabStop>();
        if (document.BrowsingContext is null)
            return result;

        // A document that was never laid out has boxes only when its styles may generate the controls
        var styleEngine = document.GetStyleEngine();
        if (document.LayoutEngineOrNull is null && !styleEngine.MayHavePseudoElementRules(ScrollControls.Marker) && !styleEngine.MayHavePseudoElementRules(ScrollControls.ButtonRuleName))
            return result;

        var engine = document.GetLayoutEngine();
        engine.EnsureLayout();
        if (GetBoxes(document) is not { } boxes)
            return result;

        var scrollers = new List<Element>();
        foreach (var scroller in boxes.Groups.Keys)
        {
            scrollers.Add(scroller);
        }

        foreach (var (button, _) in boxes.Buttons)
        {
            if (!scrollers.Contains(button.Scroller))
            {
                scrollers.Add(button.Scroller);
            }
        }

        foreach (var scroller in scrollers)
        {
            if (FocusModel.IsInert(scroller))
                continue;

            boxes.Groups.TryGetValue(scroller, out var group);
            var groupIsBefore = group is not null && styleEngine.GetStyle(scroller).GetComputedValue("scroll-marker-group").StartsWith("before", StringComparison.Ordinal);
            if (group is not null && groupIsBefore)
            {
                AddMarkers(group, beforeScroller: true);
            }

            foreach (var (button, _) in boxes.Buttons)
            {
                if (ReferenceEquals(button.Scroller, scroller) && engine.IsScrollButtonEnabled(button.Element, button.PseudoElement))
                {
                    result.Add(new TabStop(button, BeforeScroller: true));
                }
            }

            if (group is not null && !groupIsBefore)
            {
                AddMarkers(group, beforeScroller: false);
            }
        }

        return result;

        void AddMarkers(ScrollMarkerGroupBox group, bool beforeScroller)
        {
            if (group.Markers.Count == 0)
                return;

            if (!group.IsTabs)
            {
                foreach (var marker in group.Markers)
                {
                    result.Add(new TabStop(new ScrollControlReference(marker.Element, marker.PseudoElement!, group.Scroller), beforeScroller));
                }

                return;
            }

            // The focused marker of the group stays its tab stop (the focus follows the active marker)
            var tabStop = group.Markers[0];
            if (document.FocusedScrollControl is { IsMarker: true } focused && ReferenceEquals(focused.Scroller, group.Scroller) && group.Markers.Contains(new ScrollMarkerReference(focused.Element, focused.PseudoElement)))
            {
                tabStop = new ScrollMarkerReference(focused.Element, focused.PseudoElement);
            }
            else if (engine.GetScrollMarkerGroup(group.Scroller)?.Active is { } active && group.Markers.Contains(active))
            {
                tabStop = active;
            }

            result.Add(new TabStop(new ScrollControlReference(tabStop.Element, tabStop.PseudoElement!, group.Scroller), beforeScroller));
        }
    }
}
