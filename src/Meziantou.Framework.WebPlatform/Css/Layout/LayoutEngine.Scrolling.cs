using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The scrolling boxes of the document (https://drafts.csswg.org/cssom-view/#scrolling): the scroll positions of the scroll
// containers and of the viewport, the scroll operations (instant and smooth), and the scroll events fired by the rendering steps.
internal sealed partial class LayoutEngine
{
    private Dictionary<Element, ScrollState>? _scrollStates;

    /// <summary>The scroll positions of the elements that stopped being scroll containers but still generate a box (see <see cref="DiscardScrollStates"/>).</summary>
    private Dictionary<Element, PhysicalOffset>? _savedScrollPositions;
    private ScrollState? _viewportScrollState;

    /// <summary>The pending scroll events of the document (https://drafts.csswg.org/cssom-view/#document-pending-scroll-events), in the order they were added.</summary>
    private readonly List<PendingScrollEvent> _pendingScrollEvents = [];

    /// <summary>The target scrolled into view once the document is laid out (the last fragment navigation or focus() call of a document whose geometry was never read).</summary>
    private (Element Target, ScrollLogicalPosition Block, ScrollLogicalPosition Inline, Dom.Range? Range, bool SnapsToTarget)? _pendingScrollIntoView;

    /// <summary>Whether a scrolling box has a deferred scroll event (see <see cref="DeferScrollEvent"/>).</summary>
    private bool _hasDeferredScrollEvents;

    /// <summary>The number of changes of the scroll positions, without a layout pass (see <see cref="LayoutShiftTracker"/>).</summary>
    internal int ScrollVersion { get; private set; }

    /// <summary>The scrolling box of the viewport (its scroll position is kept when the document is laid out again).</summary>
    public ScrollState ViewportScrollState => _viewportScrollState ??= new ScrollState(element: null);

    /// <summary>Returns the scrolling box of a scroll container in the last layout, without computing the layout, or null.</summary>
    public ScrollState? TryGetScrollState(Element element) => _scrollStates is not null && _scrollStates.TryGetValue(element, out var state) ? state : null;

    /// <summary>Returns the scrolling box of the principal box of an element (the layout is computed when it is out of date), or null when it is not a scroll container.</summary>
    public ScrollState? GetScrollState(Element element)
    {
        EnsureLayout();

        EnsureContentsLaidOut(element);
        return TryGetScrollState(element) is { Fragment: not null } state ? state : null;
    }

    private ScrollState GetOrCreateScrollState(Element element)
    {
        _scrollStates ??= new(ReferenceEqualityComparer.Instance);
        if (!_scrollStates.TryGetValue(element, out var state))
        {
            state = new ScrollState(element);
            _scrollStates.Add(element, state);
            if (_savedScrollPositions is not null && _savedScrollPositions.Remove(element, out var saved))
            {
                state.Offset = saved;
            }
        }

        return state;
    }

    /// <summary>Associates a scrolling box with the fragment of its scroll container after a layout pass, and clamps its scroll position to its new scrolling area.</summary>
    private void UpdateScrollState(ScrollState state, BoxFragment fragment)
    {
        state.LayoutVersion = _fragmentIndexVersion;
        state.Fragment = fragment;
        fragment.ScrollState = state;
        state.ScrollingArea = fragment.GetScrollingArea();
        state.Scrollport = fragment.PaddingBoxRect;
        // The layout of a scroll container and its intrinsic sizes depend on the scrollbars of its last layout
        if (state.Scrollbars != fragment.ScrollbarsDisplayed && fragment.Box is { } scrollContainer)
        {
            LayoutResultCache.Invalidate(scrollContainer);
        }

        state.Scrollbars = fragment.ScrollbarsDisplayed;
        if (state.IsViewport)
        {
            var (overflowStyle, rootStyle) = ScrollContainers.GetViewportStyles(fragment.Box);
            state.Overflow = ScrollContainers.GetViewportOverflow(overflowStyle);
            state.WritingDirection = rootStyle is null ? WritingDirection.HorizontalLtr : WritingDirection.FromStyle(rootStyle);
            state.ContainerStyle = rootStyle;
        }
        else
        {
            state.Overflow = ScrollContainers.GetUsedOverflow(fragment.Box);
            state.WritingDirection = fragment.WritingDirection;
            state.ContainerStyle = fragment.Box.Style.Layout;
        }

        // The scroll position stays in the scrolling area when the contents shrink (as in the browsers, the scroll and scrollend
        // events are fired: WPT scrollend-event-fired-after-layout-clamping.html)
        // The contents of a box that skips them are not laid out: its scroll position is kept, as in the browsers (WPT
        // css-contain/content-visibility/content-visibility-081.html)
        var clamped = state.SmoothScroll is null && !fragment.Box.HasUnlaidSkippedContents ? state.Clamp(state.Offset) : state.Offset;
        if (clamped != state.Offset)
        {
            state.Offset = clamped;
            state.ChangedSinceScrollEnd = true;
            QueueScrollEvent(state);
            QueueScrollEndEvent(state);
        }
    }

    /// <summary>
    /// Discards the scrolling boxes of the elements that are not scroll containers anymore. As in the browsers, the scroll
    /// position of an element that still generates a box (e.g. its <c>overflow</c> became <c>visible</c>) is kept, and restored
    /// when it becomes a scroll container again; the one of an element that generates no box anymore is reset.
    /// </summary>
    private void DiscardScrollStates()
    {
        if (_savedScrollPositions is { Count: > 0 } saved)
        {
            foreach (var element in saved.Keys.ToArray())
            {
                if (_boxTree?.GetPrincipalBox(element) is null)
                {
                    saved.Remove(element);
                }
            }
        }

        if (_scrollStates is null)
            return;

        List<Element>? removed = null;
        foreach (var (element, state) in _scrollStates)
        {
            if (state.LayoutVersion != _fragmentIndexVersion)
            {
                state.Fragment = null;
                state.SmoothScroll = null;
                state.UserGestureStart = null;
                state.UserGestureHeld = false;
                ResolvePendingPromises(state);
                (removed ??= []).Add(element);
                if (state.Offset != PhysicalOffset.Zero && _boxTree?.GetPrincipalBox(element) is not null)
                {
                    (_savedScrollPositions ??= new(ReferenceEqualityComparer.Instance))[element] = state.Offset;
                }
            }
        }

        if (removed is not null)
        {
            foreach (var element in removed)
            {
                _scrollStates.Remove(element);
            }
        }
    }

    /// <summary>
    /// Performs a scroll of a scrolling box (https://drafts.csswg.org/cssom-view/#perform-a-scroll) to a scroll position (clamped
    /// to the scrolling area): an instant scroll, or a smooth scroll animated by the rendering steps when the behavior is
    /// <c>smooth</c>, or <c>auto</c> and the associated element has <c>scroll-behavior: smooth</c>. The task completes when the
    /// scroll is completed or aborted.
    /// </summary>
    public Task PerformScroll(ScrollState state, PhysicalOffset position, ScrollBehavior behavior, Element? associatedElement)
    {
        state.IsScrolledByOperation = true;
        return PerformScrollCore(state, position, behavior, associatedElement);
    }

    /// <summary>Performs a scroll of a scrolling box that is not a scroll operation (a re-snap after a layout change, see <see cref="PerformScroll"/>).</summary>
    private Task PerformScrollCore(ScrollState state, PhysicalOffset position, ScrollBehavior behavior, Element? associatedElement)
    {
        position = state.Clamp(position);

        // The scroll ends the scroll gesture of the user whose pointer is not pressed (the wheel) without snapping it
        if (!state.UserGestureHeld)
        {
            state.UserGestureStart = null;
            state.UserGestureVersion++;
        }

        var smooth = behavior == ScrollBehavior.Smooth
            || (behavior == ScrollBehavior.Auto && associatedElement is not null && Cascade.ComputedStyleDeclarations.IsBeingRendered(associatedElement)
                && _document.GetStyleEngine().GetStyle(associatedElement).Layout.Overflow.SmoothScrolling);
        var eventLoop = _document.EventLoop;

        // A smooth scroll to the position an ongoing smooth scroll goes to does not restart it, as in the browsers (WPT
        // smooth-scroll-nonstop.html): its promise completes with the ongoing one
        if (smooth && eventLoop is not null && state.SmoothScroll is { } ongoing && ongoing.Target == position)
            return AddPendingPromise(state);

        // "Abort any ongoing smooth scroll for box. Resolve all pending scroll Promises whose scroll container is box."
        state.SmoothScroll = null;
        ResolvePendingPromises(state);
        if (smooth && position != state.Offset && eventLoop is not null)
        {
            state.SmoothScroll = new SmoothScroll(state.Offset, position, eventLoop.TimeProvider.GetTimestamp());
            eventLoop.RequestScrollSteps(_document);
            return AddPendingPromise(state);
        }

        // An instant scroll completes at once: "If the scroll position changed as a result of this call, emit the scrollend event"
        // (preceded by the scrollsnapchange event, https://drafts.csswg.org/css-scroll-snap-2/#scrollsnapchange)
        if (SetScrollPosition(state, position))
        {
            UpdateScrollSnapChangeTargets(state);
            QueueScrollEndEvent(state);
        }
        else
        {
            UpdateScrollSnapChangeTargets(state);
        }

        return Task.CompletedTask;
    }

    private static Task AddPendingPromise(ScrollState state)
    {
        var completion = new TaskCompletionSource();
        (state.PendingPromises ??= []).Add(completion);
        return completion.Task;
    }

    /// <summary>Returns the scroll position a smooth scroll in progress goes to, or the current scroll position.</summary>
    public static PhysicalOffset GetTargetScrollPosition(ScrollState state) => state.SmoothScroll?.Target ?? state.Offset;

    /// <summary>
    /// Sets the scroll position of a scrolling box (an explicit scroll: it invalidates the anchor nodes of the scroll anchoring)
    /// and queues a scroll event when it changes. Returns whether it changed.
    /// </summary>
    private bool SetScrollPosition(ScrollState state, PhysicalOffset position)
    {
        if (position == state.Offset)
            return false;

        InvalidateScrollAnchors(state);
        state.ChangedSinceScrollEnd = true;
        ApplyScrollPosition(state, position);
        QueueScrollEvent(state);
        return true;
    }

    /// <summary>Changes the scroll position of a scrolling box (the caller queues the scroll event). Returns whether it changed.</summary>
    private bool ApplyScrollPosition(ScrollState state, PhysicalOffset position)
    {
        if (position == state.Offset)
            return false;

        state.Offset = position;
        ScrollVersion++;
        InvalidatePaint();
        _document.AnimationsOrNull?.OnScrollOrLayoutChanged();

        // https://drafts.csswg.org/css-position-3/#stickypos-insets: the sticky offsets depend on the scroll positions
        if (_stickyFragments is not null && !_inLayout)
        {
            StickyPosition.Apply(_stickyFragments, _initialContainingBlockSize, ViewportScrollState.Offset);
        }

        // https://drafts.csswg.org/css-anchor-position-1/#default-scroll-shift: the anchor-positioned boxes follow their default anchor box
        if (_anchorPositionedBoxes is { Count: > 0 } && !_inLayout)
        {
            ApplyAnchorScrollShifts();
            _document.EventLoop?.RequestRenderingUpdate();
        }

        // The scroll-state query containers, the active scroll markers and the scroll buttons are snapshotted by the next rendering update
        if (_scrollStateQueried || HasScrollMarkerState)
        {
            _document.EventLoop?.RequestRenderingUpdate();
        }

        return true;
    }

    /// <summary>
    /// "Whenever a viewport gets scrolled [...] Append (doc, "scroll") to doc's pending scroll events"; "Whenever an element gets
    /// scrolled [...] Append (element, "scroll") to doc's pending scroll events" (https://drafts.csswg.org/cssom-view/#scrolling-events).
    /// </summary>
    private void QueueScrollEvent(ScrollState state)
    {
        state.LastScrollEventOffset = state.Offset;
        QueuePendingScrollEvent(state.Element ?? (EventTarget)_document, "scroll");
    }

    /// <summary>
    /// Defers the scroll event of a change of the scroll position that is not an explicit scroll (a scroll anchoring adjustment)
    /// to the next rendering update (see <see cref="QueueDeferredScrollEvents"/>), as Blink: the event is fired when the scroll
    /// position differs from the one of the last scroll event at that time, so the adjustments that cancel each other out (e.g. an
    /// element shown and hidden again) fire no event.
    /// </summary>
    private void DeferScrollEvent(ScrollState state)
    {
        state.HasDeferredScrollEvent = true;
        _hasDeferredScrollEvents = true;
        _document.EventLoop?.RequestRenderingUpdate();
    }

    /// <summary>
    /// Queues the deferred scroll events (see <see cref="DeferScrollEvent"/>) of the scrolling boxes whose scroll position changed
    /// since their last scroll event, at the end of the rendering update: they are fired by the scroll steps of the next frame. As
    /// in Blink, the change is complete (a scrollend event follows) unless a smooth scroll is in progress.
    /// </summary>
    private void QueueDeferredScrollEvents()
    {
        if (!_hasDeferredScrollEvents)
            return;

        _hasDeferredScrollEvents = false;
        foreach (var state in EnumerateScrollStates())
        {
            if (!state.HasDeferredScrollEvent)
                continue;

            state.HasDeferredScrollEvent = false;
            if (state.Fragment is null || state.LastScrollEventOffset == state.Offset)
                continue;

            QueueScrollEvent(state);
            if (state.SmoothScroll is null)
            {
                state.ChangedSinceScrollEnd = true;
                QueueScrollEndEvent(state);
            }
        }
    }

    private void QueueScrollEndEvent(ScrollState state)
    {
        // The scroll of a gesture of the user ends when the gesture ends (see EndUserGesture)
        if (!state.ChangedSinceScrollEnd || state.UserGestureStart is not null)
            return;

        state.ChangedSinceScrollEnd = false;
        QueuePendingScrollEvent(state.Element ?? (EventTarget)_document, "scrollend");
    }

    private void QueuePendingScrollEvent(EventTarget target, string type, Node? snapTargetBlock = null, Node? snapTargetInline = null)
    {
        for (var i = 0; i < _pendingScrollEvents.Count; i++)
        {
            var pending = _pendingScrollEvents[i];
            if (ReferenceEquals(pending.Target, target) && pending.Type == type)
            {
                // A pending snap event reports the latest snap targets
                _pendingScrollEvents[i] = pending with { SnapTargetBlock = snapTargetBlock, SnapTargetInline = snapTargetInline };
                return;
            }
        }

        _pendingScrollEvents.Add(new PendingScrollEvent(target, type, snapTargetBlock, snapTargetInline));
        _document.EventLoop?.RequestScrollSteps(_document);
    }

    private static void ResolvePendingPromises(ScrollState state)
    {
        if (state.PendingPromises is not { Count: > 0 } promises)
            return;

        var completions = promises.ToArray();
        promises.Clear();
        foreach (var completion in completions)
        {
            completion.TrySetResult();
        }
    }

    /// <summary>
    /// Runs the scroll steps of the document (https://drafts.csswg.org/cssom-view/#document-run-the-scroll-steps), from the
    /// rendering steps: advances the smooth scrolls to the time of the frame (a completed one queues its <c>scrollend</c> event
    /// and resolves its promises), then fires the pending scroll events. Returns whether a smooth scroll is still in progress
    /// (the next frame must run the steps again).
    /// </summary>
    internal bool RunScrollSteps(long frameTimestamp, long timestampFrequency)
    {
        var animating = false;
        foreach (var state in EnumerateScrollStates())
        {
            if (state.SmoothScroll is not { } smooth)
                continue;

            var elapsed = (double)(frameTimestamp - smooth.StartTimestamp) / timestampFrequency;
            var progress = elapsed / SmoothScroll.Duration.TotalSeconds;
            SetScrollPosition(state, state.Clamp(smooth.GetPosition(progress)));
            if (progress >= 1)
            {
                state.SmoothScroll = null;
                UpdateScrollSnapChangeTargets(state);
                QueueScrollEndEvent(state);
                ResolvePendingPromises(state);
            }
            else
            {
                animating = true;
            }
        }

        if (_pendingScrollEvents.Count > 0)
        {
            var events = _pendingScrollEvents.ToArray();
            _pendingScrollEvents.Clear();
            foreach (var (target, type, snapTargetBlock, snapTargetInline) in events)
            {
                // "If target is a Document, and type is "scroll" or "scrollend", fire an event named type that bubbles at target.
                // Otherwise, fire an event named type at target." The snap events are SnapEvent objects that bubble only at a document
                // (https://drafts.csswg.org/css-scroll-snap-2/#snapevent-interface)
                if (type is "scrollsnapchange" or "scrollsnapchanging")
                {
                    target.FireEvent(new Om.SnapEvent(type, new Om.SnapEventInit { Bubbles = target is Document, SnapTargetBlock = snapTargetBlock, SnapTargetInline = snapTargetInline }));
                }
                else
                {
                    target.FireEvent(target is Document ? new Event(type, new EventInit { Bubbles = true }) : new Event(type));
                }
            }
        }

        return animating;
    }

    private IEnumerable<ScrollState> EnumerateScrollStates()
    {
        if (_viewportScrollState is not null)
            yield return _viewportScrollState;

        if (_scrollStates is not null)
        {
            foreach (var state in _scrollStates.Values.ToArray())
            {
                yield return state;
            }
        }
    }

    /// <summary>
    /// Scrolls an element into view (https://drafts.csswg.org/cssom-view/#scroll-a-target-into-view, with the behavior <c>auto</c>)
    /// once the document is laid out: at once when its layout was already requested, else after its first layout (the scroll
    /// positions are only observable through the geometry, so a document that never reads it is not laid out by a fragment
    /// navigation or a call of <c>focus()</c>, unless the scroll events of the viewport are listened to).
    /// </summary>
    /// <param name="target">The element.</param>
    /// <param name="block">The alignment in the block flow direction.</param>
    /// <param name="inline">The alignment in the inline base direction.</param>
    /// <param name="range">The range to scroll into view instead of the box of the element (the target text of a text directive), or null.</param>
    /// <param name="snapsToTarget">Whether the nearest scroll snap container of the target snaps to one of its snap positions (see <see cref="ScrollIntoView.Run"/>).</param>
    public void ScrollIntoViewWhenLaidOut(Element target, ScrollLogicalPosition block, ScrollLogicalPosition inline, Dom.Range? range = null, bool snapsToTarget = false)
    {
        // The scroll events of the viewport make the scroll observable: the document is laid out at once when they are listened to
        if (IsLayoutRequested || HasViewportScrollListeners())
        {
            RunScrollIntoView(target, block, inline, range, snapsToTarget);
            return;
        }

        _pendingScrollIntoView = (target, block, inline, range, snapsToTarget);
    }

    private void RunScrollIntoView(Element target, ScrollLogicalPosition block, ScrollLogicalPosition inline, Dom.Range? range, bool snapsToTarget)
    {
        // https://wicg.github.io/scroll-to-text-fragment/#invoking-text-directives: "scroll a target into view, with target set
        // to scrollTarget" (the range: its bounding box). As in the browsers, the start of a range that is larger than the
        // viewport is scrolled into view (its first box).
        PhysicalRect? rangeRect = null;
        if (range is not null && EnsureLayout() is not null)
        {
            var rects = range.GetClientRects();
            var bounds = range.GetBoundingClientRect();
            if (rects.Length > 0 && (bounds.Width > 0 || bounds.Height > 0))
            {
                var viewport = Css.Cascade.StyleEngine.CreateEnvironment(_document);
                var rect = bounds.Height > viewport.Height || bounds.Width > viewport.Width ? rects[0] : bounds;
                rangeRect = new PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height).Translate(FromViewport(PhysicalOffset.Zero));
            }
        }

        _ = ScrollIntoView.Run(this, target, ScrollBehavior.Auto, block, inline, container: null, targetRect: rangeRect, snapsToTarget: snapsToTarget);
    }

    private bool HasViewportScrollListeners()
        => _document.HasEventListener("scroll") || _document.HasEventListener("scrollend")
            || (_document.DefaultView is { } window && (window.HasEventListener("scroll") || window.HasEventListener("scrollend")));

    /// <summary>Cancels the scroll into view that waits for the first layout (see <see cref="ScrollIntoViewWhenLaidOut"/>).</summary>
    public void CancelPendingScrollIntoView() => _pendingScrollIntoView = null;

    private void RunPendingScrollsIntoView()
    {
        if (_pendingScrollIntoView is not { } pending)
            return;

        _pendingScrollIntoView = null;
        var (target, block, inline, range, snapsToTarget) = pending;
        if (target.IsConnected && ReferenceEquals(target.NodeDocument, _document))
        {
            RunScrollIntoView(target, block, inline, range, snapsToTarget);
        }
    }
}

/// <summary>A pending scroll event (https://drafts.csswg.org/cssom-view/#document-pending-scroll-events), with the snap targets of a snap event.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PendingScrollEvent(EventTarget Target, string Type, Node? SnapTargetBlock, Node? SnapTargetInline);
