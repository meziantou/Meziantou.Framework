using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The modifier keys that are pressed while a pointer or wheel input is processed.</summary>
[Flags]
public enum InputModifiers
{
    /// <summary>No modifier key.</summary>
    None = 0,

    /// <summary>The Shift key.</summary>
    Shift = 1,

    /// <summary>The Control key.</summary>
    Control = 2,

    /// <summary>The Alt key.</summary>
    Alt = 4,

    /// <summary>The Meta key.</summary>
    Meta = 8,
}

/// <summary>
/// The properties of the contact of a pointer (https://w3c.github.io/pointerevents/#pointerevent-interface): its geometry,
/// its pressure and the orientation of its transducer.
/// </summary>
public sealed record PointerInputProperties
{
    /// <summary>Gets the width, in CSS pixels, of the contact geometry.</summary>
    public double Width { get; init; } = 1;

    /// <summary>Gets the height, in CSS pixels, of the contact geometry.</summary>
    public double Height { get; init; } = 1;

    /// <summary>Gets the normalized pressure, or null for the default one (0.5 while a button is pressed or the pointer is in contact, else 0).</summary>
    public double? Pressure { get; init; }

    /// <summary>Gets the normalized tangential pressure, between -1 and 1.</summary>
    public double TangentialPressure { get; init; }

    /// <summary>Gets the tilt of the transducer along the X axis, in degrees, or null to compute it from the spherical angles.</summary>
    public int? TiltX { get; init; }

    /// <summary>Gets the tilt of the transducer along the Y axis, in degrees, or null to compute it from the spherical angles.</summary>
    public int? TiltY { get; init; }

    /// <summary>Gets the clockwise rotation of the transducer around its axis, in degrees.</summary>
    public int Twist { get; init; }

    /// <summary>Gets the altitude of the transducer, in radians, or null to compute it from the tilt.</summary>
    public double? AltitudeAngle { get; init; }

    /// <summary>Gets the azimuth of the transducer, in radians, or null to compute it from the tilt.</summary>
    public double? AzimuthAngle { get; init; }

    internal static PointerInputProperties Default { get; } = new();
}

/// <summary>
/// The input devices of the user of a top-level browsing context (see <see cref="BrowsingContext.Input"/>): a host
/// application injects the input of the user (the moves, the presses and the releases of the pointers, the rotations of
/// the wheel), and the library dispatches the trusted events of the Pointer Events (https://w3c.github.io/pointerevents/)
/// and UI Events (https://w3c.github.io/uievents/) specifications to the documents of the browsing context and of its
/// nested browsing contexts, and runs the default actions of the user agent (the focus, the activation, the drag and drop,
/// the scrolling). The positions are in CSS pixels, relative to the viewport of the top-level browsing context.
/// </summary>
public sealed partial class UserInput
{
    private readonly BrowsingContext _browsingContext;
    private readonly List<PointerInput> _pointers = [];
    private int _nextPointerId = 2;

    internal UserInput(BrowsingContext browsingContext)
    {
        _browsingContext = browsingContext;
        Mouse = new PointerInput(this, "mouse") { PointerId = 1, IsPrimary = true, IsActive = true };
        _pointers.Add(Mouse);
    }

    /// <summary>Gets the mouse of the user agent (its pointer always exists, https://w3c.github.io/pointerevents/#dfn-active-pointer).</summary>
    public PointerInput Mouse { get; }

    /// <summary>Gets or sets the modifier keys that are pressed (the <c>shiftKey</c>, <c>ctrlKey</c>, <c>altKey</c> and <c>metaKey</c> of the events).</summary>
    public InputModifiers Modifiers { get; set; }

    /// <summary>
    /// Creates a pointer of the given type: <c>touch</c> (a finger: a new pointer of the touch screen for each contact),
    /// <c>pen</c> (a stylus that hovers) or <c>mouse</c> (returns <see cref="Mouse"/>: there is a single mouse).
    /// </summary>
    public PointerInput CreatePointer(string pointerType)
    {
        ArgumentNullException.ThrowIfNull(pointerType);
        if (pointerType == "mouse")
            return Mouse;

        if (pointerType is not ("touch" or "pen"))
            throw new ArgumentException("The pointer type must be mouse, pen or touch", nameof(pointerType));

        var pointer = new PointerInput(this, pointerType);
        _pointers.Add(pointer);
        return pointer;
    }

    internal BrowsingContext BrowsingContext => _browsingContext;

    /// <summary>The active pointer (https://w3c.github.io/pointerevents/#dfn-active-pointer) with the given id, or null.</summary>
    internal PointerInput? GetActivePointer(int pointerId)
    {
        foreach (var pointer in _pointers)
        {
            if (pointer.IsActive && pointer.PointerId == pointerId)
                return pointer;
        }

        return null;
    }

    private int AllocatePointerId() => _nextPointerId++;

    /// <summary>Ends the multiple clicks in progress: the next press of a button is a single click.</summary>
    internal void ResetClickCounts()
    {
        foreach (var pointer in _pointers)
        {
            pointer.ClickButton = -1;
            pointer.ClickCount = 0;
        }
    }

    private bool HasActiveTouch(PointerInput except)
    {
        foreach (var pointer in _pointers)
        {
            if (pointer.IsTouch && pointer.IsActive && !ReferenceEquals(pointer, except))
                return true;
        }

        return false;
    }

    private long Now => _browsingContext.EventLoop.TimeProvider.GetTimestamp();

    private TimeSpan GetElapsed(long since) => _browsingContext.EventLoop.TimeProvider.GetElapsedTime(since);

    // ----------------------------------------------------------------------------------------------------------------
    // Hit testing and coordinates
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the element at a point of the viewport of the top-level browsing context, as the hit testing finds it
    /// (https://drafts.csswg.org/cssom-view/#hit-testing): not retargeted out of the shadow trees, and in the document of
    /// the nested browsing context when the point is in the content box of its container. The point of an empty area hits
    /// the document element. It returns null when the documents are not laid out (e.g. the document is not fully active).
    /// </summary>
    /// <param name="x">The horizontal position, in CSS pixels, relative to the viewport of the top-level browsing context.</param>
    /// <param name="y">The vertical position, in CSS pixels, relative to the viewport of the top-level browsing context.</param>
    public Element? HitTest(double x, double y) => HitTest(x, y, out _);

    /// <summary>
    /// Returns the element at a point of the viewport of the top-level browsing context (see <see cref="HitTest(double, double)"/>),
    /// and the name of the pseudo-element of the element whose box is hit (see <see cref="Css.Layout.LayoutBox.PseudoElement"/>), or null.
    /// </summary>
    internal Element? HitTest(double x, double y, out string? pseudoElement)
    {
        pseudoElement = null;
        var document = _browsingContext.Document;
        while (true)
        {
            if (!document.IsFullyActive)
                return null;

            var element = HitTestDocument(document, x, y, out pseudoElement);
            if (element is null)
                return null;

            if (NavigableContainers.GetContentNavigable(element) is not { IsDestroyed: false } child || child.Document.DocumentElement is null
                || !TryMapToFrame(element, x, y, out var frameX, out var frameY, out var inContentBox) || !inContentBox)
            {
                return element;
            }

            document = child.Document;
            (x, y) = (frameX, frameY);
        }
    }

    /// <summary>The element at a point of the viewport of a document (the document element for an empty area or a point outside of the viewport).</summary>
    internal static Element? HitTestDocument(Document document, double x, double y) => HitTestDocument(document, x, y, out _);

    /// <summary>
    /// The element at a point of the viewport of a document (see <see cref="HitTestDocument(Document, double, double)"/>), and the
    /// name of the pseudo-element of the element whose box is hit, or null.
    /// </summary>
    internal static Element? HitTestDocument(Document document, double x, double y, out string? pseudoElement)
    {
        pseudoElement = null;
        if (document.BrowsingContext is null)
            return null;

        if (CssomViewGeometry.IsInViewport(document, x, y) && HitTesting.HitTest(document.GetLayoutEngine(), new PhysicalOffset(x, y)) is { } hit)
        {
            if (hit.Target is null)
            {
                pseudoElement = hit.Fragment.Box?.PseudoElement;
            }

            return hit.Element;
        }

        return document.DocumentElement;
    }

    /// <summary>Whether a point of the viewport of the top-level browsing context is on a scrollbar of the scroll container <paramref name="element"/> (between its border and its padding box).</summary>
    private bool IsOnScrollbar(Element element, double x, double y)
    {
        var document = element.NodeDocument;
        if (document.BrowsingContext is null)
            return false;

        (x, y) = MapToDocument(document, x, y);
        if (!CssomViewGeometry.IsInViewport(document, x, y) || HitTesting.HitTest(document.GetLayoutEngine(), new PhysicalOffset(x, y)) is not { Fragment: BoxFragment fragment } hit || !ReferenceEquals(hit.Element, element))
            return false;

        var scrollbars = fragment.Scrollbars;
        if (scrollbars.Left == 0 && scrollbars.Right == 0 && scrollbars.Top == 0 && scrollbars.Bottom == 0)
            return false;

        var point = hit.LocalPoint;
        var inner = fragment.BorderBoxRect.Contract(fragment.Border);
        var paddingBox = fragment.PaddingBoxRect;
        return IsInside(inner) && !IsInside(paddingBox);

        bool IsInside(PhysicalRect rect) => point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
    }

    /// <summary>Maps a point of the viewport of the document of a navigable container to the viewport of its content document.</summary>
    private static bool TryMapToFrame(Element container, double x, double y, out double frameX, out double frameY, out bool inContentBox)
    {
        frameX = x;
        frameY = y;
        inContentBox = false;
        if (CssomViewGeometry.GetLayoutEngine(container) is not { } engine || engine.GetFragments(container) is not [var fragment, ..])
            return false;

        if (!LayoutEngine.TryMapFromRoot(fragment, engine.FromViewport(new PhysicalOffset(x, y)), out var local))
            return false;

        var contentBox = fragment.ContentBoxRect;
        inContentBox = local.X >= contentBox.Left && local.X < contentBox.Right && local.Y >= contentBox.Top && local.Y < contentBox.Bottom;
        frameX = local.X - contentBox.Offset.X;
        frameY = local.Y - contentBox.Offset.Y;
        return true;
    }

    /// <summary>
    /// Maps a point of the viewport of the top-level browsing context to the viewport of a document of a nested browsing
    /// context (the document of an <c>iframe</c>), through the transforms and the scroll offsets of the containers.
    /// </summary>
    /// <param name="document">A document of the top-level browsing context or of a nested browsing context.</param>
    /// <param name="x">The horizontal position, in CSS pixels, relative to the viewport of the top-level browsing context.</param>
    /// <param name="y">The vertical position, in CSS pixels, relative to the viewport of the top-level browsing context.</param>
    public (double X, double Y) MapToDocument(Document document, double x, double y)
    {
        List<Element>? containers = null;
        for (var current = document; current.BrowsingContext is { Parent: not null, Container: { } container }; current = container.NodeDocument)
        {
            (containers ??= []).Add(container);
        }

        if (containers is not null)
        {
            for (var i = containers.Count - 1; i >= 0; i--)
            {
                if (TryMapToFrame(containers[i], x, y, out var frameX, out var frameY, out _))
                {
                    (x, y) = (frameX, frameY);
                }
            }
        }

        return (x, y);
    }

    /// <summary>
    /// Maps a point of the viewport of a document (e.g. the coordinates of <see cref="Element.GetBoundingClientRect"/> in the
    /// document of an <c>iframe</c>) to the viewport of the top-level browsing context, through the transforms and the scroll
    /// offsets of the containers: the coordinates of the input of the user (see <see cref="PointerInput.Move"/>).
    /// </summary>
    /// <param name="document">A document of the top-level browsing context or of a nested browsing context.</param>
    /// <param name="x">The horizontal position, in CSS pixels, relative to the viewport of the document.</param>
    /// <param name="y">The vertical position, in CSS pixels, relative to the viewport of the document.</param>
    public static (double X, double Y) MapToTopLevel(Document document, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(document);
        var point = new PhysicalOffset(x, y);
        for (var current = document; current.BrowsingContext is { Parent: not null, Container: { } container }; current = container.NodeDocument)
        {
            if (CssomViewGeometry.GetLayoutEngine(container) is not { } engine || engine.GetFragments(container) is not [var fragment, ..])
                break;

            point = engine.ToViewport(LayoutEngine.MapToRoot(fragment, fragment.ContentBoxRect.Offset + point));
        }

        return (point.X, point.Y);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Node removal
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The steps run before a node is removed from a document some pointer targets (see <see cref="Document.HasPointerTargets"/>):
    /// "If the previousTarget at any point will no longer be connected, update the previousTarget to the nearest still connected
    /// parent following the event path corresponding to dispatching events to the previousTarget, and set the needsOverEvent
    /// flag to true" (https://w3c.github.io/pointerevents/#firing-events-using-the-pointerevent-interface). So are the targets
    /// of the presses of the buttons (the click is fired at their nearest connected ancestor, as in the browsers), and the target
    /// of the wheel event transaction.
    /// </summary>
    internal void OnRemoving(Node node)
    {
        var parent = GetEventPathParent(node);
        foreach (var pointer in _pointers)
        {
            if (pointer.PreviousTarget is { } previous && node.IsShadowIncludingInclusiveAncestorOf(previous))
            {
                pointer.PreviousTarget = parent;
                pointer.NeedsOverEvent = true;
            }

            // The capture is lost when the capture target is removed, even when it is inserted again before the next event
            if (pointer.CaptureTarget is { } capture && node.IsShadowIncludingInclusiveAncestorOf(capture))
            {
                pointer.CaptureTargetRemoved = true;
            }

            if (pointer.PendingCaptureTarget is { } pending && !ReferenceEquals(pending, pointer.CaptureTarget) && node.IsShadowIncludingInclusiveAncestorOf(pending))
            {
                pointer.PendingCaptureTarget = null;
            }

            if (pointer.DownTargets is { Count: > 0 } downTargets)
            {
                foreach (var (button, target) in downTargets.ToArray())
                {
                    if (target is not null && node.IsShadowIncludingInclusiveAncestorOf(target))
                    {
                        downTargets[button] = parent;
                    }
                }
            }
        }

        if (_mouseOverTarget is { } mouseOver && node.IsShadowIncludingInclusiveAncestorOf(mouseOver))
        {
            _mouseOverTarget = parent;
            _mouseNeedsOverEvent = true;
        }

        OnRemovingWheelTransactionTarget(node);
    }

    /// <summary>The parent of a node in the path of the events dispatched to it: its assigned slot, its parent, or the host of its shadow root.</summary>
    private static Node? GetEventPathParent(Node node) => HtmlElement.GetFlatTreeParent(node) ?? (node.ParentNode is ShadowRoot shadowRoot ? shadowRoot.Host : node.ParentNode);
}

/// <summary>
/// A pointer of an input device of the user (https://w3c.github.io/pointerevents/#dfn-pointer): the mouse, a pen, or a
/// finger on a touch screen. See <see cref="UserInput"/>.
/// </summary>
public sealed class PointerInput
{
    private readonly UserInput _input;

    internal PointerInput(UserInput input, string pointerType)
    {
        _input = input;
        PointerType = pointerType;
    }

    /// <summary>Gets the type of the device: <c>mouse</c>, <c>pen</c> or <c>touch</c>.</summary>
    public string PointerType { get; }

    /// <summary>
    /// Gets the identifier of the pointer (https://w3c.github.io/pointerevents/#dom-pointerevent-pointerid): 1 for the mouse;
    /// a touch pointer gets a new identifier for each contact, and a pen when it is first used.
    /// </summary>
    public int PointerId { get; internal set; }

    /// <summary>Gets a value indicating whether the pointer is the primary pointer of its type (https://w3c.github.io/pointerevents/#dfn-primary-pointer).</summary>
    public bool IsPrimary { get; internal set; }

    /// <summary>Gets the horizontal position of the pointer in the viewport of the top-level browsing context.</summary>
    public double X { get; internal set; }

    /// <summary>Gets the vertical position of the pointer in the viewport of the top-level browsing context.</summary>
    public double Y { get; internal set; }

    /// <summary>Gets the pressed buttons (https://w3c.github.io/pointerevents/#dfn-chorded-button-interactions: the <c>buttons</c> bit mask). A touch pointer in contact presses the primary button.</summary>
    public int Buttons { get; internal set; }

    /// <summary>
    /// Moves the pointer to a position of the viewport of the top-level browsing context: the boundary events, then
    /// <c>pointermove</c> and its compatibility mouse event; a touch pointer in contact may pan (scroll) instead, as
    /// <c>touch-action</c> allows. While the mouse is locked (https://w3c.github.io/pointerlock/), it keeps its position
    /// (<see cref="X"/>, <see cref="Y"/>): the events are fired at the pointer-lock target with the movement from the
    /// position of the previous move (<c>movementX</c>, <c>movementY</c>); see <see cref="MoveBy"/>.
    /// </summary>
    public void Move(double x, double y, PointerInputProperties? properties = null) => _input.Move(this, x, y, properties);

    /// <summary>
    /// Moves the pointer by a distance, as <see cref="Move"/> does: the movement of the device that a host reads while the
    /// mouse is locked, where the pointer has no position.
    /// </summary>
    public void MoveBy(double deltaX, double deltaY, PointerInputProperties? properties = null) => _input.Move(this, DeviceX + deltaX, DeviceY + deltaY, properties);

    /// <summary>Presses a button of the pointer (0: primary, 1: auxiliary, 2: secondary), or starts the contact of a touch pointer.</summary>
    public void Down(int button = 0, PointerInputProperties? properties = null) => _input.Down(this, checked((short)button), properties);

    /// <summary>Releases a button of the pointer, or ends the contact of a touch pointer: <c>pointerup</c>, then the click events.</summary>
    public void Up(int button = 0) => _input.Up(this, checked((short)button));

    /// <summary>Cancels the pointer (https://w3c.github.io/pointerevents/#dfn-pointercancel-event): <c>pointercancel</c>, and the buttons are released without click.</summary>
    public void Cancel() => _input.Cancel(this);

    internal bool IsTouch => PointerType == "touch";

    internal bool IsMouse => PointerType == "mouse";

    /// <summary>Whether the pointer is in the list of active pointers: the mouse always, a pen once it is used, a touch pointer while it is in contact.</summary>
    internal bool IsActive { get; set; }

    internal PointerInputProperties Properties { get; set; } = PointerInputProperties.Default;

    /// <summary>The pointer capture target override (https://w3c.github.io/pointerevents/#dfn-pointer-capture-target-override).</summary>
    internal Element? CaptureTarget { get; set; }

    /// <summary>Whether the pointer capture target override was removed from its tree since it captured the pointer.</summary>
    internal bool CaptureTargetRemoved { get; set; }

    /// <summary>The pending pointer capture target override.</summary>
    internal Element? PendingCaptureTarget { get; set; }

    /// <summary>The active document of the pointer (https://w3c.github.io/pointerevents/#dfn-active-document): the document of the target of its last pointerdown, pointermove or pointerup event.</summary>
    internal Document? ActiveDocument { get; set; }

    /// <summary>The document where the first button of a pointer that hovers was pressed: its events are dispatched there until the buttons are released.</summary>
    internal Document? PressedDocument { get; set; }

    /// <summary>The previousTarget of the pointer: the target of its last event, for the boundary events.</summary>
    internal Node? PreviousTarget { get; set; }

    /// <summary>The needsOverEvent flag: the previous target was removed, a pointerover event is needed even if the target is the same.</summary>
    internal bool NeedsOverEvent { get; set; }

    /// <summary>The PREVENT MOUSE EVENT flag (https://w3c.github.io/pointerevents/#compatibility-mapping-with-mouse-events): pointerdown was canceled.</summary>
    internal bool PreventMouseEvent { get; set; }

    /// <summary>
    /// The position of the device: the position of the pointer, except while the pointer is locked
    /// (https://w3c.github.io/pointerlock/), where the pointer keeps its position and the moves only change this one.
    /// </summary>
    internal double DeviceX { get; set; }

    internal double DeviceY { get; set; }

    /// <summary>The pointer-lock target the last pointer event of the pointer was fired at, or null: the target of its compatibility mouse event.</summary>
    internal Element? LockedEventTarget { get; set; }

    /// <summary>The movement of the last move.</summary>
    internal double MovementX { get; set; }

    internal double MovementY { get; set; }

    /// <summary>The targets of the presses of the buttons (the click is fired at the common ancestor of the targets of the press and of the release).</summary>
    internal Dictionary<short, Node?>? DownTargets { get; set; }

    /// <summary>The pseudo-elements hit by the presses of the buttons (see <see cref="PseudoTarget"/>), by button.</summary>
    internal Dictionary<short, Css.Om.CssPseudoElement?>? DownPseudoTargets { get; set; }

    /// <summary>
    /// The pseudo-element of the target of the last pointer event that was hit by the pointer (the pseudoTarget of its event,
    /// https://github.com/w3c/uievents/pull/413), or null.
    /// </summary>
    internal Css.Om.CssPseudoElement? PseudoTarget { get; set; }

    /// <summary>Whether the pointer was captured when the last button was released (the click is then fired at the capture target).</summary>
    internal Element? CaptureTargetAtRelease { get; set; }

    // The multiple clicks (https://w3c.github.io/uievents/#current-click-count)
    internal short ClickButton { get; set; } = -1;

    internal int ClickCount { get; set; }

    internal double ClickX { get; set; }

    internal double ClickY { get; set; }

    internal Node? ClickTarget { get; set; }

    internal long ClickTimestamp { get; set; }

    /// <summary>The press of the primary button on a scrollbar, until the release (see <see cref="UserInput.ScrollbarPress"/>).</summary>
    internal UserInput.ScrollbarPress? ScrollbarPress { get; set; }

    /// <summary>The press of the primary button on a resizer, until the release (see <see cref="UserInput.ResizerPress"/>).</summary>
    internal UserInput.ResizerPress? ResizerPress { get; set; }

    // The drag-and-drop operation of the mouse
    internal Node? DragNode { get; set; }

    /// <summary>Whether the press of the primary button was in the selection of the document: a move then drags the selection.</summary>
    internal bool DragsSelection { get; set; }

    internal double DownX { get; set; }

    internal double DownY { get; set; }

    internal DragAndDropOperation? DragOperation { get; set; }

    // The contact of a touch pointer
    /// <summary>The pointer event stream was canceled (the touch pans): no pointer event is fired until the end of the contact.</summary>
    internal bool IsCanceled { get; set; }

    /// <summary>Whether the touch left the slop region of its start position (a tap does not).</summary>
    internal bool HasMoved { get; set; }

    /// <summary>Whether the pan was decided (allowed or not by touch-action) for the contact.</summary>
    internal bool PanDecided { get; set; }

    /// <summary>The direct manipulations that touch-action allows for the contact (determined when it starts).</summary>
    internal int TouchActions { get; set; }

    /// <summary>The scrolling box the pan of the contact scrolls, once latched.</summary>
    internal Css.Layout.ScrollState? PanScroller { get; set; }

    internal Document? PanDocument { get; set; }

    internal bool PanScrolled { get; set; }

    /// <summary>The compatibility mouse events and the click of the tap are suppressed (touchstart or touchend was canceled).</summary>
    internal bool PreventTapEvents { get; set; }

    /// <summary>The target of the touch events of the contact (the element under the touch when it started).</summary>
    internal Element? TouchTarget { get; set; }

    /// <summary>The identifier of the touch point of the contact (https://w3c.github.io/touch-events/#dom-touch-identifier).</summary>
    internal int TouchIdentifier { get; set; }
}
