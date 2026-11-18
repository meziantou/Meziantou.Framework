using Meziantou.Framework.WebPlatform.Dom;

#pragma warning disable WEBPLATEXP0021 // The scroll performance timing is a proposal

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

    /// <summary>Returns the source of the scroll performance timing of a kind of scroll of the user.</summary>
    internal static string GetScrollSource(UserScrollKind kind) => kind switch
    {
        UserScrollKind.Wheel => "wheel",
        UserScrollKind.Directional => "keyboard",
        UserScrollKind.DirectManipulation => "touch",
        _ => "other",
    };

    /// <summary>
    /// Starts (or continues) the measure of a scroll of the user of a scrolling box, before it scrolls by a delta: the scroll
    /// in progress continues when the input has the same source and does not reverse its direction.
    /// </summary>
    internal void TrackUserScroll(ScrollState state, string source, PhysicalOffset delta)
    {
        if (_document.EventLoop is not { } eventLoop || _document.RelevantWindow is not { } window || !window.Options.EffectiveFeatures.ScrollTimingEnabled)
            return;

        if (state.ScrollTiming is { } current)
        {
            // A scroll whose position did not change yet (e.g. a key at the limit of the box) is started again
            var moved = state.Offset - current.StartOffset;
            var reversed = (moved.X * delta.X < 0) || (moved.Y * delta.Y < 0);
            if (current.LastChangeTimestamp != 0 && current.Source == source && !reversed)
                return;

            ReportScrollTiming(state);
        }

        state.ScrollTiming = new ScrollTimingRecord(source, state.Offset, eventLoop.TimeProvider.GetTimestamp());
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
        if (record.LastChangeTimestamp == 0 || state.Offset == record.StartOffset || _document.RelevantWindow is not { } window || !_document.IsFullyActive)
            return;

        var startTime = window.ToRelativeHighResolutionTime(record.StartTimestamp);
        var duration = Math.Max(0, window.ToRelativeHighResolutionTime(record.LastChangeTimestamp) - startTime);
        var firstFrameTime = record.FirstFrameTimestamp is { } firstFrame ? window.ToRelativeHighResolutionTime(firstFrame) : 0;
        var framesExpected = Math.Max(record.FramesProduced, (uint)Math.Ceiling(duration / DomEventLoop.FrameDuration.TotalMilliseconds));
        var delta = state.Offset - record.StartOffset;
        var target = state.Element ?? (Node)_document;
        window.Performance.QueueScrollEntry(new PerformanceScrollTiming(window, startTime, duration, firstFrameTime, framesExpected, record.FramesProduced,
            (int)Math.Round(delta.X, MidpointRounding.AwayFromZero), (int)Math.Round(delta.Y, MidpointRounding.AwayFromZero), record.Source, target));
    }
}
