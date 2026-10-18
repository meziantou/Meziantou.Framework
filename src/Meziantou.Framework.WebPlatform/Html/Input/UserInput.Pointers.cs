using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// The pointers that hover (the mouse and the pens): the events of Pointer Events
// (https://w3c.github.io/pointerevents/#firing-events-using-the-pointerevent-interface), the pointer capture, the boundary
// events, the compatibility mouse events (https://w3c.github.io/pointerevents/#compatibility-mapping-with-mouse-events) and
// the click events (https://w3c.github.io/uievents/#events-mouseevents).
public sealed partial class UserInput
{
    /// <summary>The time between two presses of a multiple click, as in the browsers.</summary>
    private static readonly TimeSpan MultipleClickTime = TimeSpan.FromMilliseconds(500);

    /// <summary>The target of the last compatibility mouse event (https://w3c.github.io/pointerevents/#tracking-the-effective-position-of-the-legacy-mouse-pointer).</summary>
    private Node? _mouseOverTarget;
    private bool _mouseNeedsOverEvent;

    internal void Move(PointerInput pointer, double x, double y, PointerInputProperties? properties)
    {
        if (properties is not null)
        {
            pointer.Properties = properties;
        }

        if (pointer.IsMouse && (x != pointer.X || y != pointer.Y))
        {
            EndWheelTransaction();
        }

        // https://w3c.github.io/pointerlock/#dom-mouseevent-movementx: while the pointer is locked, the position of the
        // pointer is constant ("as if the pointer did not move at all once pointer lock was entered"), and the events
        // only provide the movements of the device
        if (GetPointerLockTarget(pointer) is not null)
        {
            pointer.MovementX = x - pointer.DeviceX;
            pointer.MovementY = y - pointer.DeviceY;
            pointer.DeviceX = x;
            pointer.DeviceY = y;
            FireHoverableEvent(pointer, "pointermove", "mousemove", button: -1, detail: 0, out _);
            pointer.MovementX = 0;
            pointer.MovementY = 0;
            return;
        }

        var (oldX, oldY) = (pointer.X, pointer.Y);
        pointer.MovementX = x - oldX;
        pointer.MovementY = y - oldY;
        pointer.X = x;
        pointer.Y = y;
        pointer.DeviceX = x;
        pointer.DeviceY = y;
        if (pointer.IsTouch)
        {
            TouchMove(pointer, oldX, oldY);
            return;
        }

        Activate(pointer);
        if (pointer.DragOperation is { } drag)
        {
            // The pointer events are suppressed during a drag-and-drop operation
            drag.Update(HitTest(x, y), GetDragState(pointer, drag.SourceNode));
            return;
        }

        if (pointer.ScrollbarPress is { } scrollbarPress)
        {
            // The scrollbar that is pressed captures the pointer: no event is fired until the release
            MoveScrollbarPress(pointer, scrollbarPress);
            pointer.MovementX = 0;
            pointer.MovementY = 0;
            return;
        }

        FireHoverableEvent(pointer, "pointermove", "mousemove", button: -1, detail: 0, out _);
        pointer.MovementX = 0;
        pointer.MovementY = 0;
        TryStartDrag(pointer);
    }

    internal void Down(PointerInput pointer, short button, PointerInputProperties? properties)
    {
        if (properties is not null)
        {
            pointer.Properties = properties;
        }

        if (pointer.IsTouch)
        {
            TouchDown(pointer);
            return;
        }

        Activate(pointer);
        var bit = GetButtonBit(button);
        if ((pointer.Buttons & bit) != 0)
            return;

        // The press of a locked mouse is on the target of the pointer lock, not on the element at the position of the pointer
        _browsingContext.Document.OnPointerDownForSpeculation(GetPointerLockTarget(pointer) ?? pointer.CaptureTarget ?? HitTest(pointer));

        // https://w3c.github.io/pointerevents/#chorded-button-interactions: the press of another button is a pointermove
        var chorded = pointer.Buttons != 0;
        pointer.Buttons |= bit;
        if (pointer.DragOperation is not null)
            return;

        if (chorded && ShouldFirePointerRawUpdate(pointer))
        {
            FirePointerEvent(pointer, "pointerrawupdate", button, updateMouseOver: false, out _);
        }

        // The press of the primary button on a scrollbar is handled by the scrollbar (see UserInput.Scrollbars.cs)
        var scrollbarPress = button == 0 && !chorded && pointer.CaptureTarget is null && pointer.PendingCaptureTarget is null && GetPointerLockTarget(pointer) is null ? CreateScrollbarPress(pointer) : null;
        pointer.ScrollbarPress = scrollbarPress;
        var target = FirePointerEvent(pointer, chorded ? "pointermove" : "pointerdown", button, updateMouseOver: true, out var canceled);
        if (!chorded && canceled)
        {
            pointer.PreventMouseEvent = true;
        }

        if (target is null)
        {
            if (scrollbarPress is not null)
            {
                BeginScrollbarPress(pointer, scrollbarPress);
            }

            return;
        }

        // https://w3c.github.io/uievents/#current-click-count: a press of the same button at the same position and on the same
        // element as the previous press is a multiple click
        var now = Now;
        if (pointer.ClickButton == button && pointer.ClickX == pointer.X && pointer.ClickY == pointer.Y && ReferenceEquals(pointer.ClickTarget, target)
            && GetElapsed(pointer.ClickTimestamp) < MultipleClickTime)
        {
            pointer.ClickCount++;
        }
        else
        {
            pointer.ClickButton = button;
            pointer.ClickX = pointer.X;
            pointer.ClickY = pointer.Y;
            pointer.ClickTarget = target;
            pointer.ClickCount = 1;
        }

        pointer.ClickTimestamp = now;
        if (!chorded)
        {
            pointer.PressedDocument = target.NodeDocument;
        }

        (pointer.DownTargets ??= [])[button] = target;
        (pointer.DownPseudoTargets ??= [])[button] = pointer.PseudoTarget;
        if (button == 0)
        {
            pointer.DragNode = target;
            pointer.DownX = pointer.X;
            pointer.DownY = pointer.Y;
        }

        if (!pointer.PreventMouseEvent)
        {
            FireMouseEvent(pointer, "mousedown", target, button, pointer.ClickCount);
        }

        if (scrollbarPress is not null)
        {
            BeginScrollbarPress(pointer, scrollbarPress);
            return;
        }

        // The selection can be dragged when the press was in it (a press elsewhere starts a new selection, which the moves extend)
        if (button == 0)
        {
            pointer.DragsSelection = target is not (HtmlInputElement or HtmlTextAreaElement or HtmlSelectElement or HtmlButtonElement)
                && target.NodeDocument.SelectionOrNull is { IsCollapsed: false } selection && selection.ContainsNode(target);
        }
    }

    internal void Up(PointerInput pointer, short button)
    {
        if (pointer.IsTouch)
        {
            TouchUp(pointer);
            return;
        }

        var bit = GetButtonBit(button);
        if ((pointer.Buttons & bit) == 0)
            return;

        pointer.Buttons &= ~bit;
        if (button == 0)
        {
            pointer.DragNode = null;
            if (pointer.DragOperation is { } drag)
            {
                // The drop: the pointer event stream was canceled when the drag started, so there is no pointerup, mouseup or click
                pointer.DragOperation = null;
                pointer.PressedDocument = null;
                pointer.DownTargets?.Remove(button);
                drag.End(GetDragState(pointer, drag.SourceNode));
                return;
            }
        }

        if (pointer.DragOperation is not null)
            return;

        var chorded = pointer.Buttons != 0;
        if (chorded && ShouldFirePointerRawUpdate(pointer))
        {
            FirePointerEvent(pointer, "pointerrawupdate", button, updateMouseOver: false, out _);
        }

        var target = FirePointerEvent(pointer, chorded ? "pointermove" : "pointerup", button, updateMouseOver: true, out _);
        var capturedTarget = pointer.CaptureTarget;
        var clickCount = pointer.ClickButton == button ? pointer.ClickCount : 1;
        var preventMouseEvent = pointer.PreventMouseEvent;
        if (!chorded)
        {
            // "Immediately after firing the pointerup or pointercancel events, the user agent MUST clear the pending pointer
            // capture target override for the pointerId, and then run process pending pointer capture steps to fire
            // lostpointercapture if necessary"
            pointer.PreventMouseEvent = false;
            pointer.PendingCaptureTarget = null;
            ProcessPendingPointerCapture(pointer);
        }

        if (target is not null && !preventMouseEvent)
        {
            FireMouseEvent(pointer, "mouseup", target, button, clickCount);
        }

        if (button == 0 && pointer.ScrollbarPress is { } scrollbarPress)
        {
            // The release of a press on a scrollbar is not a click
            pointer.ScrollbarPress = null;
            EndScrollbarPress(scrollbarPress);
            pointer.DownTargets?.Remove(button);
        }
        else
        {
            FireClick(pointer, button, target, capturedTarget, clickCount);
        }

        if (!chorded)
        {
            pointer.PressedDocument = null;

            // "if the pointer supports hover, user agent MUST also send corresponding boundary events necessary to reflect
            // the current position of the pointer with no capture"
            UpdateHover(pointer);
        }
    }

    internal void Cancel(PointerInput pointer)
    {
        if (pointer.IsTouch)
        {
            TouchCancel(pointer);
            return;
        }

        if (pointer.Buttons == 0)
            return;

        pointer.Buttons = 0;
        pointer.DownTargets?.Clear();
        pointer.DragNode = null;
        if (pointer.ScrollbarPress is { } scrollbarPress)
        {
            pointer.ScrollbarPress = null;
            EndScrollbarPress(scrollbarPress);
        }

        FirePointerEvent(pointer, "pointercancel", button: 0, updateMouseOver: false, out _);
        pointer.PressedDocument = null;
        pointer.PreventMouseEvent = false;
        pointer.PendingCaptureTarget = null;
        ProcessPendingPointerCapture(pointer);
        UpdateHover(pointer);
    }

    /// <summary>A pen becomes an active pointer (with its pointer id) when it is first used.</summary>
    private void Activate(PointerInput pointer)
    {
        if (pointer.IsActive)
            return;

        pointer.PointerId = AllocatePointerId();
        pointer.IsPrimary = true;
        pointer.IsActive = true;
    }

    /// <summary>The bit of a button in the buttons bit mask (https://w3c.github.io/uievents/#dom-mouseevent-buttons).</summary>
    private static int GetButtonBit(short button) => button switch
    {
        1 => 4,
        2 => 2,
        _ => 1 << button,
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Events
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Fires the pointer event of a pointer that hovers, preceded by <c>pointerrawupdate</c> for a move, and followed by its
    /// compatibility mouse event. Returns the target of the pointer event.
    /// </summary>
    private Node? FireHoverableEvent(PointerInput pointer, string type, string? mouseType, short button, int detail, out bool canceled)
    {
        if (type == "pointermove" && ShouldFirePointerRawUpdate(pointer))
        {
            // The pointer boundary events precede pointerrawupdate, and the mouse boundary events follow it
            FirePointerEvent(pointer, "pointerrawupdate", button, updateMouseOver: false, out _);
        }

        var target = FirePointerEvent(pointer, type, button, updateMouseOver: true, out canceled);
        if (target is not null && mouseType is not null && !(pointer.PreventMouseEvent && pointer.Buttons != 0))
        {
            FireMouseEvent(pointer, mouseType, target, button, detail);
        }

        return target;
    }

    /// <summary>
    /// The element at the position of a pointer. As in the browsers, while a button of a pointer that hovers is pressed in the
    /// document of a nested browsing context, its events are dispatched in that document (the mouse is captured by the
    /// frame): the document element is the target when the pointer is outside of the frame.
    /// </summary>
    private Element? HitTest(PointerInput pointer) => HitTest(pointer, out _);

    /// <summary>The element at the position of a pointer (see <see cref="HitTest(PointerInput)"/>), and the name of its pseudo-element whose box is hit, or null.</summary>
    private Element? HitTest(PointerInput pointer, out string? pseudoElement)
    {
        var hit = HitTest(pointer.X, pointer.Y, out pseudoElement);
        if (pointer.IsTouch || pointer.PressedDocument is not { IsFullyActive: true, BrowsingContext.Parent: not null } pressedDocument)
            return hit;

        for (Node? node = hit; node is not null; node = node.NodeDocument.BrowsingContext is { Parent: not null, Container: { } container } ? container : null)
        {
            if (ReferenceEquals(node.NodeDocument, pressedDocument))
                return hit;
        }

        var (x, y) = MapToDocument(pressedDocument, pointer.X, pointer.Y);
        return HitTestDocument(pressedDocument, x, y, out pseudoElement);
    }

    /// <summary>
    /// Returns the pseudo-element of an element whose box is hit, as the pseudoTarget of the events of the interaction
    /// (https://github.com/w3c/uievents/pull/413), or null when it is not represented by a <c>CSSPseudoElement</c>.
    /// </summary>
    private static CssPseudoElement? GetPseudoTarget(Node? target, string? pseudoElement)
        => pseudoElement is not null && target is Element element ? CssPseudoElement.FromBoxName(element, pseudoElement) : null;

    /// <summary>
    /// Returns the pseudoTarget of an event fired at a target after the hit testing: the pseudo-element hit, when the event is
    /// fired at its originating element and it still generates a box (as in the browsers, the pseudo-element is not the target
    /// anymore when the box tree built after the hit testing does not have it, e.g. when a listener hid it and forced a layout).
    /// </summary>
    private static CssPseudoElement? GetLivePseudoTarget(CssPseudoElement? pseudoTarget, Node target)
    {
        if (pseudoTarget is null || !ReferenceEquals(pseudoTarget.Element, target) || pseudoTarget.BoxName is not { } name)
            return null;

        return target.NodeDocument.LayoutEngineOrNull?.BoxTree?.GetPseudoElementBox(pseudoTarget.Element, name) is not null ? pseudoTarget : null;
    }

    /// <summary>
    /// Whether <c>pointerrawupdate</c> is fired before the <c>pointermove</c> event of a pointer
    /// (https://w3c.github.io/pointerevents/#the-pointerrawupdate-event): only in a secure context and, as in the browsers, only
    /// when an event listener can observe it (firing it also processes the pending pointer capture).
    /// </summary>
    private bool ShouldFirePointerRawUpdate(PointerInput pointer)
    {
        return HasListener(pointer.PendingCaptureTarget) || HasListener(pointer.CaptureTarget) || HasListener(GetPointerLockTarget(pointer) ?? HitTest(pointer));

        static bool HasListener(Node? target)
        {
            if (target is null || target.NodeDocument.DefaultView is not { IsSecureContext: true } window)
                return false;

            for (var node = target; node is not null; node = GetEventPathParent(node) ?? node.ParentNode)
            {
                if (node.HasEventListener("pointerrawupdate") || (node.ParentNode is ShadowRoot shadowRoot && shadowRoot.HasEventListener("pointerrawupdate")))
                    return true;
            }

            return window.HasEventListener("pointerrawupdate");
        }
    }

    /// <summary>
    /// Fires a pointer event (https://w3c.github.io/pointerevents/#firing-events-using-the-pointerevent-interface): the pending
    /// pointer capture is processed, the target is the pointer capture target override or the element at the position of the
    /// pointer, and the boundary events that move the pointer from its previous target to the target are fired before the
    /// event. Returns the target (its nearest connected ancestor when a listener removed it), or null when there is none.
    /// </summary>
    private Node? FirePointerEvent(PointerInput pointer, string type, short button, bool updateMouseOver, out bool canceled)
    {
        canceled = false;
        ProcessPendingPointerCapture(pointer);
        pointer.LockedEventTarget = null;
        pointer.PseudoTarget = null;
        if (GetPointerLockTarget(pointer) is { } lockTarget)
        {
            // https://w3c.github.io/pointerlock/: the pointer-lock target is the target of the events of the mouse. The
            // pointer does not move over the elements: no boundary event is fired.
            if (type is "pointerdown" or "pointermove" or "pointerup")
            {
                pointer.ActiveDocument = lockTarget.NodeDocument;
            }

            pointer.LockedEventTarget = lockTarget;
            canceled = !Dispatch(CreateEvent(pointer, type, lockTarget, button, relatedTarget: null, detail: 0), lockTarget);
            return lockTarget.IsConnected ? lockTarget : null;
        }

        string? pseudoElement = null;
        Node? target = pointer.CaptureTarget ?? HitTest(pointer, out pseudoElement);
        if (target is null)
            return null;

        var pseudoTarget = GetPseudoTarget(target, pseudoElement);
        SetPointerOver(pointer, target, button);
        if (updateMouseOver && pointer.IsPrimary)
        {
            SetMouseOver(pointer, pointer.PreviousTarget);
        }

        // A listener of the boundary events may have removed the target: the event is then fired at its nearest ancestor that
        // is connected
        if (!target.IsConnected)
        {
            target = pointer.PreviousTarget;
            if (target is null)
                return null;
        }

        // https://w3c.github.io/pointerevents/#implicit-pointer-capture: the direct manipulation devices capture the pointer
        // ("if the pointerdown event is fired for a direct manipulation device, set pointer capture for this pointerId to the target")
        if (type == "pointerdown" && pointer.IsTouch && target is Element element)
        {
            pointer.PendingCaptureTarget = element;
        }

        // "If the event is pointerdown, pointermove, or pointerup, set active document for the event's pointerId to targetDocument"
        if (type is "pointerdown" or "pointermove" or "pointerup")
        {
            pointer.ActiveDocument = target as Document ?? target.NodeDocument;
        }

        // https://github.com/w3c/uievents/pull/413: the pseudo-element hit is the pseudoTarget of the event (a listener of the
        // boundary events may have hidden it)
        pointer.PseudoTarget = GetLivePseudoTarget(pseudoTarget, target);
        var @event = CreateEvent(pointer, type, target, button, relatedTarget: null, detail: 0);
        @event.PseudoTargetValue = pointer.PseudoTarget;
        canceled = !Dispatch(@event, target);
        return pointer.PreviousTarget;
    }

    /// <summary>Fires a compatibility mouse event at the target of the pointer event (preceded by the mouse boundary events when the target changed).</summary>
    private void FireMouseEvent(PointerInput pointer, string type, Node target, short button, int detail)
    {
        if (!pointer.IsPrimary)
            return;

        // The compatibility mouse event of a pointer event fired at the pointer-lock target, without boundary event
        if (ReferenceEquals(pointer.LockedEventTarget, target))
        {
            Dispatch(CreateEvent(pointer, type, target, button, relatedTarget: null, detail), target);
            return;
        }

        SetMouseOver(pointer, target);
        if (_mouseOverTarget is not { } mouseTarget)
            return;

        var @event = CreateEvent(pointer, type, mouseTarget, button, relatedTarget: null, detail);
        @event.PseudoTargetValue = GetLivePseudoTarget(pointer.PseudoTarget, mouseTarget);
        Dispatch(@event, mouseTarget);
    }

    /// <summary>
    /// The click of the release of a button (https://w3c.github.io/uievents/#event-type-click): fired at the nearest common
    /// inclusive ancestor of the targets of the press and of the release (the capture target when the pointer was captured,
    /// https://w3c.github.io/pointerevents/#the-click-auxclick-and-contextmenu-events), followed by <c>dblclick</c> for the
    /// second click of the primary button, and by <c>contextmenu</c> for the secondary button (after the release, as on
    /// Windows).
    /// </summary>
    private void FireClick(PointerInput pointer, short button, Node? upTarget, Element? capturedTarget, int clickCount)
    {
        Node? downTarget = null;
        CssPseudoElement? downPseudoTarget = null;
        var pressed = pointer.DownTargets is { } downTargets && downTargets.Remove(button, out downTarget);
        pointer.DownPseudoTargets?.Remove(button, out downPseudoTarget);
        var upPseudoTarget = upTarget is null ? null : GetLivePseudoTarget(pointer.PseudoTarget, upTarget);
        if (pressed && upTarget is not null)
        {
            var clickTarget = capturedTarget is { IsConnected: true } ? capturedTarget : GetCommonAncestor(downTarget, upTarget);
            if (clickTarget is { IsConnected: true })
            {
                // The pseudoTarget of the click is the nearest common inclusive ancestor of the pseudo-elements hit by the press
                // and by the release, when the click is fired at their originating element
                var clickPseudoTarget = GetLivePseudoTarget(GetCommonAncestor(downPseudoTarget, upPseudoTarget), clickTarget);
                var click = CreateEvent(pointer, button == 0 ? "click" : "auxclick", clickTarget, button, relatedTarget: null, clickCount);
                click.PseudoTargetValue = clickPseudoTarget;
                Dispatch(click, clickTarget);
                if (button == 0 && clickCount == 2 && clickTarget.IsConnected)
                {
                    var doubleClick = CreateEvent(pointer, "dblclick", clickTarget, button, relatedTarget: null, clickCount);
                    doubleClick.PseudoTargetValue = GetLivePseudoTarget(clickPseudoTarget, clickTarget);
                    Dispatch(doubleClick, clickTarget);
                }
            }
        }

        if (button == 2 && upTarget is { IsConnected: true })
        {
            var contextMenu = CreateEvent(pointer, "contextmenu", upTarget, button, relatedTarget: null, detail: 0);
            contextMenu.PseudoTargetValue = GetLivePseudoTarget(upPseudoTarget, upTarget);
            Dispatch(contextMenu, upTarget);
        }
    }

    /// <summary>The nearest common inclusive ancestor of two pseudo-elements of the same element, or null.</summary>
    private static CssPseudoElement? GetCommonAncestor(CssPseudoElement? first, CssPseudoElement? second)
    {
        if (first is null || second is null || !ReferenceEquals(first.Element, second.Element))
            return null;

        for (var ancestor = first; ancestor is not null; ancestor = ancestor.ParentPseudoElement)
        {
            for (var other = second; other is not null; other = other.ParentPseudoElement)
            {
                if (ReferenceEquals(ancestor, other))
                    return ancestor;
            }
        }

        return null;
    }

    /// <summary>The nearest common inclusive ancestor of two nodes in the flat tree, or null.</summary>
    private static Node? GetCommonAncestor(Node? first, Node? second)
    {
        if (first is null || second is null)
            return null;

        for (var ancestor = first; ancestor is not null; ancestor = GetEventPathParent(ancestor))
        {
            for (var other = second; other is not null; other = GetEventPathParent(other))
            {
                if (ReferenceEquals(ancestor, other))
                    return ancestor;
            }
        }

        return null;
    }

    /// <summary>Sends the boundary events that reflect the position of a pointer that hovers, without other event (after the release of the capture, or when the layout changes under the pointer).</summary>
    private void UpdateHover(PointerInput pointer)
    {
        if (pointer.IsTouch || pointer.DragOperation is not null || GetPointerLockTarget(pointer) is not null)
            return;

        Node? target = pointer.CaptureTarget ?? HitTest(pointer);
        if (target is null)
            return;

        // The legacy mouse pointer follows the pointer when it changes of target (it may be elsewhere, after a tap)
        if (ReferenceEquals(pointer.PreviousTarget, target) && !pointer.NeedsOverEvent)
            return;

        SetPointerOver(pointer, target, button: -1);
        if (pointer.IsPrimary)
        {
            SetMouseOver(pointer, pointer.PreviousTarget);
        }
    }

    /// <summary>
    /// Fires the boundary events of the pointers that hover when the element under them changed without a move (the layout
    /// changed, the document scrolled), as the browsers do after a layout.
    /// </summary>
    internal void UpdateHoverAfterLayout()
    {
        if (!_hasHoveringPointer || !_browsingContext.Document.IsFullyActive)
            return;

        // Only when a document of the browsing context or of a nested browsing context was laid out or scrolled
        long version = GetLayoutVersion(_browsingContext.Document);
        foreach (var navigable in _browsingContext.GetDescendantNavigables())
        {
            version += GetLayoutVersion(navigable.Document);
        }

        if (version == _hoverLayoutVersion)
            return;

        _hoverLayoutVersion = version;
        foreach (var pointer in _pointers.ToArray())
        {
            // The target of a captured pointer does not depend on the layout (the pending pointer capture is processed by
            // the next pointer event)
            if (pointer.IsTouch || !pointer.IsActive || pointer.PreviousTarget is null || pointer.CaptureTarget is not null || pointer.PendingCaptureTarget is not null)
                continue;

            UpdateHover(pointer);
        }

        static long GetLayoutVersion(Document document) => document.LayoutEngineOrNull is { } engine ? engine.LayoutCount + (long)engine.ScrollVersion : 0;
    }

    /// <summary>Whether a pointer that hovers has a target (see <see cref="UpdateHoverAfterLayout"/>).</summary>
    private bool _hasHoveringPointer;
    private long _hoverLayoutVersion = -1;

    private static bool Dispatch(Event @event, Node target)
    {
        @event.IsTrusted = true;

        // The event is created in the realm of its target: its time stamp is relative to the time origin of the window of the
        // target (e.g. the window of a nested browsing context), not to the one of the current global object
        if (target.RelevantGlobal is { } global && !ReferenceEquals(global, GlobalScope.Current))
        {
            @event.InitializeTimeStamp(global);
        }

        return EventDispatcher.Dispatch(@event, target);
    }

    /// <summary>Creates a pointer event (pointer*, click, auxclick, contextmenu) or a mouse event (mouse*, dblclick) of a pointer.</summary>
    private MouseEvent CreateEvent(PointerInput pointer, string type, Node target, short button, EventTarget? relatedTarget, int detail)
    {
        var document = target as Document ?? target.NodeDocument;
        var (x, y) = MapToDocument(document, pointer.X, pointer.Y);
        var isPointerEvent = type.StartsWith("pointer", StringComparison.Ordinal) || type is "click" or "auxclick" or "contextmenu" or "gotpointercapture" or "lostpointercapture";
        var isEnterOrLeave = type is "pointerenter" or "pointerleave" or "mouseenter" or "mouseleave";
        var isMove = type is "pointermove" or "pointerrawupdate" or "mousemove";
        var isCancel = type == "pointercancel";
        var properties = pointer.Properties;
        var pressed = pointer.Buttons != 0;
        var modifiers = Modifiers;
        PointerEventInit CreateInit(bool bubbles, bool cancelable) => new()
        {
            Bubbles = bubbles,
            Cancelable = cancelable,
            // https://w3c.github.io/pointerevents/#attributes-and-default-actions: pointerenter and pointerleave are not composed
            Composed = !isEnterOrLeave,
            View = document.DefaultView,
            Detail = detail,
            // As in Blink, pointercancel has no position
            ClientX = isCancel ? 0 : x,
            ClientY = isCancel ? 0 : y,
            ScreenX = isCancel ? 0 : pointer.X + _browsingContext.ClientWindowLeft,
            ScreenY = isCancel ? 0 : pointer.Y + _browsingContext.ClientWindowTop,
            MovementX = isMove ? pointer.MovementX : 0,
            MovementY = isMove ? pointer.MovementY : 0,
            // The button of the mouse events that no button changed is 0 (https://w3c.github.io/uievents/#dom-mouseevent-button)
            Button = isPointerEvent ? button : Math.Max(button, (short)0),
            Buttons = (ushort)pointer.Buttons,
            ShiftKey = modifiers.HasFlag(InputModifiers.Shift),
            CtrlKey = modifiers.HasFlag(InputModifiers.Control),
            AltKey = modifiers.HasFlag(InputModifiers.Alt),
            MetaKey = modifiers.HasFlag(InputModifiers.Meta),
            RelatedTarget = relatedTarget,
            PointerId = pointer.PointerId,
            PointerType = pointer.PointerType,
            IsPrimary = pointer.IsPrimary,
            Width = properties.Width,
            Height = properties.Height,
            Pressure = (float)(pressed ? properties.Pressure ?? 0.5 : 0),
            TangentialPressure = (float)properties.TangentialPressure,
            TiltX = properties.TiltX,
            TiltY = properties.TiltY,
            Twist = properties.Twist,
            AltitudeAngle = properties.AltitudeAngle,
            AzimuthAngle = properties.AzimuthAngle,
            // As in Blink, the pointer events of the mouse have the persistent device id 1, and the click events 0
            PersistentDeviceId = pointer.IsMouse && isPointerEvent && type is not ("click" or "auxclick" or "contextmenu") ? 1 : 0,
        };

        var init = CreateInit(!isEnterOrLeave, !isEnterOrLeave && type is not ("pointercancel" or "pointerrawupdate" or "gotpointercapture" or "lostpointercapture"));
        if (!isPointerEvent)
            return new MouseEvent(type, init) { ScrollbarPress = type == "mousedown" && button == 0 ? pointer.ScrollbarPress : null, IsFromTouch = pointer.IsTouch };

        var @event = new PointerEvent(type, init);
        if (isMove)
        {
            // https://w3c.github.io/pointerevents/#coalesced-events: the coalesced events list of a trusted pointermove or
            // pointerrawupdate event has at least one event
            @event.SetCoalescedEvents([new PointerEvent(type, CreateInit(bubbles: false, cancelable: false)) { IsTrusted = true }]);
        }

        return @event;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Pointer lock
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The pointer-lock target (https://w3c.github.io/pointerlock/#dfn-pointer-lock-target) when the pointer is the mouse and is locked, else null.</summary>
    private Element? GetPointerLockTarget(PointerInput pointer) => pointer.IsMouse ? _browsingContext.PointerLockTarget : null;

    /// <summary>
    /// The pointer lock state is entered: the cursor position is the position of the mouse, and "the user agent MUST run the
    /// steps as if the releasePointerCapture() method has been called if any element is set to be captured or pending to be
    /// captured" (https://w3c.github.io/pointerevents/#pointer-lock).
    /// </summary>
    internal void OnPointerLocked()
    {
        Mouse.DeviceX = Mouse.X;
        Mouse.DeviceY = Mouse.Y;
        foreach (var pointer in _pointers)
        {
            pointer.PendingCaptureTarget = null;
        }
    }

    /// <summary>The pointer lock state is exited: "the system mouse cursor must be displayed again and positioned at cursor position".</summary>
    internal void OnPointerUnlocked()
    {
        Mouse.DeviceX = Mouse.X;
        Mouse.DeviceY = Mouse.Y;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Pointer capture
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://w3c.github.io/pointerevents/#process-pending-pointer-capture</summary>
    private void ProcessPendingPointerCapture(PointerInput pointer)
    {
        var current = pointer.CaptureTarget;

        // "When the pointer capture target override is no longer connected, the pending pointer capture target override and
        // pointer capture target override nodes SHOULD be cleared and also a PointerEvent named lostpointercapture [...] SHOULD
        // be fired at the document"
        if (current is not null && (!current.IsConnected || pointer.CaptureTargetRemoved))
        {
            pointer.CaptureTarget = null;
            pointer.CaptureTargetRemoved = false;
            if (ReferenceEquals(pointer.PendingCaptureTarget, current))
            {
                pointer.PendingCaptureTarget = null;
            }

            var document = current.NodeDocument;
            Dispatch(CreateEvent(pointer, "lostpointercapture", document, button: -1, relatedTarget: null, detail: 0), document);
            current = null;
        }

        if (pointer.PendingCaptureTarget is { IsConnected: false })
        {
            pointer.PendingCaptureTarget = null;
        }

        var pending = pointer.PendingCaptureTarget;
        if (ReferenceEquals(current, pending))
            return;

        pointer.CaptureTarget = pending;
        pointer.CaptureTargetRemoved = false;
        if (current is not null)
        {
            Dispatch(CreateEvent(pointer, "lostpointercapture", current, button: -1, relatedTarget: null, detail: 0), current);

            // A listener may have removed the new capture target
            if (pending is { IsConnected: false })
            {
                pointer.CaptureTarget = null;
                if (ReferenceEquals(pointer.PendingCaptureTarget, pending))
                {
                    pointer.PendingCaptureTarget = null;
                }

                return;
            }
        }

        if (pending is not null)
        {
            // The pointer moves over the capture target (the boundary events precede gotpointercapture, as in the browsers)
            SetPointerOver(pointer, pending, button: -1);
            if (pending.IsConnected)
            {
                Dispatch(CreateEvent(pointer, "gotpointercapture", pending, button: -1, relatedTarget: null, detail: 0), pending);
            }

            // A listener removed the capture target: lostpointercapture is fired at the document before the next pointer event
            if (pointer.CaptureTarget is { } removed && (!removed.IsConnected || pointer.CaptureTargetRemoved))
            {
                pointer.CaptureTarget = null;
                pointer.CaptureTargetRemoved = false;
                if (ReferenceEquals(pointer.PendingCaptureTarget, removed))
                {
                    pointer.PendingCaptureTarget = null;
                }

                var document = removed.NodeDocument;
                Dispatch(CreateEvent(pointer, "lostpointercapture", document, button: -1, relatedTarget: null, detail: 0), document);
            }
        }
    }

    /// <summary>https://w3c.github.io/pointerevents/#setting-pointer-capture</summary>
    internal static void SetPointerCapture(Element element, int pointerId)
    {
        var pointer = element.NodeDocument.BrowsingContext?.Top.InputOrNull?.GetActivePointer(pointerId);
        if (pointer is null)
            throw DomException.NotFound("The pointer id does not match any of the active pointers");

        if (!element.IsConnected)
            throw DomException.InvalidState("The element is not connected");

        // "If this's node document's pointerLockElement is set, throw an InvalidStateError DOMException"
        if (element.NodeDocument.BrowsingContext?.Top.PointerLockTarget is { } lockTarget && ReferenceEquals(lockTarget.NodeDocument, element.NodeDocument))
            throw DomException.InvalidState("The pointer is locked");

        // "If the pointer is not in the active buttons state or the element's node document is not the active document of the
        // pointer, then terminate these steps"
        if (pointer.Buttons == 0 || pointer.IsCanceled)
            return;

        // As in the browsers, an element of another document releases the pointer capture (e.g. the implicit capture of a touch)
        if (!ReferenceEquals(pointer.ActiveDocument, element.NodeDocument))
        {
            pointer.PendingCaptureTarget = null;
            return;
        }

        pointer.PendingCaptureTarget = element;
    }

    /// <summary>https://w3c.github.io/pointerevents/#releasing-pointer-capture</summary>
    internal static void ReleasePointerCapture(Element element, int pointerId)
    {
        var pointer = element.NodeDocument.BrowsingContext?.Top.InputOrNull?.GetActivePointer(pointerId);
        if (pointer is null)
            throw DomException.NotFound("The pointer id does not match any of the active pointers");

        if (ReferenceEquals(pointer.PendingCaptureTarget, element))
        {
            pointer.PendingCaptureTarget = null;
        }
    }

    /// <summary>https://w3c.github.io/pointerevents/#dom-element-haspointercapture</summary>
    internal static bool HasPointerCapture(Element element, int pointerId)
        => element.NodeDocument.BrowsingContext?.Top.InputOrNull?.GetActivePointer(pointerId) is { } pointer && ReferenceEquals(pointer.PendingCaptureTarget, element);

    // ----------------------------------------------------------------------------------------------------------------
    // Boundary events
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Moves a pointer over a target: "the user agent SHOULD treat the target as if the pointing device has moved over it from
    /// the previousTarget for the purpose of ensuring event ordering". The pointerout, pointerleave, pointerover and
    /// pointerenter events are fired, then the target is the previous target.
    /// </summary>
    private void SetPointerOver(PointerInput pointer, Node? target, short button)
    {
        var previous = pointer.PreviousTarget;
        var needsOverEvent = pointer.NeedsOverEvent;
        if (ReferenceEquals(previous, target) && !needsOverEvent)
            return;

        pointer.PreviousTarget = target;
        pointer.NeedsOverEvent = false;
        if (target is not null)
        {
            (target as Document ?? target.NodeDocument).HasPointerTargets = true;
            _hasHoveringPointer |= !pointer.IsTouch;
        }

        // https://html.spec.whatwg.org/multipage/speculative-loading.html#consider-speculative-loads: the user indicates
        // interest in the hyperlink under the pointer (the eagerness heuristics of the speculation rules)
        if (!pointer.IsTouch)
        {
            _browsingContext.Document.OnPointerOverForSpeculation(target);
        }

        SendBoundaryEvents(pointer, previous, needsOverEvent, target, mouse: false, button);
    }

    /// <summary>Moves the legacy mouse pointer over a target: the mouseout, mouseleave, mouseover and mouseenter events.</summary>
    private void SetMouseOver(PointerInput pointer, Node? target)
    {
        var previous = _mouseOverTarget;
        var needsOverEvent = _mouseNeedsOverEvent;
        if (ReferenceEquals(previous, target) && !needsOverEvent)
            return;

        _mouseOverTarget = target;
        _mouseNeedsOverEvent = false;
        if (target is not null)
        {
            (target as Document ?? target.NodeDocument).HasPointerTargets = true;
        }

        SendBoundaryEvents(pointer, previous, needsOverEvent, target, mouse: true, button: 0);
    }

    /// <summary>
    /// Fires the boundary events of a pointer that leaves <paramref name="previous"/> and enters <paramref name="target"/>
    /// (https://w3c.github.io/uievents/#events-mouseevent-event-order): out at the previous target (unless it is the ancestor of a
    /// removed previous target), leave at its inclusive ancestors that do not contain the target, over at the target, and enter at
    /// its inclusive ancestors that did not contain the previous target, from the outermost. The ancestors are the ones of the
    /// flat tree. In the documents of the ancestor browsing contexts, the pointer is over the container of the nested browsing
    /// context.
    /// </summary>
    private void SendBoundaryEvents(PointerInput pointer, Node? previous, bool previousRemoved, Node? target, bool mouse, short button)
    {
        var previousChain = GetFrameChain(previous);
        var targetChain = GetFrameChain(target);
        var i = previousChain.Count - 1;
        var j = targetChain.Count - 1;
        while (i >= 0 && j >= 0 && ReferenceEquals(previousChain[i], targetChain[j]))
        {
            i--;
            j--;
        }

        if (i < 0 && j < 0)
        {
            // The same target: a pointerover event is needed after the removal of the previous target
            if (target is { IsConnected: true })
            {
                FireBoundaryEvent(pointer, mouse ? "mouseover" : "pointerover", target, previous, button);
            }

            return;
        }

        // The documents the pointer leaves (the innermost first)
        for (var k = 0; k < i; k++)
        {
            Leave(previousChain[k], null, k == 0 && previousRemoved);
        }

        if (i >= 0 && j >= 0)
        {
            // In the same document
            Leave(previousChain[i], targetChain[j], i == 0 && previousRemoved);
            Enter(targetChain[j], previousChain[i]);
        }
        else if (i >= 0)
        {
            Leave(previousChain[i], null, i == 0 && previousRemoved);
        }
        else
        {
            Enter(targetChain[j], null);
        }

        for (var k = j - 1; k >= 0; k--)
        {
            Enter(targetChain[k], null);
        }

        void Leave(Node from, Node? to, bool removed)
        {
            var related = to;
            if (!removed && from.IsConnected)
            {
                FireBoundaryEvent(pointer, mouse ? "mouseout" : "pointerout", from, related, button);
            }

            var toAncestors = GetInclusiveAncestors(to);
            foreach (var ancestor in GetInclusiveAncestors(from))
            {
                if (toAncestors.Contains(ancestor))
                    break;

                if (ancestor.IsConnected)
                {
                    FireBoundaryEvent(pointer, mouse ? "mouseleave" : "pointerleave", ancestor, related, button);
                }
            }
        }

        void Enter(Node to, Node? from)
        {
            var related = from;
            var fromAncestors = GetInclusiveAncestors(from);
            var toAncestors = GetInclusiveAncestors(to);
            if (to.IsConnected)
            {
                FireBoundaryEvent(pointer, mouse ? "mouseover" : "pointerover", to, related, button);
            }

            for (var k = toAncestors.Count - 1; k >= 0; k--)
            {
                var ancestor = toAncestors[k];
                if (fromAncestors.Contains(ancestor) || !ancestor.IsConnected)
                    continue;

                FireBoundaryEvent(pointer, mouse ? "mouseenter" : "pointerenter", ancestor, related, button);
            }
        }
    }

    private void FireBoundaryEvent(PointerInput pointer, string type, Node target, Node? relatedTarget, short button)
        => Dispatch(CreateEvent(pointer, type, target, button, relatedTarget, detail: 0), target);

    /// <summary>A node and the containers of the browsing contexts of its document, from the innermost.</summary>
    private static List<Node> GetFrameChain(Node? node)
    {
        var result = new List<Node>();
        while (node is not null)
        {
            result.Add(node);
            node = (node as Document ?? node.NodeDocument).BrowsingContext is { Parent: not null, Container: { } container } ? container : null;
        }

        return result;
    }

    /// <summary>The inclusive ancestors of a node in the flat tree that are elements, from the innermost.</summary>
    private static List<Node> GetInclusiveAncestors(Node? node)
    {
        var result = new List<Node>();
        for (; node is not null; node = GetEventPathParent(node))
        {
            if (node is Element)
            {
                result.Add(node);
            }
        }

        return result;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Drag and drop
    // ----------------------------------------------------------------------------------------------------------------

    private DragPointerState GetDragState(PointerInput pointer, Node node)
    {
        var (x, y) = MapToDocument(node.NodeDocument, pointer.X, pointer.Y);
        return new DragPointerState(x, y, (ushort)pointer.Buttons);
    }

    /// <summary>
    /// A move with the primary button pressed begins a drag-and-drop operation on the pressed node
    /// (https://html.spec.whatwg.org/multipage/dnd.html#drag-and-drop-processing-model): the pointer event stream is then
    /// suppressed until the drop (https://w3c.github.io/pointerevents/#suppressing-a-pointer-event-stream).
    /// </summary>
    private void TryStartDrag(PointerInput pointer)
    {
        if (!pointer.IsMouse || pointer.DragNode is not { } dragNode || (pointer.X == pointer.DownX && pointer.Y == pointer.DownY) || GetPointerLockTarget(pointer) is not null)
            return;

        pointer.DragNode = null;
        if (DragAndDropOperation.TryStart(dragNode, GetDragState(pointer, dragNode), pointer.DragsSelection) is not { } operation)
            return;

        // The pointer capture is released, and the boundary events of the end of the stream were fired at the source node
        pointer.DragOperation = operation;
        pointer.PendingCaptureTarget = null;
        ProcessPendingPointerCapture(pointer);
        pointer.PreviousTarget = null;
        pointer.NeedsOverEvent = false;
        pointer.PreventMouseEvent = false;
        operation.Update(HitTest(pointer.X, pointer.Y), GetDragState(pointer, operation.SourceNode));
    }
}
