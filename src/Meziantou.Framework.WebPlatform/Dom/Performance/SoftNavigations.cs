namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The contentful paint of the content modified by a user interaction (https://wicg.github.io/soft-navigations/#sec-interaction-contentful-paint):
/// a new largest image or text among the content that the tasks of the interaction (its event listeners and the callbacks they
/// scheduled) inserted or modified, reported to the <c>interaction-contentful-paint</c> performance observers of its window.
/// </summary>
/// <seealso href="https://wicg.github.io/soft-navigations/#interactioncontentfulpaint"/>
[IdlInterface("InteractionContentfulPaint", "https://wicg.github.io/soft-navigations/#interactioncontentfulpaint")]
public sealed class InteractionContentfulPaint : PerformanceEntry
{
    /// <summary>The entry type of the interaction contentful paint entries.</summary>
    internal const string InteractionContentfulPaintEntryType = "interaction-contentful-paint";

    /// <summary>The maximum size of the performance entry buffer of the interaction contentful paints.</summary>
    internal const int MaxBufferSize = 150;

    private readonly PaintTimingInfo _paintTimingInfo;

    internal InteractionContentfulPaint(GlobalScope global, InteractionContext context, LargestContentfulPaint largestContentfulPaint, PaintTimingInfo paintTimingInfo)
        : base(global, InteractionContentfulPaintEntryType, name: "", context.StartTime, paintTimingInfo.DefaultPaintTimestamp - context.StartTime)
    {
        InteractionId = context.Id;
        LargestContentfulPaint = largestContentfulPaint;
        _paintTimingInfo = paintTimingInfo;
    }

    /// <summary>Gets the largest contentful paint candidate of the interaction painted by the rendering update (not reported to the <c>largest-contentful-paint</c> observers).</summary>
    /// <seealso href="https://wicg.github.io/soft-navigations/#dom-interactioncontentfulpaint-largestcontentfulpaint"/>
    [IdlMember("largestContentfulPaint", "https://wicg.github.io/soft-navigations/#dom-interactioncontentfulpaint-largestcontentfulpaint")]
    public LargestContentfulPaint LargestContentfulPaint { get; }

    /// <summary>Gets the identifier of the interaction (<see cref="PerformanceEventTiming.InteractionId"/>).</summary>
    /// <seealso href="https://wicg.github.io/soft-navigations/#dom-interactioncontentfulpaint-interactionid"/>
    [IdlMember("interactionId", "https://wicg.github.io/soft-navigations/#dom-interactioncontentfulpaint-interactionid")]
    public ulong InteractionId { get; }

    /// <summary>Gets the time of the end of the rendering update that painted the content.</summary>
    /// <seealso href="https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime"/>
    [IdlMember("paintTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime")]
    public double PaintTime => _paintTimingInfo.RenderingUpdateEndTime;

    /// <summary>Gets the time the frame was presented to the user: null, the presentation time is not supported.</summary>
    /// <seealso href="https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime"/>
    [IdlMember("presentationTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime")]
    public double? PresentationTime => _paintTimingInfo.PresentationTime;

    /// <summary>The paint timing info of the rendering update that painted the content.</summary>
    internal PaintTimingInfo PaintTimingInfo => _paintTimingInfo;
}

/// <summary>
/// A soft navigation (https://wicg.github.io/soft-navigations/#sec-soft-navs): a user interaction that changed the URL of the
/// document (<c>history.pushState()</c>, a fragment navigation, a traversal, a navigation intercepted by the Navigation API) and
/// whose modifications of the document were painted. It is reported to the <c>soft-navigation</c> performance observers of its
/// window, and the entries queued from then on have a new <see cref="PerformanceEntry.NavigationId"/>.
/// </summary>
/// <seealso href="https://wicg.github.io/soft-navigations/#performancesoftnavigation"/>
[IdlInterface("PerformanceSoftNavigation", "https://wicg.github.io/soft-navigations/#performancesoftnavigation")]
public sealed class PerformanceSoftNavigation : PerformanceEntry
{
    /// <summary>The entry type of the soft navigation entries.</summary>
    internal const string SoftNavigationEntryType = "soft-navigation";

    /// <summary>The maximum size of the performance entry buffer of the soft navigations.</summary>
    internal const int MaxBufferSize = 50;

    private readonly InteractionContext _context;
    private readonly PaintTimingInfo _paintTimingInfo;

    internal PerformanceSoftNavigation(GlobalScope global, InteractionContext context, PaintTimingInfo paintTimingInfo)
        : base(global, SoftNavigationEntryType, context.FirstUrl ?? "", context.StartTime, paintTimingInfo.DefaultPaintTimestamp - context.StartTime)
    {
        _context = context;
        _paintTimingInfo = paintTimingInfo;
        NavigationType = context.NavigationType ?? NavigationType.Push;
    }

    /// <summary>Gets the type of the first URL change of the interaction.</summary>
    /// <seealso href="https://wicg.github.io/soft-navigations/#dom-performancesoftnavigation-navigationtype"/>
    [IdlMember("navigationType", "https://wicg.github.io/soft-navigations/#dom-performancesoftnavigation-navigationtype")]
    public NavigationType NavigationType { get; }

    /// <summary>Gets the identifier of the interaction (<see cref="PerformanceEventTiming.InteractionId"/>).</summary>
    /// <seealso href="https://wicg.github.io/soft-navigations/#dom-performancesoftnavigation-interactionid"/>
    [IdlMember("interactionId", "https://wicg.github.io/soft-navigations/#dom-performancesoftnavigation-interactionid")]
    public ulong InteractionId => _context.Id;

    /// <summary>Gets the time of the end of the rendering update of the first contentful paint of the interaction.</summary>
    /// <seealso href="https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime"/>
    [IdlMember("paintTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-painttime")]
    public double PaintTime => _paintTimingInfo.RenderingUpdateEndTime;

    /// <summary>Gets the time the frame was presented to the user: null, the presentation time is not supported.</summary>
    /// <seealso href="https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime"/>
    [IdlMember("presentationTime", "https://w3c.github.io/paint-timing/#dom-painttimingmixin-presentationtime")]
    public double? PresentationTime => _paintTimingInfo.PresentationTime;

    /// <summary>Returns the largest contentful paint of the interaction so far (it changes while the interaction paints larger content).</summary>
    /// <seealso href="https://wicg.github.io/soft-navigations/#dom-performancesoftnavigation-getlargestinteractioncontentfulpaint"/>
    [IdlMember("getLargestInteractionContentfulPaint", "https://wicg.github.io/soft-navigations/#dom-performancesoftnavigation-getlargestinteractioncontentfulpaint")]
    public InteractionContentfulPaint? GetLargestInteractionContentfulPaint() => _context.LargestContentfulPaint;
}

/// <summary>
/// An interaction context (https://wicg.github.io/soft-navigations/#interactioncontext): the state of a user interaction used to
/// detect a soft navigation. The current interaction context (the internal <c>[[ActiveInteractionContext]]</c> AsyncContext
/// variable of the specification) is set while the events of the interaction are dispatched, and propagated to the tasks and the
/// callbacks scheduled from them: the timers, the tasks and the microtasks, the promise reactions, the animation frame and idle
/// callbacks, the tasks of the schedulers, and the asynchronous operations of the library (e.g. the fetches of the scripts and the
/// style sheets inserted by the interaction, whose load events are queued in its context).
/// </summary>
internal sealed class InteractionContext
{
    // An AsyncLocal flows with the .NET asynchronous operations (e.g. a fetch started by a listener): the tasks that they queue
    // when they complete are attributed to the interaction. It is null on the threads that do not run a task of an interaction.
    private static readonly AsyncLocal<InteractionContext?> CurrentContext = new();

    public InteractionContext(ulong id, Document document, double startTime)
    {
        Id = id;
        Document = document;
        StartTime = startTime;
    }

    /// <summary>Gets the current interaction context (https://wicg.github.io/soft-navigations/#get-the-current-interaction-context, without the document check).</summary>
    public static InteractionContext? Current => CurrentContext.Value;

    /// <summary>The interaction id.</summary>
    public ulong Id { get; }

    /// <summary>The context document.</summary>
    public Document Document { get; }

    /// <summary>The start time: the time of the first event of the interaction.</summary>
    public double StartTime { get; }

    /// <summary>The navigation type of the first URL change.</summary>
    public NavigationType? NavigationType { get; set; }

    /// <summary>The first URL value.</summary>
    public string? FirstUrl { get; set; }

    /// <summary>The last URL value.</summary>
    public string? LastUrl { get; set; }

    /// <summary>The first scroll timestamp, or 0.</summary>
    public double FirstScrollTimestamp { get; set; }

    /// <summary>The first contentful paint.</summary>
    public InteractionContentfulPaint? FirstContentfulPaint { get; set; }

    /// <summary>The largest contentful paint.</summary>
    public InteractionContentfulPaint? LargestContentfulPaint { get; set; }

    /// <summary>The size of the current largest contentful paint candidate, or 0.</summary>
    public double CurrentCandidateSize { get; set; }

    /// <summary>Whether the soft navigation entry was emitted.</summary>
    public bool Emitted { get; set; }

    /// <summary>Sets the current interaction context until the scope is disposed.</summary>
    public static Scope Enter(InteractionContext? context)
    {
        var previous = CurrentContext.Value;
        if (!ReferenceEquals(previous, context))
        {
            CurrentContext.Value = context;
        }

        return new Scope(previous, context);
    }

    /// <summary>Returns a callback that runs with the current interaction context, or the callback itself when there is none.</summary>
    public static Action Wrap(Action callback)
    {
        if (CurrentContext.Value is not { } context)
            return callback;

        return () =>
        {
            using var scope = Enter(context);
            callback();
        };
    }

    /// <summary>The scope of <see cref="Enter"/>: it restores the previous interaction context.</summary>
    internal readonly struct Scope(InteractionContext? previous, InteractionContext? current) : IDisposable
    {
        public void Dispose()
        {
            if (!ReferenceEquals(previous, current))
            {
                CurrentContext.Value = previous;
            }
        }
    }
}
