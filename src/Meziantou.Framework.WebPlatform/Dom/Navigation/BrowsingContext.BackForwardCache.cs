namespace Meziantou.Framework.WebPlatform.Dom;

// The back/forward cache (https://html.spec.whatwg.org/multipage/browsing-the-web.html#note-bfcache): a top-level traversable
// keeps the eligible documents it navigates away from alive in their session history entries ("intend to keep in bfcache"
// of https://html.spec.whatwg.org/multipage/document-lifecycle.html#unload-a-document), and a traversal to such an entry makes
// its document active again (https://html.spec.whatwg.org/multipage/browsing-the-web.html#reactivate-a-document) instead of
// loading it again.
public sealed partial class BrowsingContext
{
    /// <summary>The documents of the back/forward cache of the traversable, the one cached first first.</summary>
    private readonly List<Document> _backForwardCache = [];

    /// <summary>The number of documents in the back/forward cache of the traversable.</summary>
    internal int BackForwardCacheCount => _backForwardCache.Count;

    /// <summary>
    /// Gets the reasons that prevent the document of the navigable from being kept in the back/forward cache when the
    /// navigable navigates away from it (https://html.spec.whatwg.org/multipage/browsing-the-web.html#concept-document-bfcache-blocking-details,
    /// and the user-agent specific blocking reasons that the browsers converge on), before it is unloaded. The list is empty
    /// when the document can be kept.
    /// </summary>
    /// <param name="document">The document that is navigated away from: the active document of the navigable.</param>
    /// <param name="historyHandling">The history handling of the navigation (a traversal is a "replace" navigation).</param>
    /// <param name="traversalEntry">The entry of the traversal or the reload, or null.</param>
    private List<string> GetBackForwardCacheBlockingReasons(Document document, NavigationHistoryBehavior historyHandling, SessionHistoryEntry? traversalEntry)
    {
        var reasons = new List<string>();

        // Only the documents of the top-level traversables are cached (as in the browsers), and only when the session history
        // keeps their entry: not for a "replace" navigation nor a reload
        if (Options.BackForwardCacheSize <= 0 || Parent is not null || IsPrerendering || document.IsInitialAboutBlank || !ReferenceEquals(Document, document)
            || (traversalEntry is null ? historyHandling != NavigationHistoryBehavior.Push : ReferenceEquals(traversalEntry, ActiveEntry)))
        {
            reasons.Add("masked");
            return reasons;
        }

        // Another top-level browsing context can script the window: its opener, or a popup it opened (the browsers put the
        // popups opened with noopener in another browsing context group)
        if (HasOpenerRelationship())
        {
            reasons.Add("non-trivial-browsing-context-group");
        }

        if (document.IsNetworkErrorDocument)
        {
            reasons.Add("navigation-failure");
        }

        AddReasons(reasons, document.ResponseBackForwardCacheBlockingReasons);

        AddDocumentTreeBlockingReasons(document, reasons);
        return reasons;
    }

    /// <summary>Whether the opener of the traversable is alive, or another top-level browsing context of its group was opened by it.</summary>
    private bool HasOpenerRelationship()
    {
        if (Opener is { IsDestroyed: false })
            return true;

        foreach (var topLevel in GetGroupTopLevelBrowsingContexts())
        {
            if (!ReferenceEquals(topLevel, this) && !topLevel.IsDestroyed && ReferenceEquals(topLevel.Opener, this))
                return true;
        }

        return false;
    }

    /// <summary>The blocking reasons of a document and of the documents of its descendant navigables.</summary>
    private static void AddDocumentTreeBlockingReasons(Document document, List<string> reasons)
    {
        AddReasons(reasons, document.BackForwardCacheBlockingDetails);

        // The document is still loading (its parser, or the load of its subresources)
        if (document.ReadyState != DocumentReadyState.Complete)
        {
            AddReason(reasons, document.ReadyState == DocumentReadyState.Loading ? "parser-aborted" : "navigating");
        }

        if (document.RelevantWindow is { } window && ReferenceEquals(window.Document, document))
        {
            // As in Chromium (desktop) and Firefox, a document whose unload event listeners would not run is not cached
            if (window.HasEventListener("unload"))
            {
                AddReason(reasons, "unload-listener");
            }

            // The connections that the browsers do not keep alive in the cache
            if (EventSource.HasActiveEventSources(window) || window.HasFileSystemResources)
            {
                AddReason(reasons, "masked");
            }
        }

        if (document.ChildNavigables is { } children)
        {
            foreach (var child in children)
            {
                if (child.IsDestroyed)
                    continue;

                if (child.IsCrossDocumentNavigationOngoing)
                {
                    AddReason(reasons, "navigating");
                }

                AddDocumentTreeBlockingReasons(child.Document, reasons);
            }
        }
    }

    /// <summary>
    /// The blocking reasons found while the document and its descendants are unloaded (the unloading document cleanup steps,
    /// https://html.spec.whatwg.org/multipage/document-lifecycle.html#unloading-document-cleanup-steps): the WebSockets that are
    /// still connecting or open once the <c>pagehide</c> event handlers ran make the document unsalvageable ("websocket").
    /// </summary>
    private static void AddUnloadBlockingReasons(Document document, List<string> reasons)
    {
        if (document.RelevantWindow is { } window && ReferenceEquals(window.Document, document) && WebSocket.HasConnectingOrOpenWebSockets(window))
        {
            document.MakeUnsalvageable("websocket");
            AddReason(reasons, "websocket");
        }

        if (document.ChildNavigables is { } children)
        {
            foreach (var child in children)
            {
                if (!child.IsDestroyed)
                {
                    AddUnloadBlockingReasons(child.Document, reasons);
                }
            }
        }
    }

    private static void AddReasons(List<string> reasons, List<string>? newReasons)
    {
        if (newReasons is null)
            return;

        foreach (var reason in newReasons)
        {
            AddReason(reasons, reason);
        }
    }

    private static void AddReason(List<string> reasons, string reason)
    {
        if (!reasons.Contains(reason, StringComparer.Ordinal))
        {
            reasons.Add(reason);
        }
    }

    /// <summary>
    /// The user-agent specific blocking reasons of the response of a document
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#ua-specific-blocking-reasons), or null.
    /// </summary>
    private static List<string>? GetResponseBackForwardCacheBlockingReasons(Document document, DocumentParameters parameters)
    {
        List<string>? reasons = null;
        if (document.UrlRecord.Scheme is not ("http" or "https"))
        {
            (reasons ??= []).Add("response-scheme-not-http-or-https");
        }

        if (parameters.IsPostNavigation)
        {
            (reasons ??= []).Add("request-method-not-get");
        }

        if (parameters.Status is < 200 or > 299)
        {
            (reasons ??= []).Add("response-status-not-ok");
        }

        if (HasCacheControlDirective(parameters.Headers, "no-store"))
        {
            (reasons ??= []).Add("response-cache-control-no-store");
        }

        return reasons;
    }

    /// <summary>Whether a <c>Cache-Control</c> header of the response contains the directive.</summary>
    private static bool HasCacheControlDirective(IReadOnlyList<KeyValuePair<string, string>> headers, string directive)
    {
        foreach (var header in headers)
        {
            if (!header.Key.Equals("Cache-Control", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var part in header.Value.Split(','))
            {
                var name = part.AsSpan();
                if (name.IndexOf('=') is >= 0 and var equals)
                {
                    name = name[..equals];
                }

                if (name.Trim().Equals(directive, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether a session history entry of the traversable has the document state of the document.</summary>
    private bool IsDocumentInSessionHistory(Document document)
    {
        foreach (var entryList in Traversable.GetAllEntryLists())
        {
            foreach (var entry in entryList)
            {
                if (ReferenceEquals(entry.DocumentState.Document, document))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Keeps a document that the traversable navigated away from in the back/forward cache: the document and the documents of
    /// its descendant navigables are suspended (their tasks are deferred and their timers suspended), and they are no longer
    /// clients of the service workers. When the cache is full, the document cached first is evicted.
    /// </summary>
    private void StoreInBackForwardCache(Document document)
    {
        foreach (var cachedDocument in GetDocumentTree(document))
        {
            cachedDocument.EnterBackForwardCache();
            cachedDocument.SkipViewTransitionsForBackForwardCache();
            if (cachedDocument.ServiceWorkerClient is { } client)
            {
                client.IsInBackForwardCache = true;
            }

            // https://fetch.spec.whatwg.org/#process-deferred-fetches: the deferred fetches (fetchLater()) of a document that
            // enters the back/forward cache are sent, as the background synchronization is not granted
            DeferredFetch.ProcessAll(cachedDocument);
        }

        _backForwardCache.Add(document);
        while (_backForwardCache.Count > Options.BackForwardCacheSize)
        {
            EvictFromBackForwardCache(_backForwardCache[0]);
        }
    }

    /// <summary>The document and the documents of its descendant navigables, in tree order.</summary>
    private static List<Document> GetDocumentTree(Document document)
    {
        var documents = new List<Document> { document };
        for (var i = 0; i < documents.Count; i++)
        {
            if (documents[i].ChildNavigables is { } children)
            {
                foreach (var child in children)
                {
                    if (!child.IsDestroyed)
                    {
                        documents.Add(child.Document);
                    }
                }
            }
        }

        return documents;
    }

    /// <summary>
    /// Evicts a document from the back/forward cache of the traversable: the document and its descendants are destroyed
    /// (https://html.spec.whatwg.org/multipage/document-lifecycle.html#destroy-a-document-and-its-descendants), so that a
    /// traversal to its entry loads it again.
    /// </summary>
    internal void EvictFromBackForwardCache(Document document)
    {
        if (!_backForwardCache.Remove(document))
            return;

        foreach (var cachedDocument in GetDocumentTree(document))
        {
            cachedDocument.DiscardBackForwardCacheState();
        }

        DestroyDocument(document);
    }

    /// <summary>Evicts all the documents of the back/forward cache of the traversable (e.g. it is closed).</summary>
    private void ClearBackForwardCache()
    {
        while (_backForwardCache.Count > 0)
        {
            EvictFromBackForwardCache(_backForwardCache[^1]);
        }
    }

    /// <summary>Evicts the documents of the back/forward cache whose entries were removed from the session history (e.g. the forward entries cleared by a navigation).</summary>
    private void PruneBackForwardCache()
    {
        if (_backForwardCache.Count == 0)
            return;

        foreach (var document in _backForwardCache.ToArray())
        {
            if (!IsDocumentInSessionHistory(document))
            {
                EvictFromBackForwardCache(document);
            }
        }
    }

    /// <summary>Whether the document of the entry is in the back/forward cache of the navigable, which a traversal restores.</summary>
    private bool IsInBackForwardCache(SessionHistoryEntry entry) => entry.Document is { IsInBackForwardCache: true } document && _backForwardCache.Contains(document);

    /// <summary>
    /// Restores the document of an entry from the back/forward cache (https://html.spec.whatwg.org/multipage/browsing-the-web.html#apply-the-history-step,
    /// for a document that is not null): the displayed document is deactivated (and cached in turn when it is eligible), the
    /// document becomes the active document again, and it is reactivated with the documents of its descendant navigables
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#reactivate-a-document): its deferred tasks are queued again,
    /// its timers resume, and the <c>pageshow</c> event is fired with <c>persisted</c> set to true.
    /// </summary>
    private void RestoreFromBackForwardCache(SessionHistoryEntry entry)
    {
        var document = entry.Document!;
        var window = document.RelevantWindow!;
        var oldDocument = Document;
        _backForwardCache.Remove(document);

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#apply-the-history-step: the target name of the
        // traversable is cleared when the origin changes (see Activate)
        if (Parent is null && !oldDocument.IsInitialAboutBlank && !(IsAuxiliary && Opener is not null) && !OriginRecord.IsSameOrigin(document.Origin, oldDocument.Origin))
        {
            Name = "";
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#deactivate-a-document-for-a-cross-document-navigation
        FirePageSwap(oldDocument, document, NavigationHistoryBehavior.Replace, entry);
        var blockingReasons = GetBackForwardCacheBlockingReasons(oldDocument, NavigationHistoryBehavior.Replace, entry);
        var keepOldDocument = blockingReasons.Count == 0;
        UnloadDocumentAndDescendants(oldDocument, includeInitialAboutBlank: true, persisted: keepOldDocument);
        if (keepOldDocument)
        {
            AddUnloadBlockingReasons(oldDocument, blockingReasons);
            keepOldDocument = blockingReasons.Count == 0;
        }

        // The browsing context of the document is active again when another one replaced it (a browsing context group switch)
        if (document.ReplacedBrowsingContext is { } replaced)
        {
            document.ReplacedBrowsingContext = null;
            oldDocument.ReplacedBrowsingContext = new ReplacedBrowsingContext(ActiveBrowsingContext, Group);
            ActiveBrowsingContext.Discard(oldDocument.RelevantWindow);
            replaced.Instance.Undiscard();
            ActiveBrowsingContext = replaced.Instance;
            _group = replaced.Group;
        }

        var previousEntry = ActiveEntry;
        IsDelayingLoadEvents = false;
        _isTraversalOngoing = false;
        Document = document;
        SetActiveEntry(entry);
        if (oldDocument.RelevantWindow?.NavigationIfCreated is { } previousNavigation)
        {
            previousNavigation.ClearOngoingNavigation();
        }

        // The document was displaying another entry of its document state (e.g. an entry added by pushState()): the entry
        // changes once the document is reactivated
        var documentEntry = document.LatestEntry ?? entry;
        var documentsEntryChanged = !ReferenceEquals(documentEntry, entry);
        if (keepOldDocument && IsDocumentInSessionHistory(oldDocument))
        {
            StoreInBackForwardCache(oldDocument);
        }
        else
        {
            DestroyDocument(oldDocument);
        }

        foreach (var reactivatedDocument in GetDocumentTree(document))
        {
            var navigable = reactivatedDocument.BrowsingContext;
            if (navigable is null || navigable.IsDestroyed || !ReferenceEquals(navigable.Document, reactivatedDocument))
                continue;

            var isRestoredDocument = ReferenceEquals(reactivatedDocument, document);
            navigable.ReactivateDocument(reactivatedDocument, isRestoredDocument ? documentEntry : navigable.ActiveEntry, isRestoredDocument ? previousEntry : null);
        }

        if (documentsEntryChanged && ReferenceEquals(Document, document) && document.IsFullyActive)
        {
            document.SetUrl(entry.Url);
            UpdateDocumentForHistoryStepApplication(document, entry, NavigationType.Traverse);
        }

        DocumentActivated?.Invoke(document);
        Options.DocumentActivated?.Invoke(this, document);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#reactivate-a-document. The activation of the navigation API
    /// of the restored document is updated with its new entries (as in the browsers: the specification updates it before).
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="entry">The entry of the document.</param>
    /// <param name="previousEntry">The entry the traversal comes from, for the document of the traversal (not its descendants), or null.</param>
    private void ReactivateDocument(Document document, SessionHistoryEntry entry, SessionHistoryEntry? previousEntry)
    {
        FormControlState.ResetAutocompleteOffControls(document);
        document.LeaveBackForwardCache();
        if (document.ServiceWorkerClient is { } client)
        {
            client.IsInBackForwardCache = false;
        }

        if (document.RelevantWindow is not { } window || !ReferenceEquals(window.Document, document))
            return;

        if (!document.IsInitialAboutBlank && window.NavigationIfCreated is { } navigation)
        {
            navigation.UpdateEntriesForReactivation(GetSessionHistoryEntriesForNavigationApi(entry), entry);
        }

        if (!document.IsInitialAboutBlank && previousEntry is not null)
        {
            window.Navigation.UpdateActivation(previousEntry, NavigationType.Traverse, document);
        }

        document.IsUnloaded = false;
        window.Performance.IncrementNavigationId();
        if (document.ReadyState == DocumentReadyState.Complete && !document.PageShowing)
        {
            document.PageShowing = true;
            document.ResetHasBeenRevealed();
            document.UpdateVisibilityState(Top.IsVisible);
            using (window.ScriptEngine?.PrepareToRunScript(window))
            {
                window.FireLegacyTargetOverrideEvent(new PageTransitionEvent("pageshow", new PageTransitionEventInit { Persisted = true, Bubbles = true, Cancelable = true }));
            }

            // https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering: the document is revealed at its
            // next rendering opportunity (the documents are revealed when the pagereveal event is observed, or for an inbound
            // view transition)
            if ((window.HasEventListener("pagereveal") || document.InboundViewTransitionParams is not null) && document.IsFullyActive)
            {
                EventLoop.RequestReveal(document);
            }
        }

        window.RequestPendingAnimationFrames();
    }
}
