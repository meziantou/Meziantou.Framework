namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>
/// The state of a scroll progress timeline or of a view progress timeline at the last update of the timelines: the timelines
/// are sampled by the rendering steps (https://drafts.csswg.org/scroll-animations-1/#event-loop), so their current time stays
/// the same until the next rendering update. The positions are scroll offsets along the axis of the timeline, measured from
/// the scroll origin (https://drafts.csswg.org/css-overflow-3/#scroll-origin) in CSS px of the scroll container.
/// </summary>
internal sealed record ScrollTimelineSnapshot
{
    /// <summary>The state of an inactive timeline (https://drafts.csswg.org/web-animations-1/#inactive-timeline).</summary>
    public static ScrollTimelineSnapshot Inactive { get; } = new();

    /// <summary>Whether the timeline is active: its scroll container is scrollable in its axis and its 0% and 100% positions differ.</summary>
    public bool IsActive { get; init; }

    /// <summary>The scroll container whose scroll position drives the timeline (null for an inactive timeline without source).</summary>
    public Dom.Element? Source { get; init; }

    /// <summary>The current scroll offset of the scroll container.</summary>
    public double ScrollOffset { get; init; }

    /// <summary>The largest scroll offset of the scroll container (its smallest one is 0).</summary>
    public double MaxScrollOffset { get; init; }

    /// <summary>The effective zoom of the scroll container: the positions are divided by it (they are in its unzoomed CSS px).</summary>
    public double SourceZoom { get; init; } = 1;

    /// <summary>Whether the timeline is a view progress timeline (the subject and the visibility range are set).</summary>
    public bool IsView { get; init; }

    /// <summary>The position of the start border edge of the subject in the scrollport when the scroll offset is 0.</summary>
    public double SubjectStart { get; init; }

    /// <summary>The size of the border box of the subject along the axis.</summary>
    public double SubjectSize { get; init; }

    /// <summary>
    /// The scroll offsets at which the edges of a subject that moves with the scroll position (a sticky positioned subject, or a
    /// subject in a sticky positioned box) reach the edges of the visibility range, or null when its position does not depend on
    /// the scroll position (they are computed from <see cref="SubjectStart"/>).
    /// </summary>
    public ViewTimelineEdgeOffsets? EdgeOffsets { get; init; }

    /// <summary>The start edge of the view progress visibility range in the scrollport (the start inset).</summary>
    public double ViewStart { get; init; }

    /// <summary>The end edge of the view progress visibility range in the scrollport (the size of the scrollport minus the end inset).</summary>
    public double ViewEnd { get; init; }

    /// <summary>The scroll offset of the 0% position of the timeline: 0, or the start of the cover range of a view progress timeline.</summary>
    public double StartOffset => IsView ? EdgeOffsets?.StartAtViewEnd ?? SubjectStart - ViewEnd : 0;

    /// <summary>The scroll offset of the 100% position of the timeline: the largest scroll offset, or the end of the cover range of a view progress timeline.</summary>
    public double EndOffset => IsView ? EdgeOffsets?.EndAtViewStart ?? SubjectStart + SubjectSize - ViewStart : MaxScrollOffset;

    /// <summary>
    /// The current time of the timeline, a percentage (https://drafts.csswg.org/scroll-animations-1/#scroll-timeline-progress,
    /// https://drafts.csswg.org/scroll-animations-1/#view-timeline-progress).
    /// </summary>
    public double CurrentTime => (ScrollOffset - StartOffset) / (EndOffset - StartOffset) * 100;

    /// <summary>
    /// Returns the scroll offsets of the start and the end of a named timeline range
    /// (https://drafts.csswg.org/scroll-animations-1/#view-timelines-ranges). The ranges of a scroll progress timeline are the
    /// whole timeline.
    /// </summary>
    public (double Start, double End) GetRange(TimelineRangeName name)
    {
        if (!IsView || name == TimelineRangeName.None)
            return (StartOffset, EndOffset);

        var coverStart = StartOffset;
        var coverEnd = EndOffset;

        // The subject's start edge coincides with the start edge of the visibility range, its end edge with the end edge
        var subjectStartAtViewStart = EdgeOffsets?.StartAtViewStart ?? SubjectStart - ViewStart;
        var subjectEndAtViewEnd = EdgeOffsets?.EndAtViewEnd ?? SubjectStart + SubjectSize - ViewEnd;
        var containStart = Math.Min(subjectStartAtViewStart, subjectEndAtViewEnd);
        var containEnd = Math.Max(subjectStartAtViewStart, subjectEndAtViewEnd);
        return name switch
        {
            TimelineRangeName.Contain => (containStart, containEnd),
            TimelineRangeName.Entry => (coverStart, containStart),
            TimelineRangeName.Exit => (containEnd, coverEnd),
            TimelineRangeName.EntryCrossing => (coverStart, subjectEndAtViewEnd),
            TimelineRangeName.ExitCrossing => (subjectStartAtViewStart, coverEnd),
            TimelineRangeName.Scroll => (0, MaxScrollOffset),
            _ => (coverStart, coverEnd),
        };
    }

    /// <summary>
    /// Returns the position of a boundary of an animation attachment range as a fraction of the timeline (0 for its 0%
    /// position, 1 for its 100% position): <c>normal</c> is the start or the end of the timeline.
    /// </summary>
    public double GetFraction(TimelineRangeBoundary? boundary, bool isEnd)
    {
        if (boundary is null)
            return isEnd ? 1 : 0;

        if (!IsActive)
        {
            // The ranges are unknown: only an offset in percents of the whole timeline is meaningful
            if (boundary.Name == TimelineRangeName.None && boundary.Offset.Calc is null && boundary.Offset.Length == 0)
                return boundary.Offset.Percentage / 100;

            return isEnd ? 1 : 0;
        }

        // The lengths of the offset are zoomed by the element that declares the range, the positions are in the unzoomed px of the source
        var (start, end) = GetRange(boundary.Name);
        var scale = boundary.Zoom / SourceZoom;
        var position = start + (boundary.Offset.Resolve((end - start) / scale) * scale);
        return (position - StartOffset) / (EndOffset - StartOffset);
    }
}

/// <summary>
/// The scroll offsets at which the start and end edges of the subject of a view progress timeline reach the start and end
/// edges of its visibility range (the subject moves with the scroll position when it is sticky positioned).
/// </summary>
/// <param name="StartAtViewEnd">The start edge of the subject reaches the end edge of the range: the start of the cover range.</param>
/// <param name="EndAtViewEnd">The end edge of the subject reaches the end edge of the range.</param>
/// <param name="StartAtViewStart">The start edge of the subject reaches the start edge of the range.</param>
/// <param name="EndAtViewStart">The end edge of the subject reaches the start edge of the range: the end of the cover range.</param>
internal sealed record ViewTimelineEdgeOffsets(double StartAtViewEnd, double EndAtViewEnd, double StartAtViewStart, double EndAtViewStart);
