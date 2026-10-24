using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// The scrolling by the user: the wheel (https://w3c.github.io/uievents/#events-wheelevents) and the pan of a touch. The scroll
// is chained from the scroll container under the pointer to its ancestors (https://drafts.csswg.org/css-overscroll-1/#scroll-chaining),
// unless overscroll-behavior prevents it. The scrolls are performed by LayoutEngine.ScrollByUser (the animation, the snapping
// at the end of the gestures and the scrollend events).
public sealed partial class UserInput
{
    /// <summary>
    /// The time after the last <c>wheel</c> event of a wheel event transaction after which the transaction ends (user-agent
    /// defined: Gecko waits 1.5 s, Blink ends it with the scroll gesture).
    /// </summary>
    private static readonly TimeSpan WheelTransactionTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>The wheel event transaction in progress (https://w3c.github.io/uievents/#wheel-event-transactions), or null.</summary>
    private WheelTransaction? _wheelTransaction;

    /// <summary>A wheel event transaction: its target, the position of its wheel events, and the time of its last wheel event.</summary>
    private sealed class WheelTransaction(Element target, double x, double y, long timestamp)
    {
        public Element Target { get; } = target;

        public double X { get; } = x;

        public double Y { get; } = y;

        public long Timestamp { get; set; } = timestamp;

        /// <summary>Whether the target was removed from its document since the last wheel event (even if it was inserted again).</summary>
        public bool TargetRemoved { get; set; }
    }

    /// <summary>
    /// Rotates the wheel of the mouse at a position of the viewport of the top-level browsing context: a <c>wheel</c> event is
    /// fired at the element at the position (at the pointer-lock target while the mouse is locked; at the target of the wheel
    /// event transaction in progress, see below) and, unless it is canceled, the nearest scroll container that can scroll in the
    /// direction of the deltas (in CSS pixels) is scrolled, without snapping until the gesture of the wheel ends (at once, or
    /// 100 ms after the last wheel event when <see cref="ViewportOptions.SmoothUserScrolling"/> is set). Returns whether a
    /// scrolling box was scrolled.
    /// </summary>
    /// <remarks>
    /// The wheel events of a wheel event transaction (https://w3c.github.io/uievents/#wheel-event-transactions) are fired at the
    /// target of its first event, even when the scroll moves other contents under the pointer. A transaction ends when a wheel
    /// event is at another position (the mouse moved), 500 ms after its last wheel event (on the clock of the event loop), or when
    /// its target is removed from its document or stops generating a box.
    /// </remarks>
    public bool Wheel(double x, double y, double deltaX, double deltaY)
    {
        // https://w3c.github.io/pointerlock/: while the mouse is locked, the pointer-lock target is the target of the wheel
        // events, at the position of the pointer, and the scroll is chained from it
        var target = GetPointerLockTarget(Mouse);
        if (target is not null)
        {
            (x, y) = (Mouse.X, Mouse.Y);
            _wheelTransaction = null;
        }
        else if (_wheelTransaction is { } transaction && transaction.X == x && transaction.Y == y && !transaction.TargetRemoved
            && GetElapsed(transaction.Timestamp) < WheelTransactionTimeout && transaction.Target.IsConnected
            && transaction.Target.NodeDocument.IsFullyActive && ComputedStyleDeclarations.IsBeingRendered(transaction.Target))
        {
            target = transaction.Target;
            transaction.Timestamp = Now;
        }
        else
        {
            _wheelTransaction = null;
            target = HitTest(x, y);
            if (target is not null)
            {
                _wheelTransaction = new WheelTransaction(target, x, y, Now);
                target.NodeDocument.HasPointerTargets = true;
            }
        }

        if (target is null)
            return false;

        var document = target.NodeDocument;
        var (clientX, clientY) = MapToDocument(document, x, y);
        var modifiers = Modifiers;
        var @event = new WheelEvent("wheel", new WheelEventInit
        {
            Bubbles = true,
            // The legacy mousewheel event (see EventTarget.Invoke) is the same event
            Cancelable = HasNonPassiveListener(target, "wheel") || HasNonPassiveListener(target, "mousewheel"),
            Composed = true,
            View = document.DefaultView,
            ClientX = clientX,
            ClientY = clientY,
            ScreenX = x + _browsingContext.ClientWindowLeft,
            ScreenY = y + _browsingContext.ClientWindowTop,
            Buttons = (ushort)Mouse.Buttons,
            ShiftKey = modifiers.HasFlag(InputModifiers.Shift),
            CtrlKey = modifiers.HasFlag(InputModifiers.Control),
            AltKey = modifiers.HasFlag(InputModifiers.Alt),
            MetaKey = modifiers.HasFlag(InputModifiers.Meta),
            DeltaX = deltaX,
            DeltaY = deltaY,
            DeltaMode = WheelEvent.DomDeltaPixel,
        });

        if (!Dispatch(@event, target) || !target.IsConnected)
            return false;

        // The scroll containers generated by the pseudo-elements at the point are scrolled first (the event target is their
        // originating element)
        if (ScrollPseudoElementsAt(document, clientX, clientY, deltaX, deltaY))
            return true;

        return ScrollByUser(target, deltaX, deltaY, UserScrollUnit.Pixel, UserScrollKind.Wheel, out _, out _);
    }

    /// <summary>
    /// A node is removed: the wheel event transaction whose target it contains ends, as in Gecko (the target loses its box),
    /// even when the node is inserted again before the next wheel event.
    /// </summary>
    private void OnRemovingWheelTransactionTarget(Node node)
    {
        if (_wheelTransaction is { } transaction && node.IsShadowIncludingInclusiveAncestorOf(transaction.Target))
        {
            transaction.TargetRemoved = true;
        }
    }

    /// <summary>A move of the mouse ends the wheel event transaction.</summary>
    private void EndWheelTransaction() => _wheelTransaction = null;

    /// <summary>
    /// Whether an event listener that can cancel an event of the user that scrolls (<c>wheel</c>, <c>touchstart</c>,
    /// <c>touchmove</c>) is in the path of the event: as in the browsers, the event is not cancelable when all its listeners are
    /// passive, so that the scroll does not wait for them (https://dom.spec.whatwg.org/#observing-event-listeners).
    /// </summary>
    private static bool HasNonPassiveListener(Node target, string type)
    {
        for (var node = target; node is not null; node = GetEventPathParent(node))
        {
            if (HasListener(node) || (node.ParentNode is ShadowRoot shadowRoot && HasListener(shadowRoot)))
                return true;
        }

        return target.NodeDocument.DefaultView is { } window && HasListener(window);

        bool HasListener(EventTarget eventTarget)
        {
            foreach (var listener in eventTarget.EventListenerList)
            {
                if (listener.Type == type && listener.Passive != true)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The scroll of a key (as in the browsers): the arrow keys scroll by a line, <c>PageUp</c>, <c>PageDown</c> and the space
    /// key (with <c>Shift</c>: up) by a page, and <c>Home</c> and <c>End</c> to the start and the end of the scrolling box. The
    /// nearest scrolling box of <paramref name="start"/> that can scroll in the direction is scrolled. Returns whether the key
    /// is a scrolling key that scrolled a box.
    /// </summary>
    internal static bool ScrollByKey(Element start, KeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.CtrlKey || keyboardEvent.AltKey || keyboardEvent.MetaKey)
            return false;

        const double Line = 40;
        var (deltaX, deltaY, unit) = keyboardEvent.Key switch
        {
            "ArrowDown" => (0d, Line, UserScrollUnit.Pixel),
            "ArrowUp" => (0d, -Line, UserScrollUnit.Pixel),
            "ArrowRight" => (Line, 0d, UserScrollUnit.Pixel),
            "ArrowLeft" => (-Line, 0d, UserScrollUnit.Pixel),
            "PageDown" => (0d, 1d, UserScrollUnit.Page),
            "PageUp" => (0d, -1d, UserScrollUnit.Page),
            " " => (0d, keyboardEvent.ShiftKey ? -1d : 1d, UserScrollUnit.Page),
            "End" => (0d, 1d, UserScrollUnit.Document),
            "Home" => (0d, -1d, UserScrollUnit.Document),
            _ => (0d, 0d, UserScrollUnit.Pixel),
        };

        return (deltaX != 0 || deltaY != 0) && ScrollByUser(start, deltaX, deltaY, unit, UserScrollKind.Directional, out _, out _);
    }

    /// <summary>The unit of the deltas of a scroll of the user: CSS pixels, pages (a fraction of the scrollport), or the whole scrolling area.</summary>
    internal enum UserScrollUnit
    {
        Pixel,
        Page,
        Document,
    }

    /// <summary>
    /// Scrolls the scroll containers generated by pseudo-elements (e.g. a <c>::before</c> with <c>overflow: scroll</c>) that contain
    /// a point of the viewport of a document, from the innermost one, up to the first box of an element. Returns whether one of them
    /// scrolled.
    /// </summary>
    private static bool ScrollPseudoElementsAt(Document document, double clientX, double clientY, double deltaX, double deltaY)
    {
        if (document.LayoutEngineOrNull is not { } engine || HitTesting.HitTest(engine, new PhysicalOffset(clientX, clientY)) is not { } hit)
            return false;

        for (Fragment? fragment = hit.Fragment; fragment is not null; fragment = fragment.Parent)
        {
            // (the line boxes and the anonymous boxes are in the box of the pseudo-element)
            if (fragment is not BoxFragment { Box: var box } boxFragment)
                continue;

            if (box.PseudoElement is null && box.Element is not null)
                break;

            if (boxFragment.ScrollState is { PseudoElement: not null } state)
            {
                var (remainingX, remainingY) = (deltaX, deltaY);
                if (ScrollBox(document, state, ref remainingX, ref remainingY, UserScrollUnit.Pixel, UserScrollKind.Wheel))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scrolls the nearest scrolling box of an element (in the flat tree, then the viewport of its document, then the ones of the
    /// ancestor browsing contexts) that the user can scroll in the direction of the deltas. A scroll container that cannot
    /// scroll further chains the scroll to its ancestors unless its <c>overscroll-behavior</c> is not <c>auto</c>
    /// (https://drafts.csswg.org/css-overscroll-1/#overscroll-behavior-properties).
    /// </summary>
    private static bool ScrollByUser(Element element, double deltaX, double deltaY, UserScrollUnit unit, UserScrollKind kind, out ScrollState? scrolled, out Document? scrolledDocument)
    {
        scrolled = null;
        scrolledDocument = null;
        Node? node = element;
        while (node is not null && (deltaX != 0 || deltaY != 0))
        {
            var document = node as Document ?? node.NodeDocument;
            if (document.BrowsingContext is null || !document.IsFullyActive)
                return false;

            var engine = document.GetLayoutEngine();
            ScrollState? state = null;
            ComputedStyle? style = null;
            if (node is Element current)
            {
                if (ComputedStyleDeclarations.IsBeingRendered(current))
                {
                    state = engine.GetScrollState(current);
                    style = document.GetStyleEngine().GetStyle(current);
                }
            }
            else
            {
                // The viewport: its overscroll-behavior is the one of the root element
                engine.EnsureLayout();
                state = engine.ViewportScrollState;
                style = document.DocumentElement is { } root && ComputedStyleDeclarations.IsBeingRendered(root) ? document.GetStyleEngine().GetStyle(root) : null;
            }

            if (state is { Fragment: not null })
            {
                var (remainingX, remainingY) = (deltaX, deltaY);
                if (ScrollBox(document, state, ref remainingX, ref remainingY, unit, kind))
                {
                    scrolled = state;
                    scrolledDocument = document;
                    return true;
                }

                // The scroll is not chained in the axes whose overscroll-behavior is contain or none (the property applies
                // to the axes in which the box is a scroll container: not to an axis that is clipped)
                if (style is not null)
                {
                    if (ScrollContainers.IsScrollable(state.Overflow.X) && style.GetComputedValue("overscroll-behavior-x") is not ("auto" or "chain"))
                    {
                        deltaX = 0;
                    }

                    if (ScrollContainers.IsScrollable(state.Overflow.Y) && style.GetComputedValue("overscroll-behavior-y") is not ("auto" or "chain"))
                    {
                        deltaY = 0;
                    }
                }
            }

            node = node switch
            {
                Document { BrowsingContext: { Parent: not null, Container: { } container } } => container,
                Document => null,
                _ => GetEventPathParent(node) ?? document,
            };
        }

        return false;
    }

    /// <summary>
    /// Scrolls a scrolling box by the deltas in the axes the user can scroll (<c>overflow: auto</c> or <c>scroll</c>, with
    /// overflow) and that are not at their limit (from the destination of its smooth scroll in progress, except for a direct
    /// manipulation, which moves the contents under the pointer). A scroll snap container snaps (https://drafts.csswg.org/css-scroll-snap-1/#choosing),
    /// see <see cref="LayoutEngine.ScrollByUser"/>. Returns whether the box scrolled.
    /// </summary>
    private static bool ScrollBox(Document document, ScrollState state, ref double deltaX, ref double deltaY, UserScrollUnit unit, UserScrollKind kind)
    {
        var min = state.MinOffset;
        var max = state.MaxOffset;
        var offset = kind == UserScrollKind.DirectManipulation ? state.Offset : LayoutEngine.GetTargetScrollPosition(state);
        var canScrollX = deltaX != 0 && state.Overflow.X is OverflowKind.Auto or OverflowKind.Scroll && (deltaX > 0 ? offset.X < max.X : offset.X > min.X);
        var canScrollY = deltaY != 0 && state.Overflow.Y is OverflowKind.Auto or OverflowKind.Scroll && (deltaY > 0 ? offset.Y < max.Y : offset.Y > min.Y);
        if (!canScrollX && !canScrollY)
            return false;

        // A page is 87.5% of the scrollport, as in the browsers
        var (scaleX, scaleY) = unit switch
        {
            UserScrollUnit.Page => (state.Scrollport.Width * 0.875, state.Scrollport.Height * 0.875),
            UserScrollUnit.Document => (max.X - min.X, max.Y - min.Y),
            _ => (1d, 1d),
        };
        var delta = new PhysicalOffset(canScrollX ? deltaX * scaleX : 0, canScrollY ? deltaY * scaleY : 0);
        document.GetLayoutEngine().ScrollByUser(state, delta, kind);
        return true;
    }
}
