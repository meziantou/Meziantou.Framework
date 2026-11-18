using Meziantou.Framework.WebPlatform.Dom;

#pragma warning disable WEBPLATEXP0022 // The scroll performance timing is a proposal

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A scroll of the user measured for the scroll performance timing (see <see cref="PerformanceScrollTiming"/>): its input, its
/// start, the frames that displayed it and the time of its last change of the scroll position.
/// </summary>
internal sealed class ScrollTimingRecord(string source, PhysicalOffset startOffset, long startTimestamp)
{
    /// <summary>The input of the scroll: <c>wheel</c>, <c>keyboard</c>, <c>touch</c> or <c>other</c>.</summary>
    public string Source { get; } = source;

    /// <summary>The scroll position at the start of the scroll.</summary>
    public PhysicalOffset StartOffset { get; } = startOffset;

    /// <summary>The time of the first input of the scroll (on the clock of the event loop).</summary>
    public long StartTimestamp { get; } = startTimestamp;

    /// <summary>The time of the last change of the scroll position, or 0 when it did not change.</summary>
    public long LastChangeTimestamp { get; set; }

    /// <summary>The time of the first frame that displayed the scroll, or null.</summary>
    public long? FirstFrameTimestamp { get; set; }

    /// <summary>The scroll position displayed by the last frame.</summary>
    public PhysicalOffset LastFrameOffset { get; set; } = startOffset;

    /// <summary>The number of frames whose scroll position differed from the one of the previous frame.</summary>
    public uint FramesProduced { get; set; }

    /// <summary>The direction of the inputs of the scroll in each axis (the sign of their deltas), or 0 when no input moved in the axis.</summary>
    public (int X, int Y) Direction { get; set; }

    /// <summary>Whether an input in the direction of a delta goes in the opposite direction of the scroll in an axis.</summary>
    public bool IsReversedBy(PhysicalOffset delta) => (Direction.X * Math.Sign(delta.X) < 0) || (Direction.Y * Math.Sign(delta.Y) < 0);

    /// <summary>Records the direction of an input in the axes where the scroll has no direction yet.</summary>
    public void AddDirection(PhysicalOffset delta) => Direction = (Direction.X != 0 ? Direction.X : Math.Sign(delta.X), Direction.Y != 0 ? Direction.Y : Math.Sign(delta.Y));
}

// The scroll performance timing (https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md):
// a scroll of the user starts with its first input and ends with its scrollend event (the end of its gesture, of its smooth
// scroll and of its snap). The scroll steps of each frame count the frames that displayed a new scroll position, and report the
// scrolls that ended to the scroll performance observers of the window. An input of another source, or in the opposite
// direction, ends the scroll in progress and starts a new one.
internal sealed partial class LayoutEngine
{
    /// <summary>The number of scrolling boxes that have a <see cref="ScrollState.ScrollTiming"/>.</summary>
    private int _scrollTimingCount;

    /// <summary>Whether a scroll of the user is performed (see <see cref="PerformUserScroll"/>): the other scrolls end the measure of the scroll of the user in progress.</summary>
    private bool _performingUserScroll;

    /// <summary>Returns the source of the scroll performance timing of a kind of scroll of the user.</summary>
    internal static string GetScrollSource(UserScrollKind kind) => kind switch
    {
        UserScrollKind.Wheel => "wheel",
        UserScrollKind.Directional => "keyboard",
        UserScrollKind.DirectManipulation => "touch",
        _ => "other",
    };

    /// <summary>
    /// Starts (or continues) the measure of a scroll of the user of a scrolling box, before an input scrolls it: the scroll in
    /// progress continues when the input has the same source and does not reverse its direction.
    /// </summary>
    /// <param name="state">The scrolling box.</param>
    /// <param name="source">The source of the input.</param>
    /// <param name="delta">The delta of the input: its direction (not the distance to the destination of a smooth scroll in progress).</param>
    internal void TrackUserScroll(ScrollState state, string source, PhysicalOffset delta)
    {
        if (_document.EventLoop is not { } eventLoop || _document.RelevantWindow is not { } window || !window.Options.EffectiveFeatures.ScrollTimingEnabled)
            return;

        if (state.ScrollTiming is { } current)
        {
            // A scroll that did not move and is not animated (e.g. a scroll that a later operation stopped) is started again
            var inProgress = current.LastChangeTimestamp != 0 || state.SmoothScroll is not null;
            if (inProgress && current.Source == source && !current.IsReversedBy(delta))
            {
                current.AddDirection(delta);
                return;
            }

            ReportScrollTiming(state);
        }

        var record = new ScrollTimingRecord(source, state.Offset, eventLoop.TimeProvider.GetTimestamp());
        record.AddDirection(delta);
        state.ScrollTiming = record;
        _scrollTimingCount++;
    }

    /// <summary>
    /// Counts the frame for the scrolls of the user in progress (a frame whose scroll position changed) and reports the ones
    /// that ended: their gesture, their smooth scroll and their <c>scrollend</c> event are complete.
    /// </summary>
    private void UpdateScrollTimings(long frameTimestamp)
    {
        if (_scrollTimingCount == 0)
            return;

        foreach (var state in EnumerateScrollStates())
        {
            if (state.ScrollTiming is not { } record)
                continue;

            if (state.Offset != record.LastFrameOffset)
            {
                record.FramesProduced++;
                record.FirstFrameTimestamp ??= frameTimestamp;
                record.LastFrameOffset = state.Offset;
            }

            if (state.SmoothScroll is null && state.UserGestureStart is null && !state.ChangedSinceScrollEnd)
            {
                ReportScrollTiming(state);
            }
        }
    }

    /// <summary>Ends the measure of the scroll of a scrolling box: a scroll that changed the scroll position is reported to the window of the document.</summary>
    private void ReportScrollTiming(ScrollState state)
    {
        if (state.ScrollTiming is not { } record)
            return;

        state.ScrollTiming = null;
        _scrollTimingCount--;

        if (record.LastChangeTimestamp == 0 || _document.RelevantWindow is not { } window || !_document.IsFullyActive)
            return;

        // A scroll that no frame displayed and that moved less than half a pixel (e.g. a scroll that came back to its start, or a
        // move of a fraction of a pixel stopped by another input) is not reported. A scroll that came back to its start after
        // frames displayed it (e.g. the snap of a gesture to its start position) is.
        var delta = state.Offset - record.StartOffset;
        var deltaX = (int)Math.Round(delta.X, MidpointRounding.AwayFromZero);
        var deltaY = (int)Math.Round(delta.Y, MidpointRounding.AwayFromZero);
        if (record.FramesProduced == 0 && deltaX == 0 && deltaY == 0)
            return;

        var startTime = window.ToRelativeHighResolutionTime(record.StartTimestamp);
        var duration = Math.Max(0, window.ToRelativeHighResolutionTime(record.LastChangeTimestamp) - startTime);
        var firstFrameTime = record.FirstFrameTimestamp is { } firstFrame ? window.ToRelativeHighResolutionTime(firstFrame) : 0;
        var framesExpected = Math.Max(record.FramesProduced, (uint)Math.Ceiling(duration / DomEventLoop.FrameDuration.TotalMilliseconds));
        var target = state.Element ?? (Node)_document;
        window.Performance.QueueScrollEntry(new PerformanceScrollTiming(window, startTime, duration, firstFrameTime, framesExpected, record.FramesProduced, deltaX, deltaY, record.Source, target));
    }

    /// <summary>Discards the measure of the scroll of a scrolling box that is not a scroll container anymore, without reporting it.</summary>
    private void DiscardScrollTiming(ScrollState state)
    {
        if (state.ScrollTiming is null)
            return;

        state.ScrollTiming = null;
        _scrollTimingCount--;
    }
}
