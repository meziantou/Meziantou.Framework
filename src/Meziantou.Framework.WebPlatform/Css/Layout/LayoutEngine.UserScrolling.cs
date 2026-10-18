using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The kind of a scroll of the user (see <see cref="LayoutEngine.ScrollByUser"/>).</summary>
internal enum UserScrollKind
{
    /// <summary>
    /// A key or a press on the track of a scrollbar: a relative scroll (https://drafts.csswg.org/css-scroll-snap-1/#relative-scroll)
    /// that snaps to the snap position ahead in its direction.
    /// </summary>
    Directional,

    /// <summary>
    /// The wheel: a gesture of instant scrolls by the deltas of its events (the precise deltas of a touchpad, or of the wheel
    /// actions of WebDriver), which is not snapped until it ends (see <see cref="LayoutEngine.WheelGestureEndDelay"/>).
    /// </summary>
    Wheel,

    /// <summary>A direct manipulation (a panning touch): the scroll follows the pointer until it is released.</summary>
    DirectManipulation,

    /// <summary>
    /// A drag of the thumb of a scrollbar: a direct manipulation whose scrolls are absolute scrolls (the <c>scrolled</c> feature of
    /// the scroll-state queries does not record their direction).
    /// </summary>
    ThumbDrag,
}

// The scrolls of the user (the wheel, the keys, a panning touch, the scrollbars), as in the browsers: the keys and the presses on
// the track of a scrollbar are smooth scrolls when ViewportOptions.SmoothUserScrolling is set (animated by the rendering steps,
// like the smooth scrolls of CSSOM View; a later key retargets them from their destination), and the gestures (the wheel until
// it stops, a panning touch or a drag of the thumb until the release) are not snapped while they last: they snap when they end
// (https://drafts.csswg.org/css-scroll-snap-1/#choosing; smoothly when the scrolls of the user are animated), and fire a single
// scrollend event after that (https://drafts.csswg.org/cssom-view/#scrolling-events: "when [...] scrolling has completed").
internal sealed partial class LayoutEngine
{
    /// <summary>
    /// The time without wheel event after which a gesture of the wheel ends when the scrolls of the user are animated (on the
    /// clock of the event loop; user-agent defined: Blink ends the scroll phase of the wheel events without phase after 100 ms).
    /// Without animation, each wheel event is a complete gesture.
    /// </summary>
    internal static readonly TimeSpan WheelGestureEndDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Whether the scrolls of the user are animated (<see cref="ViewportOptions.SmoothUserScrolling"/>).</summary>
    private bool SmoothUserScrolling => _document.EventLoop is not null && (_document.BrowsingContext?.Top.Options.Viewport ?? ViewportOptions.Default).SmoothUserScrolling;

    /// <summary>
    /// Scrolls a scrolling box by a delta of a scroll of the user (in the coordinates of the fragment tree, clamped to its
    /// scrolling area): see <see cref="UserScrollKind"/>.
    /// </summary>
    public void ScrollByUser(ScrollState state, PhysicalOffset delta, UserScrollKind kind)
    {
        // https://drafts.csswg.org/css-overflow-5/#current-scroll-target: the scroll of the user has no target
        SetCurrentScrollTarget(state, target: null);
        switch (kind)
        {
            case UserScrollKind.Directional:
            {
                // From the destination of the smooth scroll in progress: the keys pressed during an animation add up
                var from = GetTargetScrollPosition(state);
                var position = GetSnapPosition(state, SnapSelectionStrategy.ForDisplacement(from, delta));
                RecordRelativeScroll(state, position - state.Offset);
                if (position == state.Offset && state.SmoothScroll is null)
                {
                    UpdateScrollSnapChangeTargets(state);
                    return;
                }

                _ = PerformScroll(state, position, SmoothUserScrolling ? ScrollBehavior.Smooth : ScrollBehavior.Instant, associatedElement: null);
                return;
            }

            case UserScrollKind.Wheel:
            {
                // The wheel stops the smooth scroll in progress (e.g. the snap of the previous gesture)
                state.SmoothScroll = null;
                ResolvePendingPromises(state);
                var start = state.UserGestureStart ?? state.Offset;
                BeginUserGesture(state, start);
                var position = state.Clamp(state.Offset + delta);
                RecordRelativeScroll(state, position - state.Offset);
                UpdateUserGestureSnapTargets(state, start, position);
                SetScrollPosition(state, position);
                if (!SmoothUserScrolling)
                {
                    EndUserGesture(state);
                    return;
                }

                // The gesture ends when no wheel event follows for a while
                var version = ++state.UserGestureVersion;
                _document.EventLoop!.QueueTimer(WheelGestureEndDelay, () =>
                {
                    if (state.UserGestureVersion == version && !state.UserGestureHeld && state.Fragment is not null && _document.IsFullyActive)
                    {
                        EndUserGesture(state);
                    }
                });

                return;
            }

            default:
            {
                // A direct manipulation stops the smooth scroll in progress: the contents follow the pointer
                var start = state.UserGestureHeld && state.UserGestureStart is { } held ? held : state.Offset;
                state.SmoothScroll = null;
                ResolvePendingPromises(state);
                BeginUserGesture(state, start);
                state.UserGestureHeld = true;
                state.UserGestureVersion++;
                var position = state.Clamp(state.Offset + delta);
                if (kind == UserScrollKind.DirectManipulation)
                {
                    RecordRelativeScroll(state, position - state.Offset);
                }

                UpdateUserGestureSnapTargets(state, start, position);
                SetScrollPosition(state, position);
                return;
            }
        }
    }

    /// <summary>
    /// Starts (or continues) a scroll gesture of the user: the <c>scrollend</c> event of the box that is pending (the end of a
    /// scroll that completed in the same frame, e.g. the snap of its first layout) is merged into the one of the gesture.
    /// </summary>
    private void BeginUserGesture(ScrollState state, PhysicalOffset start)
    {
        if (state.UserGestureStart is null)
        {
            var target = state.Element ?? (EventTarget)_document;
            if (_pendingScrollEvents.RemoveAll(pending => ReferenceEquals(pending.Target, target) && pending.Type == "scrollend") > 0)
            {
                state.ChangedSinceScrollEnd = true;
            }
        }

        state.UserGestureStart = start;
    }

    /// <summary>The pointer of a direct manipulation is released (or canceled): the gesture ends (see <see cref="EndUserGesture"/>).</summary>
    public void ReleaseUserGesture(ScrollState state)
    {
        if (!state.UserGestureHeld)
            return;

        state.UserGestureHeld = false;
        EndUserGesture(state);
    }

    /// <summary>
    /// Ends the scroll gesture of the user of a scrolling box: a scroll snap container snaps to the snap position closest to the
    /// end of the gesture, among its start position and the ones beyond it in its direction (smoothly when the scrolls of the user
    /// are animated), then the scrollsnapchange and scrollend events are queued (by the end of the smooth scroll of the snap).
    /// </summary>
    private void EndUserGesture(ScrollState state)
    {
        if (state.UserGestureStart is not { } start)
            return;

        state.UserGestureStart = null;
        var position = GetSnapPosition(state, SnapSelectionStrategy.ForEndAndDirection(start, state.Offset));
        if (position != state.Offset)
        {
            _ = PerformScroll(state, position, SmoothUserScrolling ? ScrollBehavior.Smooth : ScrollBehavior.Instant, associatedElement: null);
            return;
        }

        UpdateScrollSnapChangeTargets(state);
        QueueScrollEndEvent(state);
    }

    /// <summary>
    /// Updates the snap targets of a scroll snap container during a gesture of the user, for the position the gesture would snap
    /// to if it ended at a position (a scrollsnapchanging event is queued when they change,
    /// https://drafts.csswg.org/css-scroll-snap-2/#scrollsnapchanging).
    /// </summary>
    private void UpdateUserGestureSnapTargets(ScrollState state, PhysicalOffset start, PhysicalOffset position)
        => _ = GetSnapPosition(state, SnapSelectionStrategy.ForEndAndDirection(start, position));
}
