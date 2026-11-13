using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// The touch pointers (the fingers on a touch screen): a pointer per contact, the implicit pointer capture, the panning that
// touch-action allows (https://w3c.github.io/pointerevents/#the-touch-action-css-property), which cancels the pointer event
// stream and scrolls, and the compatibility mouse events of a tap
// (https://w3c.github.io/pointerevents/#mapping-for-devices-that-do-not-support-hover).
public sealed partial class UserInput
{
    /// <summary>The distance a touch moves from its start position before it pans or stops being a tap (the touch slop of the browsers).</summary>
    private const double TouchSlop = 15;

    [Flags]
    private enum TouchActions
    {
        None = 0,
        PanLeft = 1,
        PanRight = 2,
        PanUp = 4,
        PanDown = 8,
        PinchZoom = 16,
        PanX = PanLeft | PanRight,
        PanY = PanUp | PanDown,
        Manipulation = PanX | PanY | PinchZoom,
        DoubleTapZoom = 32,
        Auto = Manipulation | DoubleTapZoom,
    }

    private void TouchDown(PointerInput pointer)
    {
        if (pointer.IsActive)
            return;

        // https://w3c.github.io/pointerevents/#dfn-primary-pointer: the first contact when there is no other active touch pointer
        pointer.PointerId = AllocatePointerId();
        pointer.IsPrimary = !HasActiveTouch(pointer);
        pointer.IsActive = true;
        pointer.Buttons = 1;
        pointer.IsCanceled = false;
        pointer.HasMoved = false;
        pointer.PanDecided = false;
        pointer.PanScroller = null;
        pointer.PanDocument = null;
        pointer.PanScrolled = false;
        pointer.PreventTapEvents = false;
        pointer.PreventMouseEvent = false;
        pointer.PreviousTarget = null;
        pointer.NeedsOverEvent = false;
        pointer.DetachedPreviousTarget = null;
        pointer.EnteredAncestors = null;
        pointer.CaptureTarget = null;
        pointer.PendingCaptureTarget = null;
        pointer.DownX = pointer.X;
        pointer.DownY = pointer.Y;
        pointer.MovementX = 0;
        pointer.MovementY = 0;

        // The touch target is the element under the contact when it starts, and the direct manipulations that touch-action
        // allows are determined then (a change of touch-action during the contact has no effect)
        var touched = HitTest(pointer.X, pointer.Y);
        pointer.TouchTarget = touched;
        pointer.TouchActions = touched is not null && !IsOnScrollbar(touched, pointer.X, pointer.Y) ? (int)GetEffectiveTouchActions(touched) : (int)TouchActions.None;
        pointer.TouchIdentifier = AllocateTouchIdentifier(pointer);
        var target = FirePointerEvent(pointer, "pointerdown", button: 0, updateMouseOver: false, out var canceled);
        if (canceled)
        {
            pointer.PreventMouseEvent = true;
        }

        (pointer.DownTargets ??= [])[0] = target;
        if (!FireTouchEvent(pointer, "touchstart"))
        {
            // A canceled touchstart prevents the direct manipulations, the compatibility mouse events and the click of the contact
            pointer.PreventTapEvents = true;
            pointer.TouchActions = (int)TouchActions.None;
        }
    }

    private void TouchMove(PointerInput pointer, double oldX, double oldY)
    {
        // A touch pointer that is not in contact does not hover
        if (!pointer.IsActive)
            return;

        if (pointer.IsCanceled)
        {
            // The touch pans: the touch events are not cancelable anymore
            Pan(pointer, oldX - pointer.X, oldY - pointer.Y);
            FireTouchEvent(pointer, "touchmove", cancelable: false);
            return;
        }

        var deltaX = pointer.DownX - pointer.X;
        var deltaY = pointer.DownY - pointer.Y;
        if (!pointer.HasMoved && Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) > TouchSlop)
        {
            pointer.HasMoved = true;
        }

        if (ShouldFirePointerRawUpdate(pointer))
        {
            FirePointerEvent(pointer, "pointerrawupdate", button: -1, updateMouseOver: false, out _);
        }

        FirePointerEvent(pointer, "pointermove", button: -1, updateMouseOver: false, out _);
        pointer.MovementX = 0;
        pointer.MovementY = 0;
        var touchMoveCanceled = !FireTouchEvent(pointer, "touchmove");
        if (!pointer.HasMoved || pointer.PanDecided || !pointer.IsActive)
            return;

        // The first move out of the slop region decides of the direct manipulation: it starts unless touchmove was canceled
        pointer.PanDecided = true;
        if (touchMoveCanceled || !TryStartPan(pointer, deltaX, deltaY))
            return;

        SuppressPointerEventStream(pointer);

        // A pinch zoom uses the other touches too: their pointer event streams are suppressed
        if (GetOtherActiveTouch(pointer) is not null)
        {
            foreach (var other in _pointers.ToArray())
            {
                if (other.IsTouch && other.IsActive && !other.IsCanceled && !ReferenceEquals(other, pointer))
                {
                    other.PanDecided = true;
                    SuppressPointerEventStream(other);
                }
            }
        }

        Pan(pointer, deltaX, deltaY);
    }

    /// <summary>
    /// https://w3c.github.io/pointerevents/#suppressing-a-pointer-event-stream: the touch is used for a direct manipulation, so
    /// pointercancel, pointerout and pointerleave are fired and the capture is released.
    /// </summary>
    private void SuppressPointerEventStream(PointerInput pointer)
    {
        pointer.Buttons = 0;
        FirePointerEvent(pointer, "pointercancel", button: 0, updateMouseOver: false, out _);
        pointer.PendingCaptureTarget = null;
        ProcessPendingPointerCapture(pointer);
        SetPointerOver(pointer, null, button: 0);
        pointer.IsCanceled = true;
    }

    /// <summary>The first active touch pointer other than <paramref name="pointer"/>, or null.</summary>
    private PointerInput? GetOtherActiveTouch(PointerInput pointer)
    {
        foreach (var other in _pointers)
        {
            if (other.IsTouch && other.IsActive && !ReferenceEquals(other, pointer))
                return other;
        }

        return null;
    }

    private void TouchUp(PointerInput pointer)
    {
        if (!pointer.IsActive)
            return;

        if (pointer.IsCanceled)
        {
            pointer.IsActive = false;
            FireTouchEvent(pointer, "touchend", cancelable: false);
            EndTouch(pointer);
            EndVisualViewportGesture();
            return;
        }

        pointer.Buttons = 0;
        var target = FirePointerEvent(pointer, "pointerup", button: 0, updateMouseOver: false, out _);
        var capturedTarget = pointer.CaptureTarget;
        pointer.PendingCaptureTarget = null;
        ProcessPendingPointerCapture(pointer);

        // The pointer does not hover: it leaves its target
        SetPointerOver(pointer, null, button: 0);
        pointer.IsActive = false;
        if (!FireTouchEvent(pointer, "touchend"))
        {
            pointer.PreventTapEvents = true;
        }

        var isTap = !pointer.HasMoved && !pointer.PreventTapEvents;
        var preventMouseEvent = pointer.PreventMouseEvent;
        var isPrimary = pointer.IsPrimary;
        EndTouch(pointer);

        // The other touch of a pinch or of a pan may be released last, without having moved
        EndVisualViewportGesture();
        if (!isTap || target is null)
            return;

        // https://w3c.github.io/pointerevents/#mapping-for-devices-that-do-not-support-hover: the compatibility mouse events of
        // a tap are fired after pointerup, at the element under the tap, then the click
        Node? tapTarget = capturedTarget is { IsConnected: true } ? capturedTarget : HitTest(pointer.X, pointer.Y);
        if (tapTarget is null)
            return;

        // https://w3c.github.io/uievents/#current-click-count: a tap at the position and on the element of the previous tap is a
        // multiple click (the second one is followed by dblclick)
        if (pointer.ClickButton == 0 && ReferenceEquals(pointer.ClickTarget, tapTarget) && GetElapsed(pointer.ClickTimestamp) < MultipleClickTime
            && Math.Abs(pointer.ClickX - pointer.X) <= TouchSlop && Math.Abs(pointer.ClickY - pointer.Y) <= TouchSlop)
        {
            pointer.ClickCount++;
        }
        else
        {
            pointer.ClickButton = 0;
            pointer.ClickTarget = tapTarget;
            pointer.ClickCount = 1;
        }

        pointer.ClickX = pointer.X;
        pointer.ClickY = pointer.Y;
        pointer.ClickTimestamp = Now;
        var clickCount = pointer.ClickCount;
        if (isPrimary && !preventMouseEvent)
        {
            // Each event is fired at the element under the tap (a listener may have changed it)
            pointer.IsPrimary = true;
            if (HitTest(pointer.X, pointer.Y) is { } moveTarget)
            {
                FireMouseEvent(pointer, "mousemove", moveTarget, button: 0, detail: 0);
            }

            pointer.Buttons = 1;
            if (HitTest(pointer.X, pointer.Y) is { } downTarget)
            {
                FireMouseEvent(pointer, "mousedown", downTarget, button: 0, clickCount);
            }

            pointer.Buttons = 0;
            if (HitTest(pointer.X, pointer.Y) is { } upTarget)
            {
                FireMouseEvent(pointer, "mouseup", upTarget, button: 0, clickCount);
            }

            pointer.IsPrimary = isPrimary;
        }

        if (tapTarget.IsConnected)
        {
            Dispatch(CreateEvent(pointer, "click", tapTarget, button: 0, relatedTarget: null, clickCount), tapTarget);
            if (clickCount == 2 && tapTarget.IsConnected)
            {
                Dispatch(CreateEvent(pointer, "dblclick", tapTarget, button: 0, relatedTarget: null, clickCount), tapTarget);
            }
        }
    }

    private void TouchCancel(PointerInput pointer)
    {
        if (!pointer.IsActive)
            return;

        if (!pointer.IsCanceled)
        {
            pointer.Buttons = 0;
            FirePointerEvent(pointer, "pointercancel", button: 0, updateMouseOver: false, out _);
            pointer.PendingCaptureTarget = null;
            ProcessPendingPointerCapture(pointer);
            SetPointerOver(pointer, null, button: 0);
        }

        pointer.IsActive = false;
        FireTouchEvent(pointer, "touchcancel", cancelable: false);
        EndTouch(pointer);
        EndVisualViewportGesture();
    }

    /// <summary>The end of a contact: once no touch is in contact, the gesture that pinched or scrolled the visual viewport ends (its <c>scrollend</c> event).</summary>
    private void EndVisualViewportGesture()
    {
        if (GetOtherActiveTouch(Mouse) is null && _browsingContext.Document.VisualViewportOrNull is { } visualViewport)
        {
            visualViewport.EndUserGesture();
        }
    }

    /// <summary>The end of the contact: the pointer is removed from the active pointers, and the scroll of its pan ends (it snaps, then fires <c>scrollend</c>).</summary>
    private static void EndTouch(PointerInput pointer)
    {
        if (pointer.PanScroller is { } scroller && pointer.PanDocument is { IsFullyActive: true, BrowsingContext: not null } document)
        {
            document.GetLayoutEngine().ReleaseUserGesture(scroller);
        }

        pointer.IsActive = false;
        pointer.Buttons = 0;
        pointer.IsCanceled = false;
        pointer.PanScroller = null;
        pointer.PanDocument = null;
        pointer.CaptureTarget = null;
        pointer.PendingCaptureTarget = null;
        pointer.PreviousTarget = null;
        pointer.NeedsOverEvent = false;
        pointer.DetachedPreviousTarget = null;
        pointer.EnteredAncestors = null;
        pointer.PreventMouseEvent = false;
        pointer.DownTargets?.Clear();
        pointer.TouchTarget = null;
    }

    /// <summary>
    /// Whether the move of a touch from its start position begins a direct manipulation
    /// (https://w3c.github.io/pointerevents/#determining-supported-direct-manipulation-behavior): a pan in the direction of the
    /// move (its dominant axis) that <c>touch-action</c> allows, or a pinch zoom when another touch is in contact.
    /// As in the browsers, a touch that starts on a scrollbar operates the scrollbar (which is not emulated): it does not pan.
    /// </summary>
    private bool TryStartPan(PointerInput pointer, double deltaX, double deltaY)
    {
        var allowed = (TouchActions)pointer.TouchActions;

        // The pinch zoom of two touches is allowed by the touch-action of the elements of both touches
        if (GetOtherActiveTouch(pointer) is { } other)
            return (allowed & (TouchActions)other.TouchActions & TouchActions.PinchZoom) != TouchActions.None;

        var required = Math.Abs(deltaX) > Math.Abs(deltaY)
            ? (deltaX > 0 ? TouchActions.PanRight : TouchActions.PanLeft)
            : (deltaY > 0 ? TouchActions.PanDown : TouchActions.PanUp);
        return (allowed & required) != TouchActions.None;
    }

    /// <summary>
    /// The direct manipulations that the <c>touch-action</c> values of an element and of its ancestors in the flat tree allow: the
    /// panning along an axis is the intersection of the values up to the nearest ancestor scroll container that the user can
    /// scroll along that axis (<c>overflow: auto</c> or <c>scroll</c>), and the zoom the intersection up to the root.
    /// </summary>
    private static TouchActions GetEffectiveTouchActions(Element element)
    {
        var result = TouchActions.Auto;
        var mask = TouchActions.Auto;
        for (Node? node = element; node is not null && mask != TouchActions.None; node = GetEventPathParent(node))
        {
            if (node is not Element current || !ComputedStyleDeclarations.IsBeingRendered(current))
                continue;

            var document = current.NodeDocument;
            var style = document.GetStyleEngine().GetStyle(current);
            if (TouchActionApplies(current, style))
            {
                result &= ParseTouchAction(style.GetComputedValue("touch-action")) | ~mask;
            }

            // As in the browsers, a touch drags the thumb of a slider along its track instead of panning
            if (current is HtmlInputElement { IsSlider: true } && document.GetLayoutEngine().GetFragments(current) is [var sliderFragment, ..])
            {
                result &= ~(sliderFragment.WritingDirection.IsHorizontal ? TouchActions.PanX : TouchActions.PanY) | ~mask;
            }

            if (document.GetLayoutEngine().GetScrollState(current) is { } state)
            {
                if (state.Overflow.X is Css.Layout.OverflowKind.Auto or Css.Layout.OverflowKind.Scroll)
                {
                    mask &= ~TouchActions.PanX;
                }

                if (state.Overflow.Y is Css.Layout.OverflowKind.Auto or Css.Layout.OverflowKind.Scroll)
                {
                    mask &= ~TouchActions.PanY;
                }
            }
        }

        return result;
    }

    /// <summary>"Applies to: all elements except: non-replaced inline elements, table rows, row groups, table columns, and column groups".</summary>
    private static bool TouchActionApplies(Element element, ComputedStyle style)
    {
        var display = style.GetComputedValue("display");
        if (display is "table-row" or "table-row-group" or "table-header-group" or "table-footer-group" or "table-column" or "table-column-group")
            return false;

        return element.NodeDocument.GetLayoutEngine().GetFragments(element) is not [{ Box.IsInlineBox: true }, ..];
    }

    private static TouchActions ParseTouchAction(string value)
    {
        var result = TouchActions.None;
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            result |= token switch
            {
                "auto" => TouchActions.Auto,
                "manipulation" => TouchActions.Manipulation,
                "pan-x" => TouchActions.PanX,
                "pan-y" => TouchActions.PanY,
                "pan-left" => TouchActions.PanLeft,
                "pan-right" => TouchActions.PanRight,
                "pan-up" => TouchActions.PanUp,
                "pan-down" => TouchActions.PanDown,
                "pinch-zoom" => TouchActions.PinchZoom,
                _ => TouchActions.None,
            };
        }

        return result;
    }

    /// <summary>
    /// Scrolls by the move of a panning touch (a direct manipulation: not snapped until the touch ends, see
    /// <see cref="LayoutEngine.ScrollByUser"/>): the scrolling box is latched when the pan starts, and only the axes that
    /// <c>touch-action</c> allows scroll. While another touch is in contact, the touches pinch instead (see <see cref="PinchTouches"/>).
    /// </summary>
    private void Pan(PointerInput pointer, double deltaX, double deltaY)
    {
        if (pointer.DownTargets is null || !pointer.DownTargets.TryGetValue(0, out var node) || node is not Element element)
            return;

        var allowed = (TouchActions)pointer.TouchActions;
        if (GetOtherActiveTouch(pointer) is { } other)
        {
            if ((allowed & TouchActions.PinchZoom) != TouchActions.None && ((TouchActions)other.TouchActions & TouchActions.PinchZoom) != TouchActions.None)
            {
                PinchTouches(new PhysicalOffset(pointer.X + deltaX, pointer.Y + deltaY), new PhysicalOffset(pointer.X, pointer.Y), new PhysicalOffset(other.X, other.Y));
            }

            return;
        }

        if ((allowed & (deltaX > 0 ? TouchActions.PanRight : TouchActions.PanLeft)) == TouchActions.None)
        {
            deltaX = 0;
        }

        if ((allowed & (deltaY > 0 ? TouchActions.PanDown : TouchActions.PanUp)) == TouchActions.None)
        {
            deltaY = 0;
        }

        if (deltaX == 0 && deltaY == 0)
            return;

        if (pointer.PanScroller is { Fragment: not null } scroller && pointer.PanDocument is { IsFullyActive: true } document)
        {
            ScrollBoxOrVisualViewport(document, scroller, ref deltaX, ref deltaY, UserScrollUnit.Pixel, UserScrollKind.DirectManipulation);
            return;
        }

        if (!pointer.PanScrolled && element.IsConnected && ScrollByUser(element, deltaX, deltaY, UserScrollUnit.Pixel, UserScrollKind.DirectManipulation, out var latched, out var latchedDocument))
        {
            pointer.PanScrolled = true;
            pointer.PanScroller = latched;
            pointer.PanDocument = latchedDocument;
        }
    }

    /// <summary>
    /// The move of a touch while another touch is in contact (a pinch): the visual viewport of the top-level document is zoomed
    /// by the ratio of the distances between the touches after and before the move, and the point that was under the middle of
    /// the touches moves under their new middle.
    /// </summary>
    /// <param name="oldPosition">The position of the touch that moved, before the move.</param>
    /// <param name="newPosition">The position of the touch that moved, after the move.</param>
    /// <param name="otherPosition">The position of the other touch.</param>
    private void PinchTouches(PhysicalOffset oldPosition, PhysicalOffset newPosition, PhysicalOffset otherPosition)
    {
        var oldDistance = Distance(oldPosition, otherPosition);
        var newDistance = Distance(newPosition, otherPosition);
        if (oldDistance <= 0 || newDistance <= 0 || !_browsingContext.Document.IsFullyActive)
            return;

        var oldCenter = new PhysicalOffset((oldPosition.X + otherPosition.X) / 2, (oldPosition.Y + otherPosition.Y) / 2);
        var newCenter = new PhysicalOffset((newPosition.X + otherPosition.X) / 2, (newPosition.Y + otherPosition.Y) / 2);
        _browsingContext.Document.VisualViewport.ZoomBy(newDistance / oldDistance, oldCenter, newCenter, endsGesture: false);

        static double Distance(PhysicalOffset a, PhysicalOffset b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
    }

    /// <summary>The identifier of a new touch point (https://w3c.github.io/touch-events/#dom-touch-identifier): the smallest one that no active touch point uses.</summary>
    private int AllocateTouchIdentifier(PointerInput pointer)
    {
        for (var identifier = 0; ; identifier++)
        {
            var used = false;
            foreach (var other in _pointers)
            {
                used |= other.IsTouch && other.IsActive && !ReferenceEquals(other, pointer) && other.TouchIdentifier == identifier;
            }

            if (!used)
                return identifier;
        }
    }

    /// <summary>
    /// Fires a touch event (https://w3c.github.io/touch-events/#touchevent-interface) for a change of a touch point, at the
    /// element the touch started on: <c>touches</c> are the touch points in contact, <c>targetTouches</c> the ones that started
    /// on the same target, and <c>changedTouches</c> the touch point. Returns false when the event is canceled.
    /// </summary>
    private bool FireTouchEvent(PointerInput pointer, string type, bool cancelable = true)
    {
        if (pointer.TouchTarget is not { } target || !_browsingContext.Options.EffectiveFeatures.TouchEventsEnabled)
            return true;

        var document = target.NodeDocument;
        var changed = CreateTouch(pointer);
        var touches = new List<Touch>();
        var targetTouches = new List<Touch>();
        foreach (var other in _pointers)
        {
            if (!other.IsTouch || !other.IsActive || other.TouchTarget is null)
                continue;

            var touch = ReferenceEquals(other, pointer) ? changed : CreateTouch(other);
            touches.Add(touch);
            if (ReferenceEquals(other.TouchTarget, target))
            {
                targetTouches.Add(touch);
            }
        }

        var modifiers = Modifiers;
        var @event = new TouchEvent(type, new TouchEventInit
        {
            Bubbles = true,
            Cancelable = cancelable && type != "touchcancel" && (type == "touchend" || HasNonPassiveListener(target, type)),
            Composed = true,
            View = document.DefaultView,
            Touches = touches,
            TargetTouches = targetTouches,
            ChangedTouches = [changed],
            ShiftKey = modifiers.HasFlag(InputModifiers.Shift),
            CtrlKey = modifiers.HasFlag(InputModifiers.Control),
            AltKey = modifiers.HasFlag(InputModifiers.Alt),
            MetaKey = modifiers.HasFlag(InputModifiers.Meta),
        });

        return Dispatch(@event, target);
    }

    private Touch CreateTouch(PointerInput pointer)
    {
        var target = pointer.TouchTarget!;
        var document = target.NodeDocument;
        var (x, y) = MapToDocument(document, pointer.X, pointer.Y);
        var window = document.DefaultView;
        var properties = pointer.Properties;
        return new Touch(new TouchInit
        {
            Identifier = pointer.TouchIdentifier,
            Target = target,
            ClientX = x,
            ClientY = y,
            ScreenX = pointer.X + _browsingContext.ClientWindowLeft,
            ScreenY = pointer.Y + _browsingContext.ClientWindowTop,
            PageX = x + (window?.ScrollX ?? 0),
            PageY = y + (window?.ScrollY ?? 0),
            RadiusX = (float)(properties.Width / 2),
            RadiusY = (float)(properties.Height / 2),
            Force = (float)(properties.Pressure ?? 0.5),
        });
    }
}
