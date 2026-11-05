namespace Meziantou.Framework.WebPlatform.Dom;

// The Page Lifecycle API (https://wicg.github.io/page-lifecycle/): a document is frozen when the user agent stops running
// its tasks (e.g. a background tab, a document in the back/forward cache, or a hidden iframe whose permissions policy
// disables "execution-while-not-rendered"), and resumed later. The freeze and resume events tell the page.
public partial class Document
{
    private volatile bool _isFrozen;
    private List<HtmlMediaElement>? _mediaElementsToResume;

    /// <summary>
    /// Whether the document is frozen (https://wicg.github.io/page-lifecycle/#concept-document-frozenness): its tasks are
    /// not runnable (they are deferred until it is resumed) and its timers are suspended. It is read by the threads of the
    /// workers (a dedicated worker whose owner document is frozen is blocked).
    /// </summary>
    internal bool IsFrozen
    {
        get => _isFrozen;
        private set => _isFrozen = value;
    }

    /// <summary>Whether the tasks and the timers of the document are suspended: it is frozen, or in the back/forward cache.</summary>
    internal bool IsSuspended => IsFrozen || IsInBackForwardCache;

    /// <summary>
    /// Gets whether the document was loaded again after the user agent discarded its browsing context to save resources
    /// (https://wicg.github.io/page-lifecycle/#dom-document-wasdiscarded, see <see cref="BrowsingContext.Discard"/>).
    /// </summary>
    /// <seealso href="https://wicg.github.io/page-lifecycle/#dom-document-wasdiscarded"/>
    [IdlMember("wasDiscarded", "https://wicg.github.io/page-lifecycle/#dom-document-wasdiscarded")]
    public bool WasDiscarded { get; internal set; }

    /// <summary>Gets or sets the event handler of the <c>freeze</c> event, fired when the document is frozen.</summary>
    /// <seealso href="https://wicg.github.io/page-lifecycle/#dom-document-onfreeze"/>
    [IdlMember("onfreeze", "https://wicg.github.io/page-lifecycle/#dom-document-onfreeze")]
    public Action<Event>? OnFreeze
    {
        get => GetEventHandler("freeze");
        set => SetEventHandler("freeze", value);
    }

    /// <summary>Gets or sets the event handler of the <c>resume</c> event, fired when the document is resumed.</summary>
    /// <seealso href="https://wicg.github.io/page-lifecycle/#dom-document-onresume"/>
    [IdlMember("onresume", "https://wicg.github.io/page-lifecycle/#dom-document-onresume")]
    public Action<Event>? OnResume
    {
        get => GetEventHandler("resume");
        set => SetEventHandler("resume", value);
    }

    /// <summary>
    /// https://wicg.github.io/page-lifecycle/#change-the-frozenness-of-a-document: runs the freeze steps or the resume steps
    /// when the frozenness changes.
    /// </summary>
    /// <param name="frozen">The new frozenness.</param>
    /// <param name="autoResumeMedia">Whether the media elements paused by the freeze steps play again when the document is resumed.</param>
    internal void ChangeFrozenness(bool frozen, bool autoResumeMedia = false)
    {
        if (IsFrozen == frozen)
            return;

        if (frozen)
        {
            Freeze(autoResumeMedia);
        }
        else
        {
            Resume();
        }
    }

    /// <summary>https://wicg.github.io/page-lifecycle/#freeze-steps</summary>
    private void Freeze(bool autoResumeMedia)
    {
        IsFrozen = true;
        StartSuspension();
        if (ServiceWorkerClient is { } client)
        {
            client.IsFrozen = true;
        }

        FireEvent(new Event("freeze"));

        // The state is set before the event is fired: the media elements are paused after the event
        foreach (var node in ShadowIncludingInclusiveDescendants())
        {
            if (node is HtmlMediaElement { Paused: false } media)
            {
                if (autoResumeMedia)
                {
                    (_mediaElementsToResume ??= []).Add(media);
                }

                media.Pause();
            }
        }
    }

    /// <summary>https://wicg.github.io/page-lifecycle/#resume-steps</summary>
    private void Resume()
    {
        if (_mediaElementsToResume is { } mediaElements)
        {
            _mediaElementsToResume = null;
            foreach (var media in mediaElements)
            {
                if (ReferenceEquals(media.NodeDocument, this))
                {
                    _ = media.Play();
                }
            }
        }

        FireEvent(new Event("resume"));
        IsFrozen = false;
        if (ServiceWorkerClient is { } client)
        {
            client.IsFrozen = false;
        }

        EndSuspensionIfResumed();
    }

    /// <summary>
    /// The intersection observer of the containers whose document can be frozen when it is not rendered or out of the
    /// viewport (<see cref="ObserveContainerForFrozenness"/>): a change of the rendering of a container updates the
    /// frozenness of its document.
    /// </summary>
    private IntersectionObserver? _frozennessIntersectionObserver;

    /// <summary>
    /// The iframe load event steps ran: when the permissions policy of the document of the iframe disables
    /// <c>execution-while-not-rendered</c> or <c>execution-while-out-of-viewport</c>, the rendering of the iframe is observed,
    /// to freeze its document (https://wicg.github.io/page-lifecycle/#update-document-frozenness-steps).
    /// </summary>
    internal static void ObserveContainerForFrozenness(HtmlIFrameElement container, Document childDocument)
    {
        var document = container.NodeDocument;
        var canBeFrozen = !PermissionsPolicyAlgorithms.IsAllowedToUse(childDocument, PolicyControlledFeature.ExecutionWhileNotRendered, report: false)
            || !PermissionsPolicyAlgorithms.IsAllowedToUse(childDocument, PolicyControlledFeature.ExecutionWhileOutOfViewport, report: false);
        if (!canBeFrozen)
        {
            document._frozennessIntersectionObserver?.Unobserve(container);
            childDocument.ChangeFrozenness(frozen: false);
            return;
        }

        document._frozennessIntersectionObserver ??= new IntersectionObserver(document, OnContainerIntersection, Css.Layout.IntersectionMargin.Zero);
        document._frozennessIntersectionObserver.Observe(container);
    }

    /// <summary>The rendering of containers changed: the frozenness of their documents is updated.</summary>
    private static void OnContainerIntersection(IReadOnlyList<IntersectionObserverEntry> entries, IntersectionObserver observer)
    {
        foreach (var entry in entries)
        {
            if (entry.Target is HtmlIFrameElement { ContentNavigable.Document: { } childDocument } container && ReferenceEquals(childDocument.BrowsingContext?.Container, container))
            {
                childDocument.UpdateDocumentFrozenness(entry.IsIntersecting);
            }
        }
    }

    /// <summary>
    /// https://wicg.github.io/page-lifecycle/#update-document-frozenness-steps: the frozenness of a document of a child
    /// navigable is updated from the permissions policy of the document and the rendering of its container
    /// (<c>execution-while-not-rendered</c>, <c>execution-while-out-of-viewport</c>). The documents of a frozen container
    /// document are not updated.
    /// </summary>
    /// <param name="intersectsViewport">Whether the container intersects the viewport of its document.</param>
    private void UpdateDocumentFrozenness(bool intersectsViewport)
    {
        if (BrowsingContext is not { Container: { } container } || ReadyState != DocumentReadyState.Complete || !IsFullyActive || container.NodeDocument.IsFrozen)
            return;

        var frozen = false;
        var autoResumeMedia = false;
        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(this, PolicyControlledFeature.ExecutionWhileNotRendered, report: false) && !Css.Cascade.ComputedStyleDeclarations.IsBeingRendered(container))
        {
            frozen = true;
        }
        else if (!intersectsViewport && !PermissionsPolicyAlgorithms.IsAllowedToUse(this, PolicyControlledFeature.ExecutionWhileOutOfViewport, report: false))
        {
            frozen = true;
            autoResumeMedia = true;
        }

        ChangeFrozenness(frozen, autoResumeMedia);
    }

    private void StartSuspension()
    {
        if (_suspensionStart == 0 && RelevantWindow is { } window)
        {
            _suspensionStart = Math.Max(1, window.EventLoop.TimeProvider.GetTimestamp());
        }
    }

    /// <summary>The document is neither frozen nor in the back/forward cache: the suspension of its timers ends, and its deferred tasks are queued again, in order.</summary>
    private void EndSuspensionIfResumed()
    {
        if (IsSuspended)
            return;

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
}
