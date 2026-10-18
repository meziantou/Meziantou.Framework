namespace Meziantou.Framework.WebPlatform.Dom;

// The intersection observers of a document and the steps of the rendering update that compute their observations
// (https://w3c.github.io/IntersectionObserver/#run-the-update-intersection-observations-steps), and the lazy load intersection
// observer (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#lazy-load-intersection-observer)
public partial class Document
{
    /// <summary>
    /// The intersection observers that belong to the document (created by its scripts, see <see cref="IntersectionObserver.OwnerDocument"/>)
    /// and observe at least one element, in their order of creation: an observer without targets has nothing to compute, and
    /// can be collected.
    /// </summary>
    private List<IntersectionObserver>? _intersectionObservers;

    /// <summary>
    /// The observers that have queued entries since the last notification: the notify list of the task queued by
    /// https://w3c.github.io/IntersectionObserver/#queue-an-intersection-observer-task (a non-empty list is the
    /// IntersectionObserverTaskQueued flag), so that an observer that stopped observing its targets still delivers its entries.
    /// </summary>
    private List<IntersectionObserver>? _intersectionObserversToNotify;

    /// <summary>The lazy load intersection observer (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#lazy-load-intersection-observer), or null.</summary>
    internal IntersectionObserver? LazyLoadIntersectionObserver { get; set; }

    /// <summary>Whether an intersection observer of the document observes an element.</summary>
    internal bool HasIntersectionObservers => _intersectionObservers is { Count: > 0 };

    /// <summary>
    /// Adds an observer that observes an element to the intersection observers of the document (when it is not in it yet), and
    /// schedules a rendering update: the documents from the one of the target to the top-level document are laid out by the
    /// rendering steps, which compute the observations.
    /// </summary>
    internal void AddIntersectionObserver(IntersectionObserver observer, Element target)
    {
        _intersectionObservers ??= [];
        if (!_intersectionObservers.Contains(observer))
        {
            var index = _intersectionObservers.Count;
            while (index > 0 && _intersectionObservers[index - 1].Sequence > observer.Sequence)
            {
                index--;
            }

            _intersectionObservers.Insert(index, observer);
            if (EventLoop is { } eventLoop)
            {
                if (_intersectionObservers.Count == 1)
                {
                    eventLoop.AddDocumentWithIntersectionObservers(this);
                }

                eventLoop.IntersectionObserverCount++;
            }
        }

        // The position of the target in the viewport depends on the layout of the documents of the ancestor navigables
        for (var context = target.NodeDocument.BrowsingContext; context is not null; context = context.Parent)
        {
            context.Document.GetLayoutEngine().RequestRenderingUpdates();
        }

        EventLoop?.RequestRenderingUpdate();
    }

    /// <summary>Removes an observer that no longer observes any element from the intersection observers of the document.</summary>
    internal void RemoveIntersectionObserver(IntersectionObserver observer)
    {
        if (_intersectionObservers is not null && _intersectionObservers.Remove(observer) && EventLoop is { } eventLoop)
        {
            eventLoop.IntersectionObserverCount--;
        }
    }

    /// <summary>
    /// https://w3c.github.io/IntersectionObserver/#run-the-update-intersection-observations-steps for the observers of the
    /// document, given the timestamp of the rendering update. The observations skipped because of the delay of their observer
    /// are computed by a next rendering update, which is scheduled.
    /// </summary>
    internal void RunUpdateIntersectionObservationsSteps(long frameTimestamp)
    {
        if (_intersectionObservers is not { Count: > 0 } observers || RelevantWindow is not { } window)
            return;

        // The delays are measured with the precise time of the frame (the time of the entries is coarsened)
        var time = window.ToRelativeHighResolutionTime(frameTimestamp);
        var preciseTime = frameTimestamp * 1000d / window.EventLoop.TimeProvider.TimestampFrequency;
        var skipped = false;
        foreach (var observer in observers.ToArray())
        {
            skipped |= observer.UpdateObservations(time, preciseTime);
        }

        if (skipped)
        {
            EventLoop?.RequestRenderingUpdate();
        }
    }

    /// <summary>https://w3c.github.io/IntersectionObserver/#queue-an-intersection-observer-task, for an observer that queued an entry.</summary>
    internal void QueueIntersectionObserverTask(IntersectionObserver observer)
    {
        if (_intersectionObserversToNotify is { Count: > 0 } observers)
        {
            if (!observers.Contains(observer))
            {
                observers.Add(observer);
            }

            return;
        }

        (_intersectionObserversToNotify ??= []).Add(observer);
        QueueGlobalTask(NotifyIntersectionObservers);
    }

    /// <summary>https://w3c.github.io/IntersectionObserver/#notify-intersection-observers-algo: the observers are notified in their order of creation.</summary>
    private void NotifyIntersectionObservers()
    {
        if (_intersectionObserversToNotify is not { Count: > 0 } list)
            return;

        var observers = list.ToArray();
        list.Clear();
        Array.Sort(observers, static (a, b) => a.Sequence.CompareTo(b.Sequence));
        foreach (var observer in observers)
        {
            observer.Notify();
        }
    }
}
