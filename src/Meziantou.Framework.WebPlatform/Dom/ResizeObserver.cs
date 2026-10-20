using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://drafts.csswg.org/resize-observer-1/#enumdef-resizeobserverboxoptions</summary>
/// <seealso href="https://drafts.csswg.org/resize-observer-1/#enumdef-resizeobserverboxoptions"/>
[IdlInterface("ResizeObserverBoxOptions", "https://drafts.csswg.org/resize-observer-1/#enumdef-resizeobserverboxoptions")]
public enum ResizeObserverBoxOptions
{
    /// <summary>The size of the border area of the box.</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverboxoptions-border-box"/>
    [IdlMember("border-box", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverboxoptions-border-box")]
    BorderBox,

    /// <summary>The size of the content area of the box.</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverboxoptions-content-box"/>
    [IdlMember("content-box", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverboxoptions-content-box")]
    ContentBox,

    /// <summary>The size of the content area of the box in integral device pixels, before the transforms are applied.</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverboxoptions-device-pixel-content-box"/>
    [IdlMember("device-pixel-content-box", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverboxoptions-device-pixel-content-box")]
    DevicePixelContentBox,
}

/// <summary>https://drafts.csswg.org/resize-observer-1/#dictdef-resizeobserveroptions</summary>
/// <seealso href="https://drafts.csswg.org/resize-observer-1/#dictdef-resizeobserveroptions"/>
[IdlInterface("ResizeObserverOptions", "https://drafts.csswg.org/resize-observer-1/#dictdef-resizeobserveroptions")]
public sealed class ResizeObserverOptions
{
    /// <summary>Gets or sets the box whose size changes are observed (the content box by default).</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserveroptions-box"/>
    [IdlMember("box", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserveroptions-box")]
    public ResizeObserverBoxOptions Box { get; set; } = ResizeObserverBoxOptions.ContentBox;
}

/// <summary>https://drafts.csswg.org/resize-observer-1/#callbackdef-resizeobservercallback</summary>
/// <param name="entries">The entries of the observed elements whose size changed.</param>
/// <param name="observer">The observer.</param>
/// <seealso href="https://drafts.csswg.org/resize-observer-1/#callbackdef-resizeobservercallback"/>
[IdlInterface("ResizeObserverCallback", "https://drafts.csswg.org/resize-observer-1/#callbackdef-resizeobservercallback")]
public delegate void ResizeObserverCallback(IReadOnlyList<ResizeObserverEntry> entries, ResizeObserver observer);

/// <summary>The size of a box in the inline and block axes of its writing mode (https://drafts.csswg.org/resize-observer-1/#resizeobserversize).</summary>
/// <seealso href="https://drafts.csswg.org/resize-observer-1/#resizeobserversize"/>
[IdlInterface("ResizeObserverSize", "https://drafts.csswg.org/resize-observer-1/#resizeobserversize")]
public sealed class ResizeObserverSize
{
    internal ResizeObserverSize(double inlineSize, double blockSize)
    {
        InlineSize = inlineSize;
        BlockSize = blockSize;
    }

    /// <summary>Gets the size of the box in its inline axis.</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserversize-inlinesize"/>
    [IdlMember("inlineSize", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserversize-inlinesize")]
    public double InlineSize { get; }

    /// <summary>Gets the size of the box in its block axis.</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserversize-blocksize"/>
    [IdlMember("blockSize", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserversize-blocksize")]
    public double BlockSize { get; }
}

/// <summary>The sizes of an observed element when its size changed (https://drafts.csswg.org/resize-observer-1/#resize-observer-entry-interface).</summary>
/// <seealso href="https://drafts.csswg.org/resize-observer-1/#resizeobserverentry"/>
[IdlInterface("ResizeObserverEntry", "https://drafts.csswg.org/resize-observer-1/#resizeobserverentry")]
public sealed class ResizeObserverEntry
{
    internal ResizeObserverEntry(Element target, DomRectReadOnly contentRect, IReadOnlyList<ResizeObserverSize> borderBoxSize, IReadOnlyList<ResizeObserverSize> contentBoxSize, IReadOnlyList<ResizeObserverSize> devicePixelContentBoxSize)
    {
        Target = target;
        ContentRect = contentRect;
        BorderBoxSize = borderBoxSize;
        ContentBoxSize = contentBoxSize;
        DevicePixelContentBoxSize = devicePixelContentBoxSize;
    }

    /// <summary>Gets the element whose size changed.</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverentry-target"/>
    [IdlMember("target", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverentry-target")]
    public Element Target { get; }

    /// <summary>
    /// Gets the content rect of the element (https://drafts.csswg.org/resize-observer-1/#content-rect): the size of its content
    /// box, at the position of its padding top and padding left.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverentry-contentrect"/>
    [IdlMember("contentRect", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverentry-contentrect")]
    public DomRectReadOnly ContentRect { get; }

    /// <summary>Gets the sizes of the border box of the element, one per fragment.</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverentry-borderboxsize"/>
    [IdlMember("borderBoxSize", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverentry-borderboxsize")]
    public IReadOnlyList<ResizeObserverSize> BorderBoxSize { get; }

    /// <summary>Gets the sizes of the content box of the element, one per fragment.</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverentry-contentboxsize"/>
    [IdlMember("contentBoxSize", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverentry-contentboxsize")]
    public IReadOnlyList<ResizeObserverSize> ContentBoxSize { get; }

    /// <summary>Gets the sizes of the content box of the element in integral device pixels, one per fragment.</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverentry-devicepixelcontentboxsize"/>
    [IdlMember("devicePixelContentBoxSize", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserverentry-devicepixelcontentboxsize")]
    public IReadOnlyList<ResizeObserverSize> DevicePixelContentBoxSize { get; }
}

/// <summary>
/// Observes the size changes of the boxes of elements (https://drafts.csswg.org/resize-observer-1/#resize-observer-interface).
/// The observations are determined and delivered by the rendering steps of the event loop, after the layout
/// (https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering), so only the elements of the documents of a
/// <see cref="BrowsingContext"/> are observed.
/// </summary>
/// <seealso href="https://drafts.csswg.org/resize-observer-1/#resizeobserver"/>
[IdlInterface("ResizeObserver", "https://drafts.csswg.org/resize-observer-1/#resizeobserver")]
public sealed class ResizeObserver
{
    private readonly ResizeObserverCallback _callback;
    private readonly List<ResizeObservation> _observationTargets = [];
    private readonly List<ResizeObservation> _activeTargets = [];
    private Document? _document;

    /// <summary>
    /// Creates an observer that invokes the callback with the entries of the observed elements whose size changed. The observer
    /// belongs to the associated document of the current global object (the <c>[[resizeObservers]]</c> of the document, whose
    /// rendering steps deliver the notifications), or, without a current global object, to the document of the first
    /// observed element.
    /// </summary>
    /// <param name="callback">The callback invoked with the entries.</param>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-resizeobserver"/>
    [IdlMember("constructor", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-resizeobserver")]
    public ResizeObserver(ResizeObserverCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        _document = Document.CurrentGlobalWindow?.Document;
        Sequence = Interlocked.Increment(ref s_lastSequence);
    }

    /// <summary>The order of creation of the observers (only compared between the observers of a document, so it can be shared by the threads).</summary>
    private static long s_lastSequence;

    /// <summary>The order of creation of the observer: the observers of a document are notified in this order.</summary>
    internal long Sequence { get; }

    /// <summary>The <c>[[skippedTargets]]</c> slot is not empty: an observation was active at a depth that was not deeper than the one of the last broadcast.</summary>
    internal bool HasSkippedTargets { get; private set; }

    internal bool HasActiveTargets => _activeTargets.Count > 0;

    /// <summary>Starts observing the size of an element, or changes the observed box of an observed element (https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-observe).</summary>
    /// <param name="target">The element to observe.</param>
    /// <param name="options">The box to observe (the content box by default).</param>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-observe"/>
    [IdlMember("observe", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-observe")]
    public void Observe(Element target, ResizeObserverOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        _document ??= target.NodeDocument;

        // "If target is in [[observationTargets]] slot, call unobserve() with argument target". As in the browsers, observing a
        // target again with the same box does nothing: its last reported size is kept (WPT resize-observer/notify.html, test8)
        var observedBox = options?.Box ?? ResizeObserverBoxOptions.ContentBox;
        if (_observationTargets.Find(observation => ReferenceEquals(observation.Target, target)) is { } existing && existing.ObservedBox == observedBox)
            return;

        RemoveObservation(target);
        _observationTargets.Add(new ResizeObservation(target, observedBox));
        _document.AddResizeObserver(this);
    }

    /// <summary>Stops observing the size of an element (https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-unobserve).</summary>
    /// <param name="target">The observed element.</param>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-unobserve"/>
    [IdlMember("unobserve", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-unobserve")]
    public void Unobserve(Element target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (RemoveObservation(target) && _observationTargets.Count == 0)
        {
            _document?.RemoveResizeObserver(this);
        }
    }

    /// <summary>Stops observing all the elements (https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-disconnect).</summary>
    /// <seealso href="https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-disconnect"/>
    [IdlMember("disconnect", "https://drafts.csswg.org/resize-observer-1/#dom-resizeobserver-disconnect")]
    public void Disconnect()
    {
        _observationTargets.Clear();
        _activeTargets.Clear();
        HasSkippedTargets = false;
        _document?.RemoveResizeObserver(this);
    }

    private bool RemoveObservation(Element target)
    {
        for (var i = 0; i < _observationTargets.Count; i++)
        {
            if (ReferenceEquals(_observationTargets[i].Target, target))
            {
                var observation = _observationTargets[i];
                _observationTargets.RemoveAt(i);
                _activeTargets.Remove(observation);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The steps of https://drafts.csswg.org/resize-observer-1/#gather-active-observations-h for this observer: the active
    /// observations whose target is deeper than <paramref name="depth"/> are active targets, the other ones are skipped targets.
    /// The targets in the skipped contents of an element (e.g. <c>content-visibility: hidden</c>) are not observed: their sizes
    /// are not reported until they are rendered again.
    /// </summary>
    internal void GatherActiveObservations(int depth)
    {
        _activeTargets.Clear();
        HasSkippedTargets = false;
        foreach (var observation in _observationTargets)
        {
            if (!observation.IsActive())
                continue;

            if (ResizeObservations.CalculateDepth(observation.Target) > depth)
            {
                _activeTargets.Add(observation);
            }
            else
            {
                HasSkippedTargets = true;
            }
        }
    }

    /// <summary>
    /// The steps of https://drafts.csswg.org/resize-observer-1/#broadcast-resize-notifications-h for this observer: invokes the
    /// callback with the entries of its active targets. Returns the depth of the shallowest target, or <see cref="int.MaxValue"/>.
    /// </summary>
    internal int BroadcastActiveObservations()
    {
        var shallowestTargetDepth = int.MaxValue;
        if (_activeTargets.Count == 0)
            return shallowestTargetDepth;

        var entries = new ResizeObserverEntry[_activeTargets.Count];
        for (var i = 0; i < _activeTargets.Count; i++)
        {
            var observation = _activeTargets[i];
            var entry = ResizeObservations.CreateEntry(observation.Target);
            entries[i] = entry;
            observation.LastReportedSizes = observation.ObservedBox switch
            {
                ResizeObserverBoxOptions.BorderBox => entry.BorderBoxSize,
                ResizeObserverBoxOptions.DevicePixelContentBox => entry.DevicePixelContentBoxSize,
                _ => entry.ContentBoxSize,
            };

            shallowestTargetDepth = Math.Min(shallowestTargetDepth, ResizeObservations.CalculateDepth(observation.Target));
        }

        try
        {
            _callback(entries, this);
        }
#pragma warning disable CA1031 // "If this throws an exception, report the exception": the other observers are still notified
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Scripting.ScriptErrors.Report(ex, _document?.RelevantWindow);
        }

        _activeTargets.Clear();
        return shallowestTargetDepth;
    }

    /// <summary>An observation of an element (https://drafts.csswg.org/resize-observer-1/#resize-observation-interface).</summary>
    private sealed class ResizeObservation(Element target, ResizeObserverBoxOptions observedBox)
    {
        private static readonly IReadOnlyList<ResizeObserverSize> InitialSizes = [new ResizeObserverSize(-1, -1)];

        public Element Target { get; } = target;

        public ResizeObserverBoxOptions ObservedBox { get; } = observedBox;

        public IReadOnlyList<ResizeObserverSize> LastReportedSizes { get; set; } = InitialSizes;

        /// <summary>
        /// https://drafts.csswg.org/resize-observer-1/#dom-resizeobservation-isactive: the sizes of the observed box differ from
        /// the last reported sizes (all the fragments are compared, https://github.com/w3c/csswg-drafts/issues/3673).
        /// </summary>
        public bool IsActive()
        {
            if (ResizeObservations.IsInSkippedContents(Target))
                return false;

            var currentSizes = ResizeObservations.CalculateBoxSize(Target, ObservedBox);
            if (currentSizes.Count != LastReportedSizes.Count)
                return true;

            for (var i = 0; i < currentSizes.Count; i++)
            {
                if (currentSizes[i].InlineSize != LastReportedSizes[i].InlineSize || currentSizes[i].BlockSize != LastReportedSizes[i].BlockSize)
                    return true;
            }

            return false;
        }
    }
}
