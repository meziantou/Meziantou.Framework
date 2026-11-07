namespace Meziantou.Framework.WebPlatform.Dom;

// The resize observers of a document (https://drafts.csswg.org/resize-observer-1/#document-slots) and the steps of the
// rendering update that determine and deliver their notifications (https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering,
// see LayoutEngine.UpdateRendering)
public partial class Document
{
    /// <summary>
    /// The <c>[[resizeObservers]]</c> slot, in their order of creation: only the observers that observe at least one element
    /// are kept (an observer without targets has nothing to gather nor to broadcast, and can be collected).
    /// </summary>
    private List<ResizeObserver>? _resizeObservers;

    /// <summary>Whether a resize observer of the document observes an element.</summary>
    internal bool HasResizeObservers => _resizeObservers is { Count: > 0 };

    /// <summary>
    /// Adds an observer that observes an element to the resize observers of the document (when it is not in it yet), and
    /// schedules a rendering update: the rendering steps of the document then update its layout and gather its observations.
    /// </summary>
    internal void AddResizeObserver(ResizeObserver observer)
    {
        _resizeObservers ??= [];
        if (!_resizeObservers.Contains(observer))
        {
            var index = _resizeObservers.Count;
            while (index > 0 && _resizeObservers[index - 1].Sequence > observer.Sequence)
            {
                index--;
            }

            _resizeObservers.Insert(index, observer);
            if (EventLoop is { } eventLoop)
            {
                eventLoop.ResizeObserverCount++;
            }
        }

        GetLayoutEngine().RequestRenderingUpdates();
    }

    /// <summary>Removes an observer that no longer observes any element from the resize observers of the document.</summary>
    internal void RemoveResizeObserver(ResizeObserver observer)
    {
        if (_resizeObservers is not null && _resizeObservers.Remove(observer) && EventLoop is { } eventLoop)
        {
            eventLoop.ResizeObserverCount--;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/resize-observer-1/#gather-active-observations-h, followed by
    /// https://drafts.csswg.org/resize-observer-1/#has-active-observations-h: returns whether the document has active resize
    /// observations at the depth.
    /// </summary>
    internal bool GatherActiveResizeObservations(int depth)
    {
        if (_resizeObservers is not { Count: > 0 } observers)
            return false;

        var hasActiveObservations = false;
        foreach (var observer in observers.ToArray())
        {
            observer.GatherActiveObservations(depth);
            hasActiveObservations |= observer.HasActiveTargets;
        }

        return hasActiveObservations;
    }

    /// <summary>https://drafts.csswg.org/resize-observer-1/#has-skipped-observations-h</summary>
    internal bool HasSkippedResizeObservations()
    {
        if (_resizeObservers is not { Count: > 0 } observers)
            return false;

        foreach (var observer in observers)
        {
            if (observer.HasSkippedTargets)
                return true;
        }

        return false;
    }

    /// <summary>
    /// https://drafts.csswg.org/resize-observer-1/#broadcast-resize-notifications-h: invokes the callbacks of the observers with
    /// active observations, and returns the depth of the shallowest broadcast target.
    /// </summary>
    internal int BroadcastActiveResizeObservations()
    {
        var shallowestTargetDepth = int.MaxValue;
        if (_resizeObservers is not { Count: > 0 } observers)
            return shallowestTargetDepth;

        // A callback may observe or disconnect observers
        foreach (var observer in observers.ToArray())
        {
            shallowestTargetDepth = Math.Min(shallowestTargetDepth, observer.BroadcastActiveObservations());
        }

        return shallowestTargetDepth;
    }

    /// <summary>
    /// https://drafts.csswg.org/resize-observer-1/#deliver-resize-error: reports an error whose message is "ResizeObserver loop
    /// completed with undelivered notifications." to the window of the document. The skipped observations are still active:
    /// they are delivered by the next rendering update, which is scheduled.
    /// </summary>
    internal void DeliverResizeLoopErrorNotification()
    {
        RelevantWindow?.ReportException(new Scripting.ScriptError("ResizeObserver loop completed with undelivered notifications.", error: null));
        EventLoop?.RequestRenderingUpdate();
    }
}
