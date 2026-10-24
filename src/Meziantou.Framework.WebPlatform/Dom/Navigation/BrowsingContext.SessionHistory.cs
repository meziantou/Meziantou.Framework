using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The result of the application of a history step (https://html.spec.whatwg.org/multipage/browsing-the-web.html#apply-the-history-step).</summary>
internal enum HistoryStepResult
{
    Applied,
    CanceledByNavigate,
    InitiatorDisallowed,
}

/// <summary>A session history entry (https://html.spec.whatwg.org/multipage/browsing-the-web.html#session-history-entry).</summary>
internal sealed class SessionHistoryEntry(UrlRecord url, DocumentState documentState)
{
    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#she-step: the step of the joint session history of the
    /// traversable, or -1 ("pending") until the entry is added to the session history.
    /// </summary>
    public int Step { get; set; } = -1;

    public UrlRecord Url { get; } = url;

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#she-document-state</summary>
    public DocumentState DocumentState { get; } = documentState;

    /// <summary>The document of the document state, or null when it was destroyed (there is no back/forward cache).</summary>
    public Document? Document => DocumentState.Document;

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#she-classic-history-api-state: the serialized state, or null for the serialization of null.</summary>
    public object? ClassicHistoryApiState { get; init; }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#she-navigation-api-state: the serialized state, or null for the serialization of undefined.</summary>
    public object? NavigationApiState { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#she-navigation-api-key</summary>
    public string NavigationApiKey { get; set; } = Guid.NewGuid().ToString("D");

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#she-navigation-api-id</summary>
    public string NavigationApiId { get; } = Guid.NewGuid().ToString("D");

    public ScrollRestoration ScrollRestoration { get; set; }

    /// <summary>https://wicg.github.io/scroll-to-text-fragment/#she-directive-state</summary>
    public DirectiveState DirectiveState { get; set; } = new(null);

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#she-scroll-position: the scroll position of the viewport saved
    /// when the entry stopped being the active entry (the scroll positions of the other scrollable regions are not saved), or null.
    /// </summary>
    internal Css.Layout.PhysicalOffset? ScrollPositionData { get; set; }
}

/// <summary>
/// The directive state of session history entries (https://wicg.github.io/scroll-to-text-fragment/#directive-state): the
/// fragment directive of the URL of the navigation that created the entry, which is removed from the URL of the entry.
/// The entries of the fragment navigations that specify no directive share the one of the previous entry.
/// </summary>
internal sealed class DirectiveState(string? value)
{
    /// <summary>The fragment directive, or null.</summary>
    public string? Value { get; set; } = value;
}

/// <summary>
/// The document state of session history entries (https://html.spec.whatwg.org/multipage/browsing-the-web.html#document-state-2):
/// the entries of a document share it, and it holds the session histories of the child navigables of the document.
/// </summary>
internal sealed class DocumentState(Document? document)
{
    /// <summary>The document, or null when it was destroyed: a traversal to an entry of the document state loads it again.</summary>
    public Document? Document { get; set; } = document;

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#document-state-origin: the origin of the document when it was created.</summary>
    public OriginRecord? Origin { get; set; } = document?.Origin;

    /// <summary>The referrer policy of the document when it was destroyed (its <c>Referrer-Policy</c> header or meta element).</summary>
    public string DocumentReferrerPolicy { get; set; } = "";

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#document-state-request-referrer-policy</summary>
    public string RequestReferrerPolicy { get; set; } = ReferrerPolicies.Default;

    /// <summary>Whether the document of the document state is an initial about:blank document.</summary>
    public bool IsInitialAboutBlank { get; init; } = document?.IsInitialAboutBlank ?? false;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#document-state-history-policy-container: the policy
    /// container of a document whose URL requires storing the policy container in history (a local URL that is not a
    /// <c>blob:</c> URL), which a traversal or a reload gives to the document it loads again, or null.
    /// </summary>
    public PolicyContainer? HistoryPolicyContainer { get; set; }

    /// <summary>
    /// Whether <see cref="InitiatorOriginDocument"/> is set: the initiator origin of the document state of an about: document
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#document-state-initiator-origin), which a traversal gives
    /// to the document again, whatever the active document is.
    /// </summary>
    public bool HasInitiatorOrigin { get; set; }

    /// <summary>The document that holds the initiator origin, or null for an opaque origin.</summary>
    public Document? InitiatorOriginDocument { get; set; }

    /// <summary>
    /// The required CSP (https://w3c.github.io/webappsec-cspee/#navigation-params-required-csp) enforced by a <c>blob:</c>
    /// document, whose policy container is not stored in the session history: a traversal enforces it again.
    /// </summary>
    public string? HistoryRequiredContentSecurityPolicy { get; set; }

    /// <summary>The srcdoc resource of the document state, used to load an iframe srcdoc document again.</summary>
    public string? SrcdocResource { get; init; }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#document-state-nested-histories</summary>
    public List<NestedHistory> NestedHistories { get; } = [];
}

/// <summary>The session history of a child navigable (https://html.spec.whatwg.org/multipage/browsing-the-web.html#nested-history).</summary>
internal sealed class NestedHistory(BrowsingContext navigable)
{
    /// <summary>The child navigable (its id).</summary>
    public BrowsingContext Navigable { get; } = navigable;

    public List<SessionHistoryEntry> Entries { get; } = [];
}

/// <summary>
/// The joint session history (https://html.spec.whatwg.org/multipage/browsing-the-web.html#session-history-infrastructure):
/// the top-level traversable holds its session history entries, whose document states hold the session histories of the
/// child navigables, and the current session history step. The modifications are applied synchronously by the tasks of the
/// event loop (the session history traversal queue is not a separate queue).
/// </summary>
public sealed partial class BrowsingContext
{
    /// <summary>The session history entries of the traversable (https://html.spec.whatwg.org/multipage/document-sequences.html#tn-session-history-entries).</summary>
    private readonly List<SessionHistoryEntry> _sessionHistory = [];

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#tn-current-session-history-step (of the traversable).</summary>
    internal int CurrentStep { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#nav-active-history-entry</summary>
#pragma warning disable IDE0370 // False positive: the constructor does not set the entries (CS8618)
    internal SessionHistoryEntry ActiveEntry { get; private set; } = null!;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/document-sequences.html#nav-current-history-entry: the entry of the current
    /// step, which a traversal changes before the entry becomes the active entry (by a task).
    /// </summary>
    internal SessionHistoryEntry CurrentEntry { get; private set; } = null!;
#pragma warning restore IDE0370

    /// <summary>
    /// Whether the ongoing navigation is a traversal (https://html.spec.whatwg.org/multipage/browsing-the-web.html#ongoing-navigation):
    /// a cross-document traversal is loading the document of an entry, and the other navigations are ignored until it completes.
    /// </summary>
    private bool _isTraversalOngoing;

    /// <summary>The maximum number of navigations and history updates of a navigable in <see cref="NavigationRateLimitInterval"/> (as in Chromium and Firefox).</summary>
    private const int NavigationRateLimit = 200;

    private static readonly TimeSpan NavigationRateLimitInterval = TimeSpan.FromSeconds(10);

    private int _navigationRateLimitCount;
    private long _navigationRateLimitStart;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/document-sequences.html#allowed-to-perform-a-navigation-or-history-update: as in
    /// the browsers, a navigable is blocked after 200 navigations or history updates (the Location navigations,
    /// <c>pushState()</c>, <c>replaceState()</c> and the traversals of <c>history.go()</c>) in 10 seconds, so that a script
    /// cannot flood the session history or reload a document endlessly.
    /// </summary>
    internal bool IsAllowedToPerformNavigationOrHistoryUpdate()
    {
        var timeProvider = EventLoop.TimeProvider;
        var now = timeProvider.GetTimestamp();
        if (_navigationRateLimitCount == 0 || timeProvider.GetElapsedTime(_navigationRateLimitStart, now) >= NavigationRateLimitInterval)
        {
            _navigationRateLimitStart = now;
            _navigationRateLimitCount = 0;
        }

        _navigationRateLimitCount++;
        return _navigationRateLimitCount <= NavigationRateLimit;
    }

    /// <summary>Sets the active entry, which is also the current entry.</summary>
    private void SetActiveEntry(SessionHistoryEntry entry)
    {
        SavePersistedState(ActiveEntry);
        ActiveEntry = entry;
        CurrentEntry = entry;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#save-persisted-state: saves the scroll position of the viewport
    /// of the document in its entry (only when the document was laid out: the scroll positions of a document that was never
    /// laid out are 0).
    /// </summary>
    private void SavePersistedState(SessionHistoryEntry? entry)
    {
        if (entry is not null && ReferenceEquals(entry.Document, Document))
        {
            entry.ScrollPositionData = Document.LayoutEngineOrNull is { IsLayoutRequested: true } engine ? engine.ViewportScrollState.Offset : Css.Layout.PhysicalOffset.Zero;
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#restore-persisted-user-state: "If entry's scroll restoration mode
    /// is "auto", and entry's document's relevant global object's navigation API's suppress normal scroll restoration during
    /// ongoing navigation is false, then restore scroll position data given entry" (a scroll of the viewport with the root
    /// element as the associated element: smooth with <c>scroll-behavior: smooth</c>, as in the browsers).
    /// </summary>
    private void RestorePersistedState(SessionHistoryEntry entry)
    {
        if (entry.ScrollRestoration == ScrollRestoration.Auto && Document.RelevantWindow?.Navigation.SuppressNormalScrollRestorationDuringOngoingNavigation is not true)
        {
            RestoreScrollPositionData(entry);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#restore-scroll-position-data: scrolls the viewport to the saved position of the entry.</summary>
    internal void RestoreScrollPositionData(SessionHistoryEntry entry)
    {
        var document = Document;
        if (!ReferenceEquals(entry.Document, document) || entry.ScrollPositionData is not { } position || document.LayoutEngineOrNull is not { } engine)
            return;

        // A document that was never laid out is at the scroll position 0 (a pending scroll to a fragment is canceled)
        if (!engine.IsLayoutRequested)
        {
            engine.CancelPendingScrollIntoView();
            if (position == Css.Layout.PhysicalOffset.Zero)
                return;
        }

        if (engine.EnsureLayout() is not null)
        {
            _ = engine.PerformScroll(engine.ViewportScrollState, position, ScrollBehavior.Auto, document.DocumentElement);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/document-sequences.html#nav-traversable (the top-level traversable).</summary>
    internal BrowsingContext Traversable => Top;

    /// <summary>
    /// Gets the session history entries of the navigable (https://html.spec.whatwg.org/multipage/browsing-the-web.html#getting-session-history-entries):
    /// the entries of the traversable, or the entries of the nested history of the navigable, or null when the navigable is
    /// not part of the session history of its traversable (e.g. it was destroyed).
    /// </summary>
    internal List<SessionHistoryEntry>? GetSessionHistoryEntries()
    {
        var traversable = Traversable;
        if (ReferenceEquals(traversable, this))
            return _sessionHistory;

        var documentStates = new List<DocumentState>();
        foreach (var entry in traversable._sessionHistory)
        {
            if (!documentStates.Contains(entry.DocumentState))
            {
                documentStates.Add(entry.DocumentState);
            }
        }

        for (var i = 0; i < documentStates.Count; i++)
        {
            foreach (var nestedHistory in documentStates[i].NestedHistories)
            {
                if (ReferenceEquals(nestedHistory.Navigable, this))
                    return nestedHistory.Entries;

                foreach (var entry in nestedHistory.Entries)
                {
                    if (!documentStates.Contains(entry.DocumentState))
                    {
                        documentStates.Add(entry.DocumentState);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>The lists of entries of the traversable and of all the nested histories (https://html.spec.whatwg.org/multipage/browsing-the-web.html#clear-the-forward-session-history).</summary>
    private IEnumerable<List<SessionHistoryEntry>> GetAllEntryLists()
    {
        var entryLists = new List<List<SessionHistoryEntry>> { _sessionHistory };
        var documentStates = new HashSet<DocumentState>();
        for (var i = 0; i < entryLists.Count; i++)
        {
            yield return entryLists[i];
            foreach (var entry in entryLists[i])
            {
                if (!documentStates.Add(entry.DocumentState))
                    continue;

                foreach (var nestedHistory in entry.DocumentState.NestedHistories)
                {
                    entryLists.Add(nestedHistory.Entries);
                }
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#clear-the-forward-session-history (of the traversable).</summary>
    private void ClearForwardSessionHistory()
    {
        var step = CurrentStep;
        List<SessionHistoryEntry>? removedEntries = null;
        foreach (var entryList in GetAllEntryLists().ToList())
        {
            foreach (var entry in entryList)
            {
                if (entry.Step > step)
                {
                    (removedEntries ??= []).Add(entry);
                }
            }

            entryList.RemoveAll(entry => entry.Step > step);
        }

        if (removedEntries is null)
            return;

        // The navigation API entries of the other navigables whose forward entries are removed are disposed (e.g. a push in a
        // child navigable removes the forward entries of its parent). The specification only updates the History object of the
        // navigables that do not change; the browsers also update their entry list and fire the dispose events. The
        // navigable that navigates updates its own entry list.
        foreach (var navigable in GetDescendantNavigables().Prepend(this).ToList())
        {
            if (navigable.Document.RelevantWindow?.NavigationIfCreated is { } navigation && navigation.HasEntryOf(removedEntries))
            {
                navigable.Document.QueueGlobalTask(() => navigation.DisposeEntriesRemovedFromSessionHistory(removedEntries));
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#getting-all-used-history-steps (of the traversable): the sorted steps.</summary>
    internal List<int> GetAllUsedHistorySteps()
    {
        var steps = new SortedSet<int>();
        foreach (var entryList in GetAllEntryLists())
        {
            foreach (var entry in entryList)
            {
                if (entry.Step >= 0)
                {
                    steps.Add(entry.Step);
                }
            }
        }

        return [.. steps];
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#getting-the-used-step (of the traversable): the greatest used step less than or equal to <paramref name="step"/>.</summary>
    private int GetUsedStep(int step)
    {
        var result = 0;
        foreach (var usedStep in GetAllUsedHistorySteps())
        {
            if (usedStep <= step)
            {
                result = usedStep;
            }
        }

        return result;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#getting-the-history-object-length-and-index: the length and
    /// the index of the History object of the documents of the traversable.
    /// </summary>
    internal (int Length, int Index) GetHistoryObjectLengthAndIndex()
    {
        var traversable = Traversable;
        var steps = traversable.GetAllUsedHistorySteps();
        var index = steps.IndexOf(traversable.CurrentStep);
        if (index < 0)
        {
            index = Math.Max(0, steps.FindLastIndex(step => step < traversable.CurrentStep));
        }

        return (steps.Count, index);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#getting-the-target-history-entry: the entry of the navigable with the greatest step less than or equal to <paramref name="step"/>.</summary>
    internal SessionHistoryEntry? GetTargetHistoryEntry(int step)
    {
        SessionHistoryEntry? result = null;
        foreach (var entry in GetSessionHistoryEntries() ?? [])
        {
            if (entry.Step <= step && (result is null || entry.Step >= result.Step))
            {
                result = entry;
            }
        }

        return result;
    }

    /// <summary>
    /// Adds the entry of a navigation to the session history of the navigable
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#finalize-a-cross-document-navigation and
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#finalize-a-same-document-navigation): a "push" clears the
    /// forward session history and gets the next step, a "replace" takes the place and the step of the active entry. The
    /// entry becomes the active entry.
    /// </summary>
    /// <remarks>
    /// The entry of a same-document navigation (a fragment navigation, pushState()) becomes the active entry immediately, but
    /// it is added to the session history after the traversals queued before it computed their target step
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#sync-navigations-jump-queue).
    /// </remarks>
    private void AddSessionHistoryEntry(SessionHistoryEntry entry, NavigationHistoryBehavior historyHandling, bool isSameDocument = false)
    {
        var traversable = Traversable;
        var entryToReplace = historyHandling == NavigationHistoryBehavior.Replace ? ActiveEntry : null;
        if (isSameDocument && traversable._pendingTraversals > 0)
        {
            SetActiveEntry(entry);
            traversable._pendingSynchronousNavigations.Add(() =>
            {
                if (ReferenceEquals(ActiveEntry, entry))
                {
                    FinalizeNavigation(entry, entryToReplace);
                }
            });
            return;
        }

        FinalizeNavigation(entry, entryToReplace);
        SetActiveEntry(entry);
    }

    private void FinalizeNavigation(SessionHistoryEntry entry, SessionHistoryEntry? entryToReplace)
    {
        var traversable = Traversable;
        var entries = GetSessionHistoryEntries();
        if (entryToReplace is null || entries is null || entries.IndexOf(entryToReplace) is not (>= 0 and var index))
        {
            traversable.ClearForwardSessionHistory();
            entry.Step = traversable.CurrentStep + 1;
            entries?.Add(entry);
            traversable.CurrentStep = entry.Step;
        }
        else
        {
            entries[index] = entry;
            entry.Step = entryToReplace.Step;

            // https://html.spec.whatwg.org/multipage/browsing-the-web.html#finalize-a-cross-document-navigation, step 9.3: the
            // navigation API key of a same-origin replacement is kept
            if (entry.DocumentState.Origin is { } origin && entryToReplace.DocumentState.Origin is { } replacedOrigin && OriginRecord.IsSameOrigin(origin, replacedOrigin))
            {
                entry.NavigationApiKey = entryToReplace.NavigationApiKey;
            }
        }
    }

    /// <summary>The number of traversals queued (whose target step is not computed yet), of the traversable.</summary>
    private int _pendingTraversals;

    /// <summary>The same-document navigations to add to the session history once the pending traversals computed their target step.</summary>
    private readonly List<Action> _pendingSynchronousNavigations = [];

    private void FlushSynchronousNavigations()
    {
        while (_pendingSynchronousNavigations.Count > 0)
        {
            var navigation = _pendingSynchronousNavigations[0];
            _pendingSynchronousNavigations.RemoveAt(0);
            navigation();
        }
    }

    /// <summary>
    /// The initial entry of the session history (https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-top-level-traversable):
    /// the step 0 of a top-level traversable. The initial entry of a child navigable is added to the document state of the
    /// active entry of its parent by <see cref="AddChildNavigableHistory"/>.
    /// </summary>
    private void SetInitialSessionHistoryEntry(SessionHistoryEntry entry)
    {
        SetActiveEntry(entry);
        if (Parent is null)
        {
            entry.Step = 0;
            _sessionHistory.Add(entry);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/document-sequences.html#create-a-new-child-navigable, step 12: the initial entry
    /// of the child navigable gets the step of the first entry of the document state of its parent, and its nested history
    /// is added to that document state.
    /// </summary>
    private void AddChildNavigableHistory()
    {
        var parent = Parent!;
        var parentDocumentState = parent.ActiveEntry.DocumentState;
        var parentEntries = parent.GetSessionHistoryEntries() ?? [];
        var firstEntry = parentEntries.Find(entry => ReferenceEquals(entry.DocumentState, parentDocumentState));
        ActiveEntry.Step = firstEntry?.Step ?? parent.ActiveEntry.Step;
        var nestedHistory = new NestedHistory(this);
        nestedHistory.Entries.Add(ActiveEntry);
        parentDocumentState.NestedHistories.Add(nestedHistory);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/document-sequences.html#destroy-a-child-navigable, steps 6 to 8: the nested
    /// history of the navigable is removed from the document state of the active entry of its parent, and the current step of
    /// the traversable becomes a used step.
    /// </summary>
    private void RemoveChildNavigableHistory()
    {
        if (Parent is not { } parent)
            return;

        parent.ActiveEntry.DocumentState.NestedHistories.RemoveAll(nestedHistory => ReferenceEquals(nestedHistory.Navigable, this));

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#update-for-navigable-creation/destruction
        var traversable = parent.Traversable;
        traversable.CurrentStep = traversable.GetUsedStep(traversable.CurrentStep);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#traverse-the-history-by-a-delta: the traversable applies the
    /// step at <paramref name="delta"/> of its current step in the used steps.
    /// </summary>
    /// <param name="delta">The delta.</param>
    /// <param name="sourceDocument">The document that initiated the traversal (its navigable must be allowed to navigate the navigables that change), or null.</param>
    internal void TraverseByDelta(int delta, Document? sourceDocument = null)
    {
        var sourceNavigable = sourceDocument?.BrowsingContext;
        var sourceFlags = sourceDocument?.ActiveSandboxingFlags ?? SandboxingFlags.None;
        var hasTransientActivation = sourceDocument?.RelevantWindow?.HasTransientActivation ?? false;
        var traversable = Traversable;
        traversable.QueueSessionHistoryTraversal(() =>
        {
            // As in Chromium and Firefox, a traversal is ignored while a previous traversal is loading the document of the
            // top-level traversable (e.g. the second of two history.go() calls of the same task), instead of being applied from
            // the step of the previous one. The traversals of the child navigables are applied one after the other, as specified.
            if (traversable._isTraversalOngoing)
            {
                traversable.FlushSynchronousNavigations();
                return;
            }

            // The same-document navigations done after the traversal was queued are added to the session history after the
            // target step is computed, and before it is applied
            var steps = traversable.GetAllUsedHistorySteps();
            var targetIndex = steps.IndexOf(traversable.CurrentStep) + delta;
            traversable.FlushSynchronousNavigations();
            if (targetIndex < 0 || targetIndex >= steps.Count)
                return;

            traversable.ApplyTraverseHistoryStep(steps[targetIndex], navigable => sourceNavigable is null || Sandboxing.IsAllowedToNavigate(sourceNavigable, navigable, sourceFlags, hasTransientActivation));
        }, flushSynchronousNavigations: false);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/document-sequences.html#tn-append-session-history-traversal-steps (of the
    /// traversable): the steps run in a task of the event loop, after the same-document navigations done in the meantime
    /// are added to the session history (unless the steps do it themselves).
    /// </summary>
    internal void QueueSessionHistoryTraversal(Action steps, bool flushSynchronousNavigations = true)
    {
        var traversable = Traversable;
        traversable._pendingTraversals++;
        void Run()
        {
            traversable._pendingTraversals--;
            if (traversable.IsDestroyed)
                return;

            if (flushSynchronousNavigations)
            {
                traversable.FlushSynchronousNavigations();
            }

            steps();
        }

        QueueTask(() =>
        {
            // The steps of the session history traversal queue run one after the other: they wait for the documents of the
            // previous traversal to be updated (e.g. a traversal started by the navigate event of another traversal)
            if (traversable._pendingHistoryStepUpdates > 0 || traversable._waitingTraversals.Count > 0)
            {
                traversable._waitingTraversals.Enqueue(Run);
                return;
            }

            Run();
        });
    }

    /// <summary>The number of documents that a traversal of the traversable still has to update (same-document entries, updated by a task).</summary>
    private int _pendingHistoryStepUpdates;

    /// <summary>The session history traversal steps that wait for the previous traversal to update its documents.</summary>
    private readonly Queue<Action> _waitingTraversals = new();

    /// <summary>A document of a traversal is updated: the waiting session history traversal steps run, by a task, once all are.</summary>
    private void OnHistoryStepUpdateCompleted()
    {
        _pendingHistoryStepUpdates--;
        if (_pendingHistoryStepUpdates > 0 || _waitingTraversals.Count == 0)
            return;

        QueueTask(() =>
        {
            while (_pendingHistoryStepUpdates == 0 && _waitingTraversals.TryDequeue(out var traversal))
            {
                traversal();
            }
        });
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#resume-applying-the-traverse-history-step: the traversal
    /// of a navigate event that intercepted it is applied without firing the navigate event again.
    /// </summary>
    internal void ResumeApplyingTraverseHistoryStep(int step, UserNavigationInvolvement userInvolvement) => ApplyTraverseHistoryStep(step, isAllowedToNavigate: null, checkForCancelation: false, userInvolvement);

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#getting-session-history-entries-for-the-navigation-api:
    /// the entries of the navigable around <paramref name="entry"/> that are same origin with it.
    /// </summary>
    internal List<SessionHistoryEntry> GetSessionHistoryEntriesForNavigationApi(SessionHistoryEntry entry)
    {
        var rawEntries = GetSessionHistoryEntries() ?? [];
        var startingIndex = rawEntries.IndexOf(entry);
        if (startingIndex < 0)
            return [entry];

        var result = new List<SessionHistoryEntry> { entry };
        var startingOrigin = entry.DocumentState.Origin;
        bool IsSameOrigin(SessionHistoryEntry other) => startingOrigin is not null && other.DocumentState.Origin is { } origin && OriginRecord.IsSameOrigin(origin, startingOrigin);
        for (var i = startingIndex - 1; i >= 0 && IsSameOrigin(rawEntries[i]); i--)
        {
            result.Insert(0, rawEntries[i]);
        }

        for (var i = startingIndex + 1; i < rawEntries.Count && IsSameOrigin(rawEntries[i]); i++)
        {
            result.Add(rawEntries[i]);
        }

        return result;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#apply-the-history-step for a traversal (of the traversable):
    /// the navigables whose current entry changes get their target entry. A same-document entry is activated by a task (its
    /// document fires popstate and hashchange); the document of an entry of another document is loaded again (there is no
    /// back/forward cache), and becomes the document of the entry.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="isAllowedToNavigate">Whether the initiator of the traversal can navigate a navigable: the traversal is canceled when it would change a navigable it cannot navigate.</param>
    /// <param name="checkForCancelation">Whether the navigate event of the traversable is fired (it can cancel or intercept the traversal).</param>
    /// <param name="userInvolvement">The user navigation involvement.</param>
    internal HistoryStepResult ApplyTraverseHistoryStep(int step, Func<BrowsingContext, bool>? isAllowedToNavigate = null, bool checkForCancelation = true, UserNavigationInvolvement userInvolvement = UserNavigationInvolvement.None)
    {
        var targetStep = GetUsedStep(step);
        var changingNavigables = new List<(BrowsingContext Navigable, SessionHistoryEntry Entry)>();
        var navigablesToCheck = new List<BrowsingContext> { this };
        for (var i = 0; i < navigablesToCheck.Count; i++)
        {
            var navigable = navigablesToCheck[i];
            if (navigable.GetTargetHistoryEntry(targetStep) is not { } targetEntry)
                continue;

            if (!ReferenceEquals(targetEntry, navigable.CurrentEntry))
            {
                changingNavigables.Add((navigable, targetEntry));
            }

            // The child navigables are checked when the document of the navigable does not change
            if (ReferenceEquals(targetEntry.Document, navigable.Document))
            {
                navigablesToCheck.AddRange(navigable.Children);
            }
        }

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#apply-the-history-step, step 2: "initiator-disallowed"
        if (isAllowedToNavigate is not null && changingNavigables.Exists(change => !isAllowedToNavigate(change.Navigable)))
            return HistoryStepResult.InitiatorDisallowed;

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#checking-if-unloading-is-canceled: the navigate event of
        // the traversable, for a same-origin entry, which can cancel or intercept the traversal
        var isBeforeUnloadFiredAtTraversable = false;
        if (checkForCancelation && changingNavigables.Find(change => ReferenceEquals(change.Navigable, this)) is { Entry: { } traversableTarget }
            && traversableTarget.DocumentState.Origin is { } targetOrigin && CurrentEntry.DocumentState.Origin is { } currentOrigin && OriginRecord.IsSameOrigin(targetOrigin, currentOrigin)
            && Document.RelevantWindow is { } traversableWindow)
        {
            // "Because we want beforeunload events to fire before navigate events, this means we need to fire beforeunload for
            // traversable here (if applicable)"
            if (!ReferenceEquals(traversableTarget.Document, Document))
            {
                isBeforeUnloadFiredAtTraversable = true;
                FireBeforeUnload();
            }

            if (!IsDestroyed && ReferenceEquals(traversableWindow.Document, Document) && !traversableWindow.Navigation.FireTraverseNavigateEvent(traversableTarget, userInvolvement))
                return HistoryStepResult.CanceledByNavigate;
        }

        // The beforeunload event of the other documents that the traversal unloads is fired before the navigate events of
        // their navigables (the navigations that load the entries do not fire it again)
        foreach (var (navigable, targetEntry) in changingNavigables)
        {
            if (!navigable.IsDestroyed && !ReferenceEquals(targetEntry.Document, navigable.Document) && !(isBeforeUnloadFiredAtTraversable && ReferenceEquals(navigable, this)))
            {
                navigable.FireBeforeUnload();
            }
        }

        CurrentStep = targetStep;
        foreach (var (navigable, targetEntry) in changingNavigables)
        {
            if (navigable.IsDestroyed)
                continue;

            // https://html.spec.whatwg.org/multipage/browsing-the-web.html#apply-the-history-step, step 12.3: the ongoing
            // navigation becomes "traversal", which cancels the navigation in progress (the ongoing navigate event of a
            // cross-document traversal is not aborted)
            var displayedEntry = navigable.ActiveEntry;
            navigable.CurrentEntry = targetEntry;
            navigable.CancelOngoingNavigation(informNavigationApi: false);
            if (!ReferenceEquals(targetEntry.Document, navigable.Document) && navigable.Document.RelevantWindow?.NavigationIfCreated is { } previousNavigation)
            {
                previousNavigation.ClearOngoingNavigation();
            }

            // The navigate event of the other navigables (not cancelable)
            if (!ReferenceEquals(navigable, this) && !ReferenceEquals(targetEntry, displayedEntry) && targetEntry.DocumentState.Origin is { } entryOrigin && displayedEntry.DocumentState.Origin is { } displayedOrigin && OriginRecord.IsSameOrigin(entryOrigin, displayedOrigin)
                && navigable.Document.RelevantWindow is { } navigableWindow)
            {
                navigableWindow.Navigation.FireTraverseNavigateEvent(targetEntry, userInvolvement);
            }

            if (ReferenceEquals(targetEntry.Document, navigable.Document))
            {
                var document = navigable.Document;
                _pendingHistoryStepUpdates++;
                QueueTask(() =>
                {
                    try
                    {
                        if (!document.IsFullyActive || navigable.IsDestroyed || !ReferenceEquals(targetEntry.Document, navigable.Document))
                            return;

                        navigable.SavePersistedState(navigable.ActiveEntry);
                        navigable.ActiveEntry = targetEntry;
                        navigable.Document.SetUrl(targetEntry.Url);
                        navigable.UpdateDocumentForHistoryStepApplication(navigable.Document, targetEntry, NavigationType.Traverse);
                    }
                    finally
                    {
                        OnHistoryStepUpdateCompleted();
                    }
                });
            }
            else
            {
                // The load starts after the tasks of the previous traversals (the traversals are processed in order). The
                // other navigations of the navigable are ignored until the traversal completes.
                navigable._isTraversalOngoing = true;
                navigable.Document.QueueGlobalTask(() =>
                {
                    if (!navigable.IsDestroyed && ReferenceEquals(navigable.CurrentEntry, targetEntry))
                    {
                        navigable._beforeUnloadFiredForEntry = targetEntry;
                        try
                        {
                            navigable.Navigate(targetEntry.Url, navigable.Document, NavigationHistoryBehavior.Replace, targetEntry.DocumentState.SrcdocResource, traversalEntry: targetEntry);
                        }
                        finally
                        {
                            navigable._beforeUnloadFiredForEntry = null;
                        }
                    }
                });
            }
        }

        return HistoryStepResult.Applied;
    }
}
