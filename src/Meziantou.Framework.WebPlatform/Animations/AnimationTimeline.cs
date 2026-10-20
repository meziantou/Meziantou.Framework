using Meziantou.Framework.WebPlatform.Css.TypedOm;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>A source of time values for animations (https://drafts.csswg.org/web-animations-1/#timelines).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#animationtimeline"/>
[IdlInterface("AnimationTimeline", "https://drafts.csswg.org/web-animations-1/#animationtimeline")]
public abstract class AnimationTimeline : IRealmBoundPlatformObject
{
    private protected AnimationTimeline()
    {
    }

    /// <summary>The current time of the timeline in milliseconds, or null when the timeline is inactive.</summary>
    internal abstract double? CurrentTimeValue { get; }

    /// <summary>The document of the timeline, used by its animations for timing (https://drafts.csswg.org/web-animations-1/#document-for-timing).</summary>
    internal abstract Document? Document { get; }

    /// <summary>Whether the timeline is active (https://drafts.csswg.org/web-animations-1/#inactive-timeline).</summary>
    internal bool IsActive => CurrentTimeValue is not null;

    /// <summary>
    /// Whether the timeline is monotonically increasing (https://drafts.csswg.org/web-animations-1/#monotonically-increasing-timeline):
    /// false for a progress-based timeline (https://drafts.csswg.org/web-animations-2/#progress-based-timeline), whose times are
    /// percentages and whose duration is 100%.
    /// </summary>
    internal virtual bool IsMonotonicallyIncreasing => true;

    /// <summary>Whether the timeline is a progress-based timeline: its time values are percentages.</summary>
    internal bool IsProgressBased => !IsMonotonicallyIncreasing;

    /// <summary>Returns a time value of the timeline as a <c>CSSNumberish</c>: a percentage for a progress-based timeline, milliseconds otherwise.</summary>
    internal CssNumberish ToCssNumberish(double time) => IsProgressBased ? new CssUnitValue(time, "percent") : time;

    /// <summary>
    /// Returns the position of a boundary of an animation attachment range on the timeline, as a fraction of the timeline
    /// (https://drafts.csswg.org/scroll-animations-1/#named-range-animation-declaration): the ranges only apply to the
    /// progress-based timelines.
    /// </summary>
    internal virtual double GetAttachmentFraction(TimelineRangeBoundary? boundary, bool isEnd) => isEnd ? 1 : 0;

    /// <summary>
    /// Whether the current time of the timeline is its minimum or its maximum time (https://drafts.csswg.org/web-animations-2/#at-timeline-boundary):
    /// the boundaries of the active interval are then inclusive. A monotonically increasing timeline has no boundary.
    /// </summary>
    internal virtual bool IsAtTimelineBoundary => false;

    /// <summary>Called when an animation is associated with the timeline.</summary>
    internal virtual void OnAnimationAttached(Animation animation)
    {
    }

    /// <summary>Called when an animation is no longer associated with the timeline.</summary>
    internal virtual void OnAnimationDetached(Animation animation)
    {
    }

    Window? IRealmBoundPlatformObject.RealmWindow => Document?.RelevantWindow;

    /// <summary>
    /// Gets the current time of the timeline, in milliseconds (https://drafts.csswg.org/web-animations-1/#dom-animationtimeline-currenttime),
    /// or null when the timeline is inactive.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animationtimeline-currenttime"/>
    [IdlMember("currentTime", "https://drafts.csswg.org/web-animations-2/#dom-animationtimeline-currenttime")]
    public CssNumberish? CurrentTime => CurrentTimeValue is { } time ? ToCssNumberish(time) : (CssNumberish?)null;

    /// <summary>
    /// Gets the duration of the timeline (https://drafts.csswg.org/web-animations-2/#dom-animationtimeline-duration): 100% for a
    /// progress-based timeline, null for a document timeline.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animationtimeline-duration"/>
    [IdlMember("duration", "https://drafts.csswg.org/web-animations-2/#dom-animationtimeline-duration")]
    public CssNumberish? Duration => IsProgressBased ? new CssUnitValue(100, "percent") : (CssNumberish?)null;

    /// <summary>Creates an animation of the effect on the timeline and plays it (https://drafts.csswg.org/web-animations-2/#dom-animationtimeline-play).</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-2/#dom-animationtimeline-play"/>
    [IdlMember("play", "https://drafts.csswg.org/web-animations-2/#dom-animationtimeline-play")]
    public Animation Play(AnimationEffect? effect = null)
    {
        var animation = new Animation(effect, this);
        animation.Play();
        return animation;
    }

    /// <summary>Converts a time value of the timeline to a time relative to the time origin of its document (https://drafts.csswg.org/web-animations-1/#origin-relative-time).</summary>
    internal virtual double? ToOriginRelativeTime(double? time) => time;
}

/// <summary>The options of a document timeline (https://drafts.csswg.org/web-animations-1/#dictdef-documenttimelineoptions).</summary>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#dictdef-documenttimelineoptions"/>
[IdlInterface("DocumentTimelineOptions", "https://drafts.csswg.org/web-animations-1/#dictdef-documenttimelineoptions")]
public sealed class DocumentTimelineOptions
{
    /// <summary>Gets the zero time of the timeline, in milliseconds relative to the time origin of the document.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-documenttimelineoptions-origintime"/>
    [IdlMember("originTime", "https://drafts.csswg.org/web-animations-1/#dom-documenttimelineoptions-origintime")]
    public double OriginTime { get; init; }
}

/// <summary>
/// A timeline whose time is the time of the frames of its document (https://drafts.csswg.org/web-animations-1/#document-timelines):
/// it is updated when the event loop updates the rendering, and stays the same during a task.
/// </summary>
/// <remarks>
/// The timeline is inactive (its current time is null) when its document is not fully active or has no browsing context.
/// </remarks>
/// <seealso href="https://drafts.csswg.org/web-animations-1/#documenttimeline"/>
[IdlInterface("DocumentTimeline", "https://drafts.csswg.org/web-animations-1/#documenttimeline")]
public sealed class DocumentTimeline : AnimationTimeline
{
    private readonly Document _document;
    private readonly double _originTime;

    /// <summary>Creates a timeline of the document of the current global object whose zero time is <see cref="DocumentTimelineOptions.OriginTime"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/web-animations-1/#dom-documenttimeline-documenttimeline"/>
    [IdlMember("constructor", "https://drafts.csswg.org/web-animations-1/#dom-documenttimeline-documenttimeline")]
    public DocumentTimeline(DocumentTimelineOptions? options = null)
        : this(Document.CurrentGlobalDocument, options?.OriginTime ?? 0)
    {
    }

    internal DocumentTimeline(Document document, double originTime)
    {
        _document = document;
        _originTime = originTime;
    }

    internal override Document Document => _document;

    internal override double? CurrentTimeValue
    {
        get
        {
            if (!_document.IsFullyActive || _document.BrowsingContext is null)
                return null;

            return _document.Animations.GetTimelineTime() - _originTime;
        }
    }

    internal override double? ToOriginRelativeTime(double? time) => time + _originTime;
}
