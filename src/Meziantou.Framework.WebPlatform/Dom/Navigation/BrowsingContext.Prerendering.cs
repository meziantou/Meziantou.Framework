using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// Prerendering (https://wicg.github.io/nav-speculation/prerendering.html): a prerendering navigable is a top-level
// traversable that is not shown, created for a prerender candidate of the speculation rules of a document. When a navigable
// navigates to its URL, the prerendered document is activated in place of the navigation: it becomes the active document of
// that navigable (the specification replaces the navigable by the prerendering one in the user interface; the
// BrowsingContext object of the application stays, so the document moves instead).
public sealed partial class BrowsingContext
{
    /// <summary>Whether the top-level traversable is a prerendering traversable (https://wicg.github.io/nav-speculation/prerendering.html#prerendering-traversable).</summary>
    internal bool IsPrerendering { get; private set; }

    /// <summary>The prerender record of a prerendering traversable.</summary>
    internal PrefetchRecord? PrerenderRecord { get; private set; }

    /// <summary>Whether the navigable is a top-level traversable that was just created for a navigation (a new window).</summary>
    private bool _isNewTopLevelTraversable;

    /// <summary>The document whose speculation rules created the prerendering traversable.</summary>
    internal Document? PrerenderInitiator { get; private set; }

    /// <summary>
    /// https://wicg.github.io/nav-speculation/prerendering.html#create-a-prerendering-traversable: a top-level traversable in a
    /// new browsing context group, which is not shown (the visibility state of its documents is "hidden"). Its session storage
    /// is a copy of the one of the initiator.
    /// </summary>
    internal BrowsingContext CreatePrerenderingBrowsingContext(Document initiator, PrefetchRecord record)
    {
        var owner = GroupOwner;
        var context = new BrowsingContext(Options, parent: null, container: null, owner, creator: null, name: "", prerendering: true)
        {
            _group = new BrowsingContextGroup(),
            PrerenderRecord = record,
            PrerenderInitiator = initiator,
        };
        (owner._auxiliaryBrowsingContexts ??= []).Add(context);
        Top.SessionStorageShed.CopyTo(context.SessionStorageShed);
        return context;
    }

    /// <summary>
    /// The requests of the navigations of a prerendering navigable (and of its descendants) are speculative
    /// (https://wicg.github.io/nav-speculation/prerendering.html#interaction-with-fetch): <c>Sec-Purpose: prefetch;prerender</c>,
    /// and the tags of the speculation rules for the initial navigation.
    /// </summary>
    private void PrepareRequestOfPrerenderingNavigable(FetchRequest request)
    {
        var top = Top;
        if (!top.IsPrerendering)
            return;

        request.IsPrefetch = true;
        request.SecPurpose = "prefetch;prerender";
        request.HeaderList.Add(new("Sec-Purpose", "prefetch;prerender"));
        if (Parent is null && Document.IsInitialAboutBlank && PrerenderRecord is { } record)
        {
            request.HeaderList.Add(new("Sec-Speculation-Tags", NavigationalPrefetch.SerializeTags(record.Tags)));
        }
    }

    /// <summary>
    /// Cancels the prerendering of a prerendering traversable (https://wicg.github.io/nav-speculation/prerendering.html#cancel-prerendering):
    /// its record is discarded and its documents are destroyed.
    /// </summary>
    internal void CancelPrerendering()
    {
        var top = Top;
        if (!top.IsPrerendering)
            return;

        if (top.PrerenderInitiator is { } initiator && top.PrerenderRecord is { } record)
        {
            NavigationalPrefetch.CancelAndDiscard(initiator, record);
        }
        else
        {
            top.Destroy();
        }
    }

    /// <summary>
    /// Whether the response of a navigation of a prerendering traversable can be prerendered
    /// (https://wicg.github.io/nav-speculation/prerendering.html#navigation-patches): a response with an ok status that creates
    /// a document, from the origin of the initiator, or from another origin of its site when the response opts in with
    /// <c>Supports-Loading-Mode: credentialed-prerender</c>. Otherwise the prerendering is canceled.
    /// </summary>
    private bool IsPrerenderingResponseAllowed(UrlRecord url, DocumentResource? resource)
    {
        if (resource is null || HasNoDocument(resource) || resource.StatusCode is < 200 or > 299 || PrerenderInitiator is not { } initiator)
            return false;

        var responseUrl = resource.Url is { } resourceUrl ? UrlParser.Parse(resourceUrl, baseUrl: null) ?? url : url;
        if (responseUrl.Scheme is not ("http" or "https"))
            return false;

        var origin = OriginRecord.FromUrl(responseUrl);
        if (OriginRecord.IsSameOrigin(origin, initiator.Origin))
            return true;

        if (!OriginRecord.IsSameSite(origin, initiator.Origin))
            return false;

        return Prerendering.HasLoadingMode(resource.Headers, "credentialed-prerender");
    }

    /// <summary>
    /// Whether the response of a navigation of a child navigable of a prerendering traversable waits for the activation: its
    /// URL is cross-origin with the prerendered document, and the response of the document of its container did not opt in
    /// with <c>Supports-Loading-Mode: prerender-cross-origin-frames</c> (as in Chromium).
    /// </summary>
    private bool IsPrerenderingChildResponseDelayed(UrlRecord url, DocumentResource? resource, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Document? prerenderingDocument)
    {
        prerenderingDocument = null;
        if (Parent is null || Container is not { } container || Top is not { IsPrerendering: true } top || container.NodeDocument.SupportsPrerenderingCrossOriginFrames)
            return false;

        // The origin of a sandboxed document (without allow-same-origin) is opaque
        var responseUrl = resource?.Url is { } resourceUrl ? UrlParser.Parse(resourceUrl, baseUrl: null) ?? url : url;
        var sandboxed = (GetIframeSandboxingFlags() & SandboxingFlags.Origin) != SandboxingFlags.None;
        if (responseUrl.Scheme is not ("http" or "https") || (!sandboxed && OriginRecord.IsSameOrigin(OriginRecord.FromUrl(responseUrl), top.Document.Origin)))
            return false;

        prerenderingDocument = top.Document;
        return true;
    }

    /// <summary>
    /// https://wicg.github.io/nav-speculation/prerendering.html#navigate-activation: a GET navigation of a top-level navigable,
    /// started by its active document, to the URL of a prerender record of that document activates the prerendered document
    /// instead of fetching the URL. A prerendering that has not created its document yet (or that is navigating) is canceled,
    /// as in Chromium, and the navigation fetches the URL.
    /// </summary>
    private bool TryActivatePrerender(UrlRecord url, Document? sourceDocument, NavigationHistoryBehavior historyHandling, PostResource? postResource, SessionHistoryEntry? traversalEntry, DocumentParameters parameters)
    {
        // The first navigation of a new top-level traversable without opener (window.open(url, "_blank", "noopener"))
        var isNewWindow = _isNewTopLevelTraversable && Document.IsInitialAboutBlank && Opener is null;
        _isNewTopLevelTraversable = false;
        if (Parent is not null || postResource is not null || traversalEntry is not null || IsPrerendering || sourceDocument?.PrefetchRecords is not { Count: > 0 } records)
            return false;

        foreach (var record in records.ToArray())
        {
            if (!record.IsPrerender || record.PrerenderingNavigable is not { } prerendering)
                continue;

            if (prerendering.IsDestroyed)
            {
                NavigationalPrefetch.CancelAndDiscard(sourceDocument, record);
                continue;
            }

            // A prerendering for a new window (target_hint: "_blank") is activated by a navigation of a new top-level
            // traversable; the other ones by a navigation of the navigable of the initiator
            if (!record.Matches(url) || isNewWindow != Prerendering.IsForNewWindow(record) || (!isNewWindow && !ReferenceEquals(sourceDocument.BrowsingContext, this)))
                continue;

            // The activation switches browsing context group: as in Chromium, a navigable whose group has other top-level
            // traversables (its opener, or the windows it opened) keeps its browsing context and fetches the URL
            if (record.State != PrefetchState.Completed || prerendering._ongoingNavigation != 0
                || GetGroupTopLevelBrowsingContexts().Any(topLevel => !ReferenceEquals(topLevel, this) && !topLevel.IsDestroyed && ReferenceEquals(topLevel.Group, Group)))
            {
                NavigationalPrefetch.CancelAndDiscard(sourceDocument, record);
                continue;
            }

            records.Remove(record);
            record.PrerenderingNavigable = null;

            // The prerendered document gets the URL of the navigation, which may differ from the prerendered URL by its
            // fragment or by its query (No-Vary-Search), unless it changed its URL itself
            if (BrowsingContext.UrlEquals(prerendering.Document.UrlRecord, record.Url, excludeFragments: false) && !BrowsingContext.UrlEquals(url, record.Url, excludeFragments: false))
            {
                prerendering.Document.SetUrl(url);
            }

            ActivatePrerender(prerendering, historyHandling, parameters);
            return true;
        }

        return false;
    }

    /// <summary>
    /// https://wicg.github.io/nav-speculation/prerendering.html#activate-a-prerendering-traversable: the prerendered document
    /// becomes the active document of this navigable, in the browsing context of the prerendering traversable (a browsing
    /// context group switch: the WindowProxy objects of the previous documents keep their last window, and the navigable has no
    /// opener nor name). The previous document is unloaded as by a navigation, and an entry is added to the session history.
    /// Then the activation is finalized (https://wicg.github.io/nav-speculation/prerendering.html#finalize-activation): the
    /// documents get the visibility state of this navigable (<c>visibilitychange</c> when it is visible), run their
    /// post-prerendering activation steps and fire <c>prerenderingchange</c>.
    /// </summary>
    /// <remarks>
    /// The text directives (https://wicg.github.io/scroll-to-text-fragment/) are not integrated with the prerendering by the
    /// specifications. The activating navigation is the one the user is involved in: its fragment directive, when it has one,
    /// is the directive state of the new entry (else the entry keeps the one of the prerendered entry), and its text
    /// directives are searched in the prerendered document as for a navigation that creates the document, with the user
    /// involvement and the text directive user activation of the activating navigation.
    /// </remarks>
    private void ActivatePrerender(BrowsingContext prerendering, NavigationHistoryBehavior historyHandling, DocumentParameters parameters)
    {
        var document = prerendering.Document;
        var window = document.RelevantWindow!;
        var prerenderedEntry = prerendering.ActiveEntry;
        _ongoingNavigation = 0;

        // The prerendering traversable is discarded without unloading its document
        prerendering.CommitPrerenderingAcceptCHCache();
        prerendering.IsPrerendering = false;
        prerendering.PrerenderRecord = null;
        prerendering.PrerenderInitiator = null;
        prerendering.IsDestroyed = true;
        GroupOwner._auxiliaryBrowsingContexts?.Remove(prerendering);

        // The browsing context of the document that activates the prerendered document is discarded: it is active again if
        // the document is restored from the back/forward cache
        if (Document is { } previousDocument)
        {
            previousDocument.ReplacedBrowsingContext = new ReplacedBrowsingContext(ActiveBrowsingContext, Group);
            if (HasOpenerRelationship())
            {
                previousDocument.MakeUnsalvageable("non-trivial-browsing-context-group");
            }
        }

        ActiveBrowsingContext.Discard(Document?.RelevantWindow);
        ActiveBrowsingContext = prerendering.ActiveBrowsingContext;
        ActiveBrowsingContext.Retarget(this);
        _group = prerendering._group;
        _sessionStorageShed = prerendering._sessionStorageShed ?? _sessionStorageShed;
        Opener = null;
        Name = "";
        foreach (var topLevel in GetGroupTopLevelBrowsingContexts())
        {
            if (topLevel.Opener is { } opener && ReferenceEquals(opener.Top, this))
            {
                topLevel.Opener = null;
            }
        }

        if (document.ChildNavigables is { } children)
        {
            foreach (var child in children)
            {
                child.Parent = this;
            }
        }

        Activate(document, window, historyHandling, srcdoc: null, parameters: parameters, prerenderActivation: true);

        // The state of the entry of the prerendered document, and the histories of its child navigables
        var entry = ActiveEntry;
        entry.NavigationApiState = prerenderedEntry.NavigationApiState;
        entry.ScrollRestoration = prerenderedEntry.ScrollRestoration;
        if (parameters.FragmentDirective is null)
        {
            entry.DirectiveState = prerenderedEntry.DirectiveState;
        }

        ApplyTextDirectivesOfPrerenderActivation(document, entry, parameters);
        foreach (var nestedHistory in prerenderedEntry.DocumentState.NestedHistories)
        {
            foreach (var nestedEntry in nestedHistory.Entries)
            {
                nestedEntry.Step = entry.Step;
            }

            entry.DocumentState.NestedHistories.Add(nestedHistory);
        }

        var activationStart = EventLoop.TimeProvider.GetTimestamp();
        var documents = new List<Document> { document };
        documents.AddRange(GetDescendantNavigables().Select(static navigable => navigable.Document));
        foreach (var activated in documents)
        {
            if (activated.LoadTimingInfo is { } timing)
            {
                timing.ActivationStart = activationStart;
            }

            activated.UpdateVisibilityState(IsVisible);
        }

        foreach (var activated in documents)
        {
            activated.FinalizePrerenderingActivation();
        }

        // The documents that wait to be revealed have rendering opportunities now
        EventLoop.RequestRenderingUpdate();
    }

    /// <summary>
    /// The text directives of the navigation that activates a prerendered document: the document gets the text directive user
    /// activation of the navigation, and the text directives of its fragment directive are its pending text directives. They
    /// are searched now when the document is already parsed, else when its parsing ends.
    /// </summary>
    private static void ApplyTextDirectivesOfPrerenderActivation(Document document, SessionHistoryEntry entry, DocumentParameters parameters)
    {
        var userInvolvement = parameters.UserInvolvement;
        document.TextDirectiveUserActivation = userInvolvement is UserNavigationInvolvement.Activation or UserNavigationInvolvement.BrowserUI || parameters.TextDirectiveUserActivation;
        if (parameters.FragmentDirective is null)
            return;

        document.PendingTextDirectives = TextDirectives.Parse(entry.DirectiveState.Value);
        var allowTextDirectiveScroll = document.CheckIfTextDirectiveCanBeScrolled(parameters.InitiatorOrigin, userInvolvement, isSameDocumentNavigation: false);
        if (document.ActiveParser is not null)
        {
            document.AllowTextDirectiveScroll = allowTextDirectiveScroll;
            return;
        }

        if (!document.ForceLoadAtTop)
        {
            ScrollToTheFragment(document, allowTextDirectiveScroll);
        }

        document.PendingTextDirectives = null;
    }
}
