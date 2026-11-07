namespace Meziantou.Framework.WebPlatform.Dom;

// The timing of the navigation and of the loading of a document of a browsing context
// (https://html.spec.whatwg.org/multipage/dom.html#document-load-timing-info), exposed by the navigation timing entry of its
// window (https://w3c.github.io/navigation-timing/)
public partial class Document
{
    private ulong _navigationResponseBodySize;
    private bool _navigationTimingEntryQueued;

    /// <summary>The timing of the navigation that created the document and of its loading, or null for a document without browsing context.</summary>
    internal DocumentLoadTimingInfo? LoadTimingInfo { get; set; }

    /// <summary>The navigation timing entry of the document, in the performance timeline of its window.</summary>
    internal PerformanceNavigationTiming? NavigationTimingEntry { get; set; }

    /// <summary>
    /// The times of the unload event of the document (https://html.spec.whatwg.org/multipage/dom.html#unload-timing-info), as
    /// timestamps of the event loop: the next document of the navigable exposes them when it has the same origin.
    /// </summary>
    internal (long Start, long End) UnloadEventTimes { get; set; }

    /// <summary>The current time of the event loop of the document, when the timing of its loading is recorded.</summary>
    private long? GetLoadTimingTimestamp() => LoadTimingInfo is not null && EventLoop is { } eventLoop ? eventLoop.TimeProvider.GetTimestamp() : null;

    // The times are set once: the document.open() steps do not reset the timing of the navigation of the document, although
    // the parser created by document.open() runs "the end" again (the browsers keep the first values too)
    private void MarkDomInteractive()
    {
        if (LoadTimingInfo is { DomInteractive: 0 } timing && GetLoadTimingTimestamp() is { } now)
        {
            timing.DomInteractive = now;
        }
    }

    private void MarkDomComplete()
    {
        if (LoadTimingInfo is { DomComplete: 0 } timing && GetLoadTimingTimestamp() is { } now)
        {
            timing.DomComplete = now;
        }
    }

    private bool MarkDomContentLoadedEventStart()
    {
        if (LoadTimingInfo is not { DomContentLoadedEventStart: 0 } timing || GetLoadTimingTimestamp() is not { } now)
            return false;

        timing.DomContentLoadedEventStart = now;
        return true;
    }

    private void MarkDomContentLoadedEventEnd()
    {
        if (LoadTimingInfo is { } timing && GetLoadTimingTimestamp() is { } now)
        {
            timing.DomContentLoadedEventEnd = now;
        }
    }

    private bool MarkLoadEventStart()
    {
        if (LoadTimingInfo is not { LoadEventStart: 0 } timing || GetLoadTimingTimestamp() is not { } now)
            return false;

        timing.LoadEventStart = now;
        return true;
    }

    private void MarkLoadEventEnd()
    {
        if (LoadTimingInfo is { } timing && GetLoadTimingTimestamp() is { } now)
        {
            timing.LoadEventEnd = now;
        }
    }

    /// <summary>Records the bytes of the body of the response of the navigation that arrive after the document is created.</summary>
    internal void AddNavigationResponseBytes(int count) => _navigationResponseBodySize += (ulong)count;

    /// <summary>
    /// Records the end of the body of the response of the navigation, when it arrives after the document is created: the
    /// <c>responseEnd</c> and the body sizes of the navigation timing entry are 0 until then.
    /// </summary>
    internal void MarkNavigationResponseEnd()
    {
        if (LoadTimingInfo is not { ResponseEnd: 0 } timing || GetLoadTimingTimestamp() is not { } now)
            return;

        timing.ResponseEnd = now;
        if (NavigationTimingEntry is { } entry && RelevantWindow is { } window)
        {
            entry.SetResponseEnd(Math.Max(0, window.ToRelativeHighResolutionTime(now)), _navigationResponseBodySize, _navigationResponseBodySize);
        }
    }
}
