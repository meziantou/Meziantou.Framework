using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The scrolling box (https://drafts.csswg.org/cssom-view/#scrolling-box) of a scroll container or of the viewport: its scroll
/// position, kept by the <see cref="LayoutEngine"/> across the layout passes (the fragments are created again by each pass),
/// and the geometry of the last layout.
/// </summary>
/// <remarks>
/// The scroll position is stored as a physical offset: the translation of the contents of the scroll container, from the
/// scroll origin (https://drafts.csswg.org/css-overflow-3/#scroll-origin: the block-start inline-start corner, where the
/// scrolling area meets the scrollport at the initial scroll position). It is (<c>scrollLeft</c>, <c>scrollTop</c>) in the
/// coordinates of the fragment tree: the contents are moved by its opposite, and it is negative when the scrollable overflow
/// extends leftwards (<c>direction: rtl</c>, <c>vertical-rl</c>) or upwards.
/// </remarks>
internal sealed class ScrollState
{
    internal ScrollState(Element? element)
    {
        Element = element;
    }

    /// <summary>The element whose principal box is the scroll container (the originating element of a pseudo-element), or null for the viewport.</summary>
    public Element? Element { get; }

    /// <summary>The pseudo-element whose box is the scroll container (e.g. <c>::before</c>, <c>::scroll-marker-group</c>), or null.</summary>
    public string? PseudoElement { get; init; }

    /// <summary>
    /// The element whose style applies to the scrolling box (<c>scroll-behavior</c>, <c>scroll-padding</c>): its element, the root
    /// element for the viewport, null for a pseudo-element (see <see cref="ContainerStyle"/>).
    /// </summary>
    internal Element? GetAssociatedElement(Document document) => PseudoElement is not null ? null : Element ?? document.DocumentElement;

    /// <summary>Whether the scrolling box is the one of the viewport.</summary>
    public bool IsViewport => Element is null;

    /// <summary>The fragment of the scroll container (the root fragment for the viewport) in the last layout, or null when it is not a scroll container anymore.</summary>
    public BoxFragment? Fragment { get; internal set; }

    /// <summary>The current scroll position (see the remarks of the class).</summary>
    public PhysicalOffset Offset { get; internal set; }

    /// <summary>
    /// The scrolling area (https://drafts.csswg.org/cssom-view/#scrolling-area): the reachable scrollable overflow of the
    /// scroll container, in the coordinates of its fragment at the scroll position 0.
    /// </summary>
    public PhysicalRect ScrollingArea { get; internal set; }

    /// <summary>The scrollport (https://drafts.csswg.org/css-overflow-3/#scrollport): the padding box without the scrollbars, in the coordinates of the fragment.</summary>
    public PhysicalRect Scrollport { get; internal set; }

    /// <summary>The used <c>overflow</c> of the scroll container: an axis whose overflow is <c>clip</c> (or <c>visible</c>) does not scroll.</summary>
    internal (OverflowKind X, OverflowKind Y) Overflow { get; set; }

    /// <summary>The scrollbars displayed by the last layout (the first guess of the next layout of an <c>overflow: auto</c> box).</summary>
    internal Scrollbars Scrollbars { get; set; }

    /// <summary>The writing mode and the direction of the scroll container (of the root element for the viewport): its overflow directions.</summary>
    internal WritingDirection WritingDirection { get; set; }

    /// <summary>The style of the scroll container (of the root element for the viewport, null without root box): its scroll-snap-type and scroll-padding.</summary>
    internal LayoutStyle? ContainerStyle { get; set; }

    /// <summary>The smallest scroll position.</summary>
    public PhysicalOffset MinOffset => new(
        ScrollContainers.IsScrollable(Overflow.X) ? Math.Min(0, ScrollingArea.Left - Scrollport.Left) : 0,
        ScrollContainers.IsScrollable(Overflow.Y) ? Math.Min(0, ScrollingArea.Top - Scrollport.Top) : 0);

    /// <summary>The largest scroll position.</summary>
    public PhysicalOffset MaxOffset => new(
        ScrollContainers.IsScrollable(Overflow.X) ? Math.Max(0, ScrollingArea.Right - Scrollport.Right) : 0,
        ScrollContainers.IsScrollable(Overflow.Y) ? Math.Max(0, ScrollingArea.Bottom - Scrollport.Bottom) : 0);

    /// <summary>Whether the scrolling area is larger than the scrollport in an axis that scrolls (https://drafts.csswg.org/cssom-view/#scrolling-box: "has overflow").</summary>
    public bool HasOverflow => MinOffset != MaxOffset;

    /// <summary>The smooth scroll in progress (https://drafts.csswg.org/cssom-view/#concept-smooth-scroll), or null.</summary>
    internal SmoothScroll? SmoothScroll { get; set; }

    /// <summary>
    /// The scroll position when the scroll gesture of the user in progress started (a panning touch or a drag of the thumb of a
    /// scrollbar until the release, the wheel until it stops), or null: the gesture is not snapped while it lasts, it snaps when
    /// it ends, and its <c>scrollend</c> event is fired after that (see <see cref="LayoutEngine.ScrollByUser"/>).
    /// </summary>
    internal PhysicalOffset? UserGestureStart { get; set; }

    /// <summary>The number of scrolls of the gestures of the user (the end of a wheel gesture is canceled by a later scroll).</summary>
    internal int UserGestureVersion { get; set; }

    /// <summary>Whether the pointer of the scroll gesture of the user is still pressed (a panning touch, a drag of the thumb of a scrollbar).</summary>
    internal bool UserGestureHeld { get; set; }

    /// <summary>The promises of the scroll operations that are not completed (https://drafts.csswg.org/cssom-view/#perform-a-scroll).</summary>
    internal List<TaskCompletionSource>? PendingPromises { get; set; }

    /// <summary>The scroll position when the last scroll event was queued, or null.</summary>
    internal PhysicalOffset? LastScrollEventOffset { get; set; }

    /// <summary>Whether the scroll event of a scroll anchoring adjustment is deferred to the next rendering update.</summary>
    internal bool HasDeferredScrollEvent { get; set; }

    /// <summary>Whether the scroll position changed since the last <c>scrollend</c> event was queued (the scroll operations that change nothing fire no <c>scrollend</c>).</summary>
    internal bool ChangedSinceScrollEnd { get; set; }

    /// <summary>Whether the state was associated with a fragment by the last layout (the states of the boxes that are not scroll containers anymore are discarded).</summary>
    internal int LayoutVersion { get; set; }

    /// <summary>The scroll snapping state (https://drafts.csswg.org/css-scroll-snap-1/), once the box was a scroll snap container.</summary>
    internal ScrollSnapState? Snap { get; set; }

    /// <summary>The anchor node of the scroll anchoring (https://drafts.csswg.org/css-scroll-anchoring/), or null when none is selected.</summary>
    internal ScrollAnchor? Anchor { get; set; }

    /// <summary>
    /// The sum of the changes of the scroll position made by the scroll anchoring adjustments: the layout shift tracker does not
    /// count the movement of the contents compensated by an adjustment (see <see cref="LayoutShiftTracker"/>).
    /// </summary>
    internal PhysicalOffset AnchoringAdjustments { get; set; }

    /// <summary>
    /// The directions of the most recent relative scroll (https://drafts.csswg.org/css-scroll-snap-1/#relative-scroll) that
    /// changed the scroll position, one per axis (an axis keeps its direction when a later relative scroll does not move in it):
    /// the <c>scrolled</c> feature of the container scroll-state queries (https://drafts.csswg.org/css-conditional-5/#scrolled).
    /// </summary>
    internal PhysicalEdges RelativeScrollDirections { get; set; }

    /// <summary>
    /// The current scroll target (https://drafts.csswg.org/css-overflow-5/#current-scroll-target): the element the last scrolling
    /// operation of the scroll container scrolled into view; null after a scrolling operation without target. It selects the
    /// active scroll marker (see <see cref="LayoutEngine.UpdateScrollMarkerSnapshots"/>).
    /// </summary>
    internal Element? CurrentScrollTarget { get; set; }

    /// <summary>The column of the current scroll target that was scrolled into view, when the target is a column of a multi-column container (the activation of a <c>::column::scroll-marker</c>).</summary>
    internal int? CurrentScrollTargetColumn { get; set; }

    /// <summary>
    /// Whether a scroll operation (of the user, of a script, of a navigation) scrolled the box: the user agent "has reason to believe
    /// the user is no longer interested in scrolling to the initial scroll position", so the box is not scrolled to its initial
    /// scroll target anymore (https://drafts.csswg.org/css-scroll-snap-2/#post-first-layout-arrivals).
    /// </summary>
    internal bool IsScrolledByOperation { get; set; }

    /// <summary>Clamps a scroll position to the scrolling area (https://drafts.csswg.org/cssom-view/#scroll-an-element).</summary>
    public PhysicalOffset Clamp(PhysicalOffset position)
    {
        var min = MinOffset;
        var max = MaxOffset;
        return new PhysicalOffset(Math.Clamp(position.X, min.X, max.X), Math.Clamp(position.Y, min.Y, max.Y));
    }
}

/// <summary>
/// The scroll snapping state of a scrolling box: the snap container of the last layout, the snap targets it is snapped to
/// (https://drafts.csswg.org/css-scroll-snap-1/#re-snap), and the snap targets of the snap events
/// (https://drafts.csswg.org/css-scroll-snap-2/#snap-events).
/// </summary>
internal sealed class ScrollSnapState
{
    /// <summary>The snap container of the last layout, or null when the box does not snap (<c>scroll-snap-type: none</c>).</summary>
    public SnapContainer? Container { get; set; }

    /// <summary>The snap target in the horizontal axis, or null when the box is not snapped in that axis.</summary>
    public SnapTarget? TargetX { get; set; }

    /// <summary>The snap target in the vertical axis, or null when the box is not snapped in that axis.</summary>
    public SnapTarget? TargetY { get; set; }

    /// <summary>Whether the targets of the snap events were set (the first snap does not fire snap events).</summary>
    public bool EventTargetsInitialized { get; set; }

    /// <summary>The scrollsnapchanging block-axis target (https://drafts.csswg.org/css-scroll-snap-2/#scrollsnapchanging-block-axis-target).</summary>
    public SnapTarget? ChangingBlock { get; set; }

    /// <summary>The scrollsnapchanging inline-axis target.</summary>
    public SnapTarget? ChangingInline { get; set; }

    /// <summary>The scrollsnapchangeTargetBlock (https://drafts.csswg.org/css-scroll-snap-2/#scrollsnapchangetargetblock).</summary>
    public SnapTarget? ChangeBlock { get; set; }

    /// <summary>The scrollsnapchangeTargetInline.</summary>
    public SnapTarget? ChangeInline { get; set; }
}

/// <summary>A smooth scroll in progress (https://drafts.csswg.org/cssom-view/#concept-smooth-scroll), animated by the rendering steps.</summary>
internal sealed class SmoothScroll
{
    /// <summary>The duration of a smooth scroll (user-agent-defined; the browsers use a few hundred milliseconds).</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(300);

    private static readonly CssEasing EaseInOut = CssEasing.Parse("ease-in-out")!;

    public SmoothScroll(PhysicalOffset start, PhysicalOffset target, long startTimestamp)
    {
        Start = start;
        Target = target;
        StartTimestamp = startTimestamp;
    }

    public PhysicalOffset Start { get; }

    public PhysicalOffset Target { get; }

    /// <summary>The timestamp (<see cref="TimeProvider.GetTimestamp"/>) of the first frame of the animation, or 0 before it.</summary>
    public long StartTimestamp { get; set; }

    /// <summary>Returns the scroll position at a time: an ease-in-out curve from <see cref="Start"/> to <see cref="Target"/>.</summary>
    public PhysicalOffset GetPosition(double progress)
    {
        if (progress >= 1)
            return Target;

        // ease-in-out (https://drafts.csswg.org/css-easing-1/#cubic-bezier-easing-functions: cubic-bezier(0.42, 0, 0.58, 1))
        var eased = EaseInOut.Evaluate(Math.Max(0, progress));
        return new PhysicalOffset(Start.X + ((Target.X - Start.X) * eased), Start.Y + ((Target.Y - Start.Y) * eased));
    }
}
