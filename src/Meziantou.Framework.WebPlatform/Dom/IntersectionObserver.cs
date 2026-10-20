using System.Collections;
using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/IntersectionObserver/#callbackdef-intersectionobservercallback</summary>
/// <param name="entries">The entries of the targets whose intersection with the root changed.</param>
/// <param name="observer">The observer.</param>
/// <seealso href="https://w3c.github.io/IntersectionObserver/#callbackdef-intersectionobservercallback"/>
[IdlInterface("IntersectionObserverCallback", "https://w3c.github.io/IntersectionObserver/#callbackdef-intersectionobservercallback")]
public delegate void IntersectionObserverCallback(IReadOnlyList<IntersectionObserverEntry> entries, IntersectionObserver observer);

/// <summary>The options of an <see cref="IntersectionObserver"/> (https://w3c.github.io/IntersectionObserver/#dictdef-intersectionobserverinit).</summary>
/// <seealso href="https://w3c.github.io/IntersectionObserver/#dictdef-intersectionobserverinit"/>
[IdlInterface("IntersectionObserverInit", "https://w3c.github.io/IntersectionObserver/#dictdef-intersectionobserverinit")]
public sealed class IntersectionObserverInit
{
    /// <summary>Gets or sets the intersection root: an <see cref="Element"/> or a <see cref="Document"/>, or null for the implicit root (the viewport of the top-level browsing context).</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-root"/>
    [IdlMember("root", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-root")]
    public Node? Root { get; set; }

    /// <summary>Gets or sets the offsets applied to the root intersection rectangle: 1 to 4 absolute lengths or percentages, as the CSS <c>margin</c> property.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-rootmargin"/>
    [IdlMember("rootMargin", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-rootmargin")]
    public string RootMargin { get; set; } = "0px";

    /// <summary>Gets or sets the offsets applied to the scrollports between the target and the root: 1 to 4 absolute lengths or percentages.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-scrollmargin"/>
    [IdlMember("scrollMargin", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-scrollmargin")]
    public string ScrollMargin { get; set; } = "0px";

    /// <summary>Gets or sets the thresholds: a <see cref="double"/> or a sequence of <see cref="double"/> values between 0 and 1 (0 by default).</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-threshold"/>
    [IdlMember("threshold", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-threshold")]
    public object? Threshold { get; set; } = 0d;

    /// <summary>Gets or sets the minimum delay in milliseconds between the notifications of a target.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-delay"/>
    [IdlMember("delay", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-delay")]
    public int Delay { get; set; }

    /// <summary>Gets or sets whether the observer tracks the visibility of the targets (Intersection Observer V2).</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-trackvisibility"/>
    [IdlMember("trackVisibility", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverinit-trackvisibility")]
    public bool TrackVisibility { get; set; }
}

/// <summary>The values of an <see cref="IntersectionObserverEntry"/> (https://w3c.github.io/IntersectionObserver/#dictdef-intersectionobserverentryinit).</summary>
/// <seealso href="https://w3c.github.io/IntersectionObserver/#dictdef-intersectionobserverentryinit"/>
[IdlInterface("IntersectionObserverEntryInit", "https://w3c.github.io/IntersectionObserver/#dictdef-intersectionobserverentryinit")]
public sealed class IntersectionObserverEntryInit
{
    /// <summary>Gets or sets the time of the observation.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-time"/>
    [IdlMember("time", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-time")]
    public required double Time { get; init; }

    /// <summary>Gets or sets the root intersection rectangle, or null.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-rootbounds"/>
    [IdlMember("rootBounds", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-rootbounds")]
    public required DomRectInit? RootBounds { get; init; }

    /// <summary>Gets or sets the bounding box of the target.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-boundingclientrect"/>
    [IdlMember("boundingClientRect", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-boundingclientrect")]
    public required DomRectInit BoundingClientRect { get; init; }

    /// <summary>Gets or sets the intersection of the target with the root.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-intersectionrect"/>
    [IdlMember("intersectionRect", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-intersectionrect")]
    public required DomRectInit IntersectionRect { get; init; }

    /// <summary>Gets or sets whether the target intersects the root.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-isintersecting"/>
    [IdlMember("isIntersecting", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-isintersecting")]
    public required bool IsIntersecting { get; init; }

    /// <summary>Gets or sets whether the target is visible.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-isvisible"/>
    [IdlMember("isVisible", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-isvisible")]
    public required bool IsVisible { get; init; }

    /// <summary>Gets or sets the ratio of the intersection area to the area of the bounding box of the target.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-intersectionratio"/>
    [IdlMember("intersectionRatio", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-intersectionratio")]
    public required double IntersectionRatio { get; init; }

    /// <summary>Gets or sets the target.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-target"/>
    [IdlMember("target", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentryinit-target")]
    public required Element Target { get; init; }
}

/// <summary>A change of the intersection of a target with the root of an observer (https://w3c.github.io/IntersectionObserver/#intersection-observer-entry).</summary>
/// <seealso href="https://w3c.github.io/IntersectionObserver/#intersectionobserverentry"/>
[IdlInterface("IntersectionObserverEntry", "https://w3c.github.io/IntersectionObserver/#intersectionobserverentry")]
public sealed class IntersectionObserverEntry
{
    /// <summary>Creates an entry from its values.</summary>
    /// <param name="intersectionObserverEntryInit">The values of the entry.</param>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-intersectionobserverentry"/>
    [IdlMember("constructor", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-intersectionobserverentry")]
    public IntersectionObserverEntry(IntersectionObserverEntryInit intersectionObserverEntryInit)
    {
        ArgumentNullException.ThrowIfNull(intersectionObserverEntryInit);
        Time = intersectionObserverEntryInit.Time;
        RootBounds = intersectionObserverEntryInit.RootBounds is { } rootBounds ? DomRectReadOnly.FromRect(rootBounds) : null;
        BoundingClientRect = DomRectReadOnly.FromRect(intersectionObserverEntryInit.BoundingClientRect);
        IntersectionRect = DomRectReadOnly.FromRect(intersectionObserverEntryInit.IntersectionRect);
        IsIntersecting = intersectionObserverEntryInit.IsIntersecting;
        IsVisible = intersectionObserverEntryInit.IsVisible;
        IntersectionRatio = intersectionObserverEntryInit.IntersectionRatio;
        Target = intersectionObserverEntryInit.Target;
    }

    internal IntersectionObserverEntry(double time, DomRectReadOnly? rootBounds, DomRectReadOnly boundingClientRect, DomRectReadOnly intersectionRect, bool isIntersecting, bool isVisible, double intersectionRatio, Element target)
    {
        Time = time;
        RootBounds = rootBounds;
        BoundingClientRect = boundingClientRect;
        IntersectionRect = intersectionRect;
        IsIntersecting = isIntersecting;
        IsVisible = isVisible;
        IntersectionRatio = intersectionRatio;
        Target = target;
    }

    /// <summary>Gets the time at which the intersection was recorded, relative to the time origin of the global object of the observer.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-time"/>
    [IdlMember("time", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-time")]
    public double Time { get; }

    /// <summary>Gets the root intersection rectangle for a same-origin-domain target, else null.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-rootbounds"/>
    [IdlMember("rootBounds", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-rootbounds")]
    public DomRectReadOnly? RootBounds { get; }

    /// <summary>Gets the bounding box of the target (see <see cref="Element.GetBoundingClientRect"/>).</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-boundingclientrect"/>
    [IdlMember("boundingClientRect", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-boundingclientrect")]
    public DomRectReadOnly BoundingClientRect { get; }

    /// <summary>Gets the part of the bounding box of the target that intersects the root intersection rectangle, after the clipping by its ancestors.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-intersectionrect"/>
    [IdlMember("intersectionRect", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-intersectionrect")]
    public DomRectReadOnly IntersectionRect { get; }

    /// <summary>Gets whether the target intersects the root (the edge-adjacent and zero-area intersections included).</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-isintersecting"/>
    [IdlMember("isIntersecting", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-isintersecting")]
    public bool IsIntersecting { get; }

    /// <summary>Gets whether the target is visible (only computed when the observer tracks the visibility).</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-isvisible"/>
    [IdlMember("isVisible", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-isvisible")]
    public bool IsVisible { get; }

    /// <summary>Gets the ratio of the intersection area to the area of the bounding box of the target.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-intersectionratio"/>
    [IdlMember("intersectionRatio", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-intersectionratio")]
    public double IntersectionRatio { get; }

    /// <summary>Gets the observed element.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-target"/>
    [IdlMember("target", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserverentry-target")]
    public Element Target { get; }
}

/// <summary>
/// Observes the intersections of elements with an intersection root (https://w3c.github.io/IntersectionObserver/#intersection-observer-interface):
/// an element or a document, or the viewport of the top-level browsing context (the implicit root). The intersections are
/// computed by the rendering steps of the event loop, after the layout ("run the update intersection observations steps",
/// https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering), and the entries are delivered by a task, so
/// only the elements of the documents of a <see cref="BrowsingContext"/> intersect.
/// </summary>
/// <seealso href="https://w3c.github.io/IntersectionObserver/#intersectionobserver"/>
[IdlInterface("IntersectionObserver", "https://w3c.github.io/IntersectionObserver/#intersectionobserver")]
public sealed class IntersectionObserver
{
    private readonly IntersectionObserverCallback _callback;
    private readonly IntersectionMargin _rootMargin;
    private readonly IntersectionMargin _scrollMargin;
    private readonly double[] _thresholds;
    private readonly List<IntersectionObserverRegistration> _observationTargets = [];
    private readonly List<IntersectionObserverEntry> _queuedEntries = [];
    private Document? _document;

    /// <summary>
    /// Creates an observer (https://w3c.github.io/IntersectionObserver/#initialize-a-new-intersectionobserver). The observer
    /// belongs to the associated document of the current global object (the time of its entries is relative to its time origin,
    /// and its notifications are delivered by a task of its event loop), or, without a current global object, to the document
    /// of the root or of the first observed element.
    /// </summary>
    /// <param name="callback">The callback invoked with the entries.</param>
    /// <param name="options">The options.</param>
    /// <exception cref="DomException">A margin cannot be parsed (<c>SyntaxError</c>).</exception>
    /// <exception cref="RangeErrorException">A threshold is not between 0 and 1.</exception>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-intersectionobserver"/>
    [IdlMember("constructor", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-intersectionobserver")]
    public IntersectionObserver(IntersectionObserverCallback callback, IntersectionObserverInit? options = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        options ??= new IntersectionObserverInit();
        _callback = callback;
        _rootMargin = IntersectionMargin.Parse(options.RootMargin ?? "") ?? throw new DomException("The root margin must be 1 to 4 absolute lengths or percentages", "SyntaxError");
        _scrollMargin = IntersectionMargin.Parse(options.ScrollMargin ?? "") ?? throw new DomException("The scroll margin must be 1 to 4 absolute lengths or percentages", "SyntaxError");
        _thresholds = GetThresholds(options.Threshold);
        Root = options.Root switch
        {
            null or Element or Document => options.Root,
            _ => throw new ArgumentException("The root must be an element or a document", nameof(options)),
        };

        // https://w3c.github.io/IntersectionObserver/#initialize-a-new-intersectionobserver, steps 10 and 11
        Delay = options.TrackVisibility && options.Delay < 100 ? 100 : options.Delay;
        TrackVisibility = options.TrackVisibility;
        _document = Document.CurrentGlobalWindow?.Document ?? (options.Root as Document ?? options.Root?.NodeDocument);
        Sequence = Interlocked.Increment(ref s_lastSequence);
    }

    /// <summary>Creates an observer of the user agent (e.g. the lazy load intersection observer of a document), which belongs to <paramref name="document"/>.</summary>
    internal IntersectionObserver(Document document, IntersectionObserverCallback callback, IntersectionMargin scrollMargin)
    {
        _callback = callback;
        _rootMargin = IntersectionMargin.Zero;
        _scrollMargin = scrollMargin;
        _thresholds = [0];
        _document = document;
        Sequence = Interlocked.Increment(ref s_lastSequence);
    }

    /// <summary>The order of creation of the observers (only compared between the observers of a document, so it can be shared by the threads).</summary>
    private static long s_lastSequence;

    /// <summary>The order of creation of the observer: the observers of a document are processed in this order.</summary>
    internal long Sequence { get; }

    /// <summary>Gets the intersection root: an <see cref="Element"/> or a <see cref="Document"/>, or null for the implicit root.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-root"/>
    [IdlMember("root", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-root")]
    public Node? Root { get; }

    /// <summary>Gets the root margin, serialized as four space-separated lengths (pixels) or percentages.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-rootmargin"/>
    [IdlMember("rootMargin", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-rootmargin")]
    public string RootMargin => _rootMargin.ToString();

    /// <summary>Gets the scroll margin, serialized as four space-separated lengths (pixels) or percentages.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-scrollmargin"/>
    [IdlMember("scrollMargin", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-scrollmargin")]
    public string ScrollMargin => _scrollMargin.ToString();

    /// <summary>Gets the thresholds, in increasing order.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-thresholds"/>
    [IdlMember("thresholds", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-thresholds")]
    public IReadOnlyList<double> Thresholds => _thresholds;

    /// <summary>Gets the minimum delay in milliseconds between the notifications of a target (at least 100 when the visibility is tracked).</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-delay"/>
    [IdlMember("delay", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-delay")]
    public int Delay { get; }

    /// <summary>Gets whether the observer tracks the visibility of the targets.</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-trackvisibility"/>
    [IdlMember("trackVisibility", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-trackvisibility")]
    public bool TrackVisibility { get; }

    /// <summary>The document whose rendering steps compute the observations of the observer and whose tasks deliver its notifications.</summary>
    internal Document? OwnerDocument => _document;

    /// <summary>Starts observing an element (https://w3c.github.io/IntersectionObserver/#observe-target-element).</summary>
    /// <param name="target">The element to observe.</param>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-observe"/>
    [IdlMember("observe", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-observe")]
    public void Observe(Element target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_observationTargets.Exists(registration => ReferenceEquals(registration.Target, target)))
            return;

        _document ??= target.NodeDocument;
        _observationTargets.Add(new IntersectionObserverRegistration(target));
        _document.AddIntersectionObserver(this, target);
    }

    /// <summary>Stops observing an element (https://w3c.github.io/IntersectionObserver/#unobserve-target-element).</summary>
    /// <param name="target">The observed element.</param>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-unobserve"/>
    [IdlMember("unobserve", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-unobserve")]
    public void Unobserve(Element target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var index = _observationTargets.FindIndex(registration => ReferenceEquals(registration.Target, target));
        if (index < 0)
            return;

        _observationTargets.RemoveAt(index);
        if (_observationTargets.Count == 0)
        {
            _document?.RemoveIntersectionObserver(this);
        }
    }

    /// <summary>
    /// Stops observing all the elements (https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-disconnect). As in
    /// the browsers, the entries that are queued are not delivered (WPT intersection-observer/disconnect.html).
    /// </summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-disconnect"/>
    [IdlMember("disconnect", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-disconnect")]
    public void Disconnect()
    {
        _queuedEntries.Clear();
        if (_observationTargets.Count == 0)
            return;

        _observationTargets.Clear();
        _document?.RemoveIntersectionObserver(this);
    }

    /// <summary>Returns the queued entries and clears them (https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-takerecords).</summary>
    /// <seealso href="https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-takerecords"/>
    [IdlMember("takeRecords", "https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-takerecords")]
    public IReadOnlyList<IntersectionObserverEntry> TakeRecords()
    {
        var queue = _queuedEntries.ToArray();
        _queuedEntries.Clear();
        return queue;
    }

    /// <summary>The steps 5 to 9 of https://w3c.github.io/IntersectionObserver/#initialize-a-new-intersectionobserver.</summary>
    private static double[] GetThresholds(object? threshold)
    {
        var thresholds = new List<double>();
        switch (threshold)
        {
            case null:
                break;
            case double value:
                thresholds.Add(value);
                break;
            case IEnumerable values:
                foreach (var value in values)
                {
                    thresholds.Add(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                }

                break;
            default:
                thresholds.Add(Convert.ToDouble(threshold, System.Globalization.CultureInfo.InvariantCulture));
                break;
        }

        foreach (var value in thresholds)
        {
            if (!(value >= 0 && value <= 1))
                throw new RangeErrorException("The thresholds must be between 0 and 1");
        }

        thresholds.Sort();
        if (thresholds.Count == 0)
        {
            thresholds.Add(0);
        }

        return [.. thresholds];
    }

    /// <summary>
    /// The steps 2.1 and 2.2 of https://w3c.github.io/IntersectionObserver/#update-intersection-observations-algo for this
    /// observer: computes the intersection of each target with the root, in the order of the calls to <see cref="Observe"/>, and
    /// queues an entry when the threshold index, the intersecting state or the visibility changed. <paramref name="time"/> is
    /// the time of the rendering update relative to the time origin of the global object of the observer, and
    /// <paramref name="preciseTime"/> the same time in milliseconds without coarsening (from any origin), which measures the
    /// delays. Returns whether an observation was skipped because of the delay (another rendering update must compute it).
    /// </summary>
    internal bool UpdateObservations(double time, double preciseTime)
    {
        var skipped = false;
        foreach (var registration in _observationTargets.ToArray())
        {
            if (preciseTime - registration.LastUpdateTime < Delay)
            {
                skipped = true;
                continue;
            }

            // As in the browsers, the target of an implicit root observer in a document without browsing context (e.g. created by
            // createHTMLDocument()) is not observed until it is adopted by a document that is rendered (WPT
            // target-in-detached-document.html); the target of an explicit root observer does not intersect
            var target = registration.Target;
            if (Root is null && !target.NodeDocument.IsFullyActive)
                continue;

            registration.LastUpdateTime = preciseTime;

            // The intersection root: the element or the document of an explicit root, else the document of the top-level browsing context
            Document? rootDocument;
            if (Root is { } root)
            {
                rootDocument = root as Document ?? root.NodeDocument;
            }
            else
            {
                rootDocument = target.NodeDocument.BrowsingContext?.Top.Document;
            }

            // The margins and the root intersection rectangle only apply to the same-origin-domain targets
            // (https://w3c.github.io/IntersectionObserver/#same-origin-domain-target)
            var sameOriginDomain = rootDocument is not null && (Root is not null || Document.IsSameOriginDomain(target.NodeDocument, rootDocument));
            var observation = rootDocument is null ? default : IntersectionGeometry.Compute(Root, rootDocument, target, _rootMargin, _scrollMargin, sameOriginDomain, TrackVisibility);

            var targetArea = observation.TargetRect.Width * observation.TargetRect.Height;
            var intersectionArea = observation.IntersectionRect.Width * observation.IntersectionRect.Height;
            var intersectionRatio = targetArea > 0 ? Math.Min(1, intersectionArea / targetArea) : observation.IsIntersecting ? 1 : 0;

            // Step 13: the index of the first threshold greater than the ratio. As in the browsers, a target that does not
            // intersect has the index 0 (a ratio of 0 does not cross the threshold 0), and a target whose ratio is below the
            // first threshold is not intersecting (https://github.com/w3c/IntersectionObserver/issues/432)
            var thresholdIndex = 0;
            if (observation.IsIntersecting)
            {
                while (thresholdIndex < _thresholds.Length && _thresholds[thresholdIndex] <= intersectionRatio)
                {
                    thresholdIndex++;
                }
            }

            var isIntersecting = thresholdIndex > 0;

            var isVisible = observation.IsVisible;
            if (thresholdIndex != registration.PreviousThresholdIndex || isIntersecting != registration.PreviousIsIntersecting || isVisible != registration.PreviousIsVisible)
            {
                // https://w3c.github.io/IntersectionObserver/#queue-intersection-observer-entry-algo
                var rootBounds = sameOriginDomain ? ToDomRect(observation.RootBounds) : null;
                _queuedEntries.Add(new IntersectionObserverEntry(time, rootBounds, ToDomRect(observation.TargetRect), ToDomRect(observation.IntersectionRect), isIntersecting, isVisible, intersectionRatio, target));
                _document?.QueueIntersectionObserverTask(this);
            }

            registration.PreviousThresholdIndex = thresholdIndex;
            registration.PreviousIsIntersecting = isIntersecting;
            registration.PreviousIsVisible = isVisible;
        }

        return skipped;

        static DomRectReadOnly ToDomRect(PhysicalRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// The steps 3.1 to 3.5 of https://w3c.github.io/IntersectionObserver/#notify-intersection-observers-algo for this observer:
    /// invokes the callback with the queued entries.
    /// </summary>
    internal void Notify()
    {
        if (_queuedEntries.Count == 0)
            return;

        var queue = _queuedEntries.ToArray();
        _queuedEntries.Clear();
        try
        {
            _callback(queue, this);
        }
#pragma warning disable CA1031 // "If this throws an exception, report the exception": the other observers are still notified
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Scripting.ScriptErrors.Report(ex, _document?.RelevantWindow);
        }
    }

    /// <summary>An IntersectionObserverRegistration record (https://w3c.github.io/IntersectionObserver/#intersectionobserverregistration).</summary>
    private sealed class IntersectionObserverRegistration(Element target)
    {
        public Element Target { get; } = target;

        public int PreviousThresholdIndex { get; set; } = -1;

        public bool PreviousIsIntersecting { get; set; }

        public bool PreviousIsVisible { get; set; }

        /// <summary>The time of the last update (the first update is never delayed).</summary>
        public double LastUpdateTime { get; set; } = double.NegativeInfinity;
    }
}
