using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Animations;

/// <summary>The axis of a scroll-driven timeline (https://drafts.csswg.org/scroll-animations-1/#enumdef-scrollaxis).</summary>
[IdlInterface("ScrollAxis", "https://drafts.csswg.org/scroll-animations-1/#enumdef-scrollaxis")]
public enum ScrollAxis
{
    /// <summary>The block axis of the scroll container.</summary>
    [IdlMember("block", "https://drafts.csswg.org/scroll-animations-1/#valdef-scroll-block")]
    Block,

    /// <summary>The inline axis of the scroll container.</summary>
    [IdlMember("inline", "https://drafts.csswg.org/scroll-animations-1/#valdef-scroll-inline")]
    Inline,

    /// <summary>The horizontal axis.</summary>
    [IdlMember("x", "https://drafts.csswg.org/scroll-animations-1/#valdef-scroll-x")]
    X,

    /// <summary>The vertical axis.</summary>
    [IdlMember("y", "https://drafts.csswg.org/scroll-animations-1/#valdef-scroll-y")]
    Y,
}

/// <summary>The options of a scroll timeline (https://drafts.csswg.org/scroll-animations-1/#dictdef-scrolltimelineoptions).</summary>
[IdlInterface("ScrollTimelineOptions", "https://drafts.csswg.org/scroll-animations-1/#dictdef-scrolltimelineoptions")]
public sealed class ScrollTimelineOptions
{
    /// <summary>Gets the scroll container whose scroll position drives the timeline; the scrolling element of the document when not set.</summary>
    [IdlMember("source", "https://drafts.csswg.org/scroll-animations-1/#dom-scrolltimelineoptions-source")]
    public Element? Source
    {
        get;
        init
        {
            field = value;
            HasSource = true;
        }
    }

    /// <summary>Whether <see cref="Source"/> is set (null is a valid source).</summary>
    internal bool HasSource { get; private init; }

    /// <summary>Gets the axis of the timeline.</summary>
    [IdlMember("axis", "https://drafts.csswg.org/scroll-animations-1/#dom-scrolltimelineoptions-axis")]
    public ScrollAxis Axis { get; init; }
}

/// <summary>The scroll container referenced by <c>scroll()</c> (https://drafts.csswg.org/scroll-animations-1/#scroll-notation).</summary>
internal enum ScrollTimelineScroller
{
    /// <summary>The source is given (the script API and the named scroll progress timelines).</summary>
    Source,

    /// <summary>The nearest ancestor scroll container of the element.</summary>
    Nearest,

    /// <summary>The document viewport.</summary>
    Root,

    /// <summary>The element itself.</summary>
    Self,
}

/// <summary>
/// A scroll progress timeline (https://drafts.csswg.org/scroll-animations-1/#scrolltimeline-interface): its current time is
/// the scroll progress of a scroll container along an axis, as a percentage. It is a progress-based timeline whose duration
/// is 100%.
/// </summary>
/// <remarks>
/// The timeline is sampled by the rendering steps of the event loop (https://drafts.csswg.org/scroll-animations-1/#event-loop):
/// its current time is the one of the last rendering update, so it does not change while a script scrolls. A timeline created
/// by the script API is sampled when it is created, with the current layout.
/// </remarks>
[IdlInterface("ScrollTimeline", "https://drafts.csswg.org/scroll-animations-1/#scrolltimeline")]
public class ScrollTimeline : AnimationTimeline
{
    private readonly Document _document;
    private readonly Element? _source;
    private readonly ScrollTimelineScroller _scroller;
    private readonly List<Animation> _animations = [];
    private ScrollTimelineSnapshot _snapshot = ScrollTimelineSnapshot.Inactive;
    private bool _sampled;

    /// <summary>
    /// Creates a scroll timeline (https://drafts.csswg.org/scroll-animations-1/#dom-scrolltimeline-scrolltimeline): its source is
    /// the <c>source</c> of the options, or the scrolling element of the document of the current global object. The timeline
    /// belongs to the document of its source (the document of the current global object without source).
    /// </summary>
    [IdlMember("constructor", "https://drafts.csswg.org/scroll-animations-1/#dom-scrolltimeline-scrolltimeline")]
    public ScrollTimeline(ScrollTimelineOptions? options = null)
        : this(options is { HasSource: true, Source: { } source } ? source.NodeDocument : Document.CurrentGlobalDocument, options is { HasSource: true } ? options.Source : Document.CurrentGlobalDocument.ScrollingElement, options?.Axis ?? ScrollAxis.Block, ScrollTimelineScroller.Source)
    {
        UpdateSnapshot();
    }

    /// <summary>Creates a timeline whose source is an element (null for an inactive timeline) or is looked up from it (<c>scroll()</c>).</summary>
    private protected ScrollTimeline(Document document, Element? source, ScrollAxis axis, ScrollTimelineScroller scroller, string? pseudoElement = null)
    {
        _document = document;
        _source = source;
        PseudoElement = pseudoElement;
        _scroller = scroller;
        Axis = axis;
        document.Animations.RegisterScrollDrivenTimeline(this);
    }

    /// <summary>Creates the timeline of a <c>scroll()</c> function of <c>animation-timeline</c>, or of a named scroll progress timeline (the scroller is then <see cref="ScrollTimelineScroller.Source"/>).</summary>
    internal static ScrollTimeline Create(Element element, string? pseudoElement, ScrollTimelineScroller scroller, ScrollAxis axis) => new(element.NodeDocument, element, axis, scroller, pseudoElement);

    /// <summary>Creates an inactive timeline (the timeline of a CSS animation whose timeline name matches no timeline).</summary>
    internal static ScrollTimeline CreateInactive(Document document) => new(document, source: null, ScrollAxis.Block, ScrollTimelineScroller.Source) { IsUnresolved = true };

    /// <summary>
    /// Whether the timeline represents an unresolved timeline name of a CSS animation: it is inactive, and the timeline of the
    /// animation is null in the object model (https://github.com/w3c/csswg-drafts/issues/13807).
    /// </summary>
    internal bool IsUnresolved { get; private init; }

    /// <summary>
    /// Gets the scroll container whose scroll position drives the timeline (https://drafts.csswg.org/scroll-animations-1/#dom-scrolltimeline-source).
    /// The source of a <c>scroll(nearest)</c> timeline is looked up from the current layout.
    /// </summary>
    [IdlMember("source", "https://drafts.csswg.org/scroll-animations-1/#dom-scrolltimeline-source")]
    public virtual Element? Source => ResolveSource();

    /// <summary>Gets the axis of the timeline (https://drafts.csswg.org/scroll-animations-1/#dom-scrolltimeline-axis).</summary>
    [IdlMember("axis", "https://drafts.csswg.org/scroll-animations-1/#dom-scrolltimeline-axis")]
    public ScrollAxis Axis { get; internal set; }

    /// <summary>The kind of the source of the timeline.</summary>
    internal ScrollTimelineScroller Scroller => _scroller;

    /// <summary>The element the timeline was created for: the source, or the element of a <c>scroll()</c> function.</summary>
    internal Element? Reference => _source;

    /// <summary>The pseudo-element of <see cref="Reference"/> whose style created the timeline (e.g. "before"), or null.</summary>
    internal string? PseudoElement { get; }

    internal override Document Document => _document;

    internal override bool IsMonotonicallyIncreasing => false;

    internal override double? CurrentTimeValue => _snapshot.IsActive ? _snapshot.CurrentTime : null;

    internal override bool IsAtTimelineBoundary => _snapshot.IsActive && (_snapshot.ScrollOffset <= 0 || _snapshot.ScrollOffset >= _snapshot.MaxScrollOffset);

    /// <summary>The state of the timeline at its last update.</summary>
    internal ScrollTimelineSnapshot Snapshot => _snapshot;

    /// <summary>The animations associated with the timeline.</summary>
    internal List<Animation> Animations => _animations;

    // The times of a progress-based timeline are not related to the time origin of the document: the events have no scheduled time
    internal override double? ToOriginRelativeTime(double? time) => null;

    internal override double GetAttachmentFraction(TimelineRangeBoundary? boundary, bool isEnd) => _snapshot.GetFraction(boundary, isEnd);

    internal override void OnAnimationAttached(Animation animation)
    {
        if (!_animations.Contains(animation))
        {
            _animations.Add(animation);
        }
    }

    internal override void OnAnimationDetached(Animation animation) => _animations.Remove(animation);

    /// <summary>Returns the scroll container of the timeline.</summary>
    private protected Element? ResolveSource() => ResolveSourceAndAxis().Element;

    private ScrollTimelineSource ResolveSourceAndAxis()
    {
        if (_source is null)
            return default;

        return _scroller switch
        {
            ScrollTimelineScroller.Nearest => ScrollTimelineGeometry.FindNearestScrollContainer(_source, PseudoElement, Axis),
            ScrollTimelineScroller.Root => new ScrollTimelineSource(ScrollTimelineGeometry.GetViewportSource(_source.NodeDocument), Horizontal: null),
            _ => new ScrollTimelineSource(_source, Horizontal: null),
        };
    }

    /// <summary>Computes the state of the timeline from the current layout.</summary>
    private protected virtual ScrollTimelineSnapshot ComputeSnapshot()
    {
        if (!IsEnabled(_document))
            return ScrollTimelineSnapshot.Inactive;

        var source = ResolveSourceAndAxis();
        return ScrollTimelineGeometry.ComputeScrollTimeline(source.Element, Axis, source.Horizontal);
    }

    /// <summary>Whether the scroll-driven animations are enabled in the browsing context of the document.</summary>
    internal static bool IsEnabled(Document document) => document.BrowsingContext is not { Options.EffectiveFeatures.ScrollDrivenAnimationsEnabled: false };

    /// <summary>
    /// Updates the current time and the ranges of the timeline from the current layout (the layout is computed when it is out of
    /// date). Returns whether they changed. When <paramref name="staleOnly"/> is true, the timeline is only updated when it is
    /// stale (https://drafts.csswg.org/scroll-animations-1/#stale-timelines): it was never sampled, or its source, its activity
    /// or its ranges changed; a change of the scroll position alone waits for the next rendering update.
    /// </summary>
    internal bool UpdateSnapshot(bool staleOnly = false)
    {
        var snapshot = ComputeSnapshot();
        if (snapshot == _snapshot)
        {
            _sampled = true;
            return false;
        }

        if (staleOnly && _sampled && snapshot with { ScrollOffset = _snapshot.ScrollOffset } == _snapshot)
            return false;

        _snapshot = snapshot;
        _sampled = true;
        return true;
    }
}
