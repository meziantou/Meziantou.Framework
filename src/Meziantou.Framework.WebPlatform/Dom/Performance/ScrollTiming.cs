using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A scroll of the user (https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md):
/// the frames that displayed a scroll of a scrolling box by the wheel, the keys, a touch or a scrollbar, from its first input
/// to its end (its <c>scrollend</c> event), reported to the <c>scroll</c> performance observers of its window.
/// </summary>
/// <remarks>The entries are measured on the clock of the event loop: the frames are its rendering updates.</remarks>
/// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#performancescrolltiming-interface"/>
[IdlInterface("PerformanceScrollTiming", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#performancescrolltiming-interface")]
[Experimental(ExperimentalApis.ScrollTiming, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class PerformanceScrollTiming : PerformanceEntry
{
    /// <summary>The entry type and the name of the scroll entries.</summary>
    internal const string ScrollEntryType = "scroll";

    /// <summary>The maximum size of the performance entry buffer of the scrolls (the explainer does not define it: the one of the layout shifts).</summary>
    internal const int MaxBufferSize = 150;

    private readonly Node? _target;

    internal PerformanceScrollTiming(GlobalScope global, double startTime, double duration, double firstFrameTime, uint framesExpected, uint framesProduced, int deltaX, int deltaY, string scrollSource, Node? target)
        : base(global, ScrollEntryType, ScrollEntryType, startTime, duration)
    {
        FirstFrameTime = firstFrameTime;
        FramesExpected = framesExpected;
        FramesProduced = framesProduced;
        DeltaX = deltaX;
        DeltaY = deltaY;
        ScrollSource = scrollSource;
        _target = target;
    }

    /// <summary>Gets <c>scroll</c>.</summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("name", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public new string Name => base.Name;

    /// <summary>Gets <c>scroll</c>.</summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("entryType", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public new string EntryType => base.EntryType;

    /// <summary>Gets the time of the first input of the scroll, in milliseconds relative to the time origin of the window.</summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("startTime", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public new double StartTime => base.StartTime;

    /// <summary>Gets the time from the start of the scroll to its last change of the scroll position (including the animation of a smooth scroll and the snap of its end).</summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("duration", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public new double Duration => base.Duration;

    /// <summary>Gets the time of the first frame (rendering update) that displayed the scroll.</summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("firstFrameTime", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public double FirstFrameTime { get; }

    /// <summary>
    /// Gets the number of frames at 60 Hz during the scroll (<c>ceil(duration / 16.67)</c>), at least the number of frames
    /// produced (an instant scroll has no duration but is displayed by a frame).
    /// </summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("framesExpected", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public uint FramesExpected { get; }

    /// <summary>Gets the number of frames (rendering updates) whose scroll position differed from the one of the previous frame.</summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("framesProduced", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public uint FramesProduced { get; }

    /// <summary>Gets 0: the contents are painted with the frames (there is no checkerboarding).</summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("checkerboardTime", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public double CheckerboardTime => 0;

    /// <summary>Gets the horizontal distance of the scroll, in CSS pixels (positive to the right).</summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("deltaX", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public int DeltaX { get; }

    /// <summary>Gets the vertical distance of the scroll, in CSS pixels (positive downwards).</summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("deltaY", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public int DeltaY { get; }

    /// <summary>Gets the input of the scroll: <c>wheel</c>, <c>keyboard</c>, <c>touch</c>, or <c>other</c> (a scrollbar).</summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("scrollSource", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public string ScrollSource { get; }

    /// <summary>
    /// Gets the scrolled node: the scroll container, or the document for the viewport. It is null when the node is not
    /// connected anymore or is in a shadow tree.
    /// </summary>
    /// <seealso href="https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference"/>
    [IdlMember("target", "https://github.com/MicrosoftEdge/MSEdgeExplainers/blob/main/PerformanceScrollTiming/explainer.md#attribute-reference")]
    public Node? Target => _target is { IsConnected: true } target && ReferenceEquals(target.GetRoot(), target.NodeDocument) ? target : null;
}
