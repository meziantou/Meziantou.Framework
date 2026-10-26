namespace Meziantou.Framework.WebPlatform.Dom;

// The state of a document kept in the back/forward cache (https://html.spec.whatwg.org/multipage/browsing-the-web.html#note-bfcache):
// the document of a session history entry that is no longer the active document of its navigable, but is not destroyed, so
// that a traversal to the entry makes it the active document again instead of loading it again (see BrowsingContext.BackForwardCache.cs).
public partial class Document
{
    private List<Action>? _tasksDeferredByBackForwardCache;
    private volatile bool _isInBackForwardCache;
    private long _suspensionStart;
    private TimeSpan _suspendedDuration;

    /// <summary>
    /// Whether the document is in the back/forward cache: it is the document of a session history entry of its top-level
    /// traversable that is not the active entry, or a document of a child navigable of such a document. Its tasks are deferred
    /// until it is fully active again (<see cref="DeferTaskUntilReactivated"/>), and its timers are suspended. It is read by
    /// the threads of the workers and of the service workers (e.g. to evict the document when a message is sent to it).
    /// </summary>
    internal bool IsInBackForwardCache
    {
        get => _isInBackForwardCache;
        private set => _isInBackForwardCache = value;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#concept-document-bfcache-blocking-details: the reasons that
    /// prevent the document from being kept in the back/forward cache, or that evicted it (the <c>reason</c> strings of the
    /// <c>NotRestoredReasonDetails</c>), or null.
    /// </summary>
    internal List<string>? BackForwardCacheBlockingDetails { get; private set; }

    /// <summary>
    /// The reasons of the response of the document that prevent it from being kept in the back/forward cache (the
    /// user-agent specific blocking reasons of its response, e.g. <c>Cache-Control: no-store</c>), set when it is created.
    /// </summary>
    internal List<string>? ResponseBackForwardCacheBlockingReasons { get; set; }

    /// <summary>The browsing context of the document that another browsing context replaced in its navigable, or null.</summary>
    internal ReplacedBrowsingContext? ReplacedBrowsingContext { get; set; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#make-document-unsalvageable: adds a reason to the bfcache
    /// blocking details of the document.
    /// </summary>
    internal void MakeUnsalvageable(string reason)
    {
        var details = BackForwardCacheBlockingDetails ??= [];
        if (!details.Contains(reason, StringComparer.Ordinal))
        {
            details.Add(reason);
        }
    }

    /// <summary>
    /// The time the timers of the window of the document were suspended, since the document was created: the timers wait
    /// until the document has been fully active for their timeout
    /// (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#run-steps-after-a-timeout).
    /// </summary>
    internal TimeSpan GetSuspendedDuration()
    {
        if (_suspensionStart == 0 || RelevantWindow is not { } window)
            return _suspendedDuration;

        return _suspendedDuration + window.EventLoop.TimeProvider.GetElapsedTime(_suspensionStart);
    }

    /// <summary>
    /// The document enters the back/forward cache (https://html.spec.whatwg.org/multipage/document-lifecycle.html#unload-a-document,
    /// the suspension time): its tasks are deferred and its timers are suspended until it is reactivated.
    /// </summary>
    internal void EnterBackForwardCache()
    {
        IsInBackForwardCache = true;
        if (_suspensionStart == 0 && RelevantWindow is { } window)
        {
            _suspensionStart = Math.Max(1, window.EventLoop.TimeProvider.GetTimestamp());
        }
    }

    /// <summary>
    /// The document leaves the back/forward cache (https://html.spec.whatwg.org/multipage/browsing-the-web.html#reactivate-a-document):
    /// the suspension of its timers ends, and its deferred tasks are queued again, in order.
    /// </summary>
    internal void LeaveBackForwardCache()
    {
        IsInBackForwardCache = false;
        _suspendedDuration = GetSuspendedDuration();
        _suspensionStart = 0;
        if (_tasksDeferredByBackForwardCache is { } tasks)
        {
            _tasksDeferredByBackForwardCache = null;
            foreach (var task in tasks)
            {
                QueueGlobalTask(task);
            }
        }
    }

    /// <summary>The document is evicted from the back/forward cache: its deferred tasks are discarded (the document is destroyed).</summary>
    internal void DiscardBackForwardCacheState()
    {
        IsInBackForwardCache = false;
        _tasksDeferredByBackForwardCache = null;
    }

    /// <summary>
    /// Defers a task of the document while it is in the back/forward cache: the tasks whose document is not fully active are
    /// not runnable (https://html.spec.whatwg.org/multipage/webappapis.html#concept-task-runnable), they run once the document
    /// is reactivated.
    /// </summary>
    internal void DeferTaskUntilReactivated(Action task) => (_tasksDeferredByBackForwardCache ??= []).Add(task);

    /// <summary>
    /// The top-level document of the back/forward cache entry that holds this document (the document itself, or the document
    /// of an ancestor navigable), or null when the document is not in the back/forward cache.
    /// </summary>
    internal Document? GetBackForwardCacheRoot()
    {
        if (!IsInBackForwardCache)
            return null;

        var document = this;
        while (document.BrowsingContext is { Container: { } container } && container.NodeDocument.IsInBackForwardCache)
        {
            document = container.NodeDocument;
        }

        return document;
    }

    /// <summary>
    /// Evicts the document from the back/forward cache, when it is in it (e.g. a <c>BroadcastChannel</c> message is sent to a
    /// cached document): the document of the entry and its descendants are destroyed, and a traversal to the entry loads the
    /// document again. The reason is added to the bfcache blocking details of the document.
    /// </summary>
    internal void EvictFromBackForwardCache(string reason)
    {
        if (GetBackForwardCacheRoot() is not { BrowsingContext: { } navigable } root)
            return;

        MakeUnsalvageable(reason);
        navigable.Traversable.EvictFromBackForwardCache(root);
    }
}
