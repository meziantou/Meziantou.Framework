namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://fullscreen.spec.whatwg.org/#enumdef-fullscreennavigationui</summary>
/// <seealso href="https://fullscreen.spec.whatwg.org/#enumdef-fullscreennavigationui"/>
[IdlInterface("FullscreenNavigationUI", "https://fullscreen.spec.whatwg.org/#enumdef-fullscreennavigationui")]
public enum FullscreenNavigationUI
{
    /// <summary>The application has no preference.</summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-fullscreennavigationui-auto"/>
    [IdlMember("auto", "https://fullscreen.spec.whatwg.org/#dom-fullscreennavigationui-auto")]
    Auto,

    /// <summary>The navigation controls of the user agent are preferred over the screen space.</summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-fullscreennavigationui-show"/>
    [IdlMember("show", "https://fullscreen.spec.whatwg.org/#dom-fullscreennavigationui-show")]
    Show,

    /// <summary>More screen space is preferred.</summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-fullscreennavigationui-hide"/>
    [IdlMember("hide", "https://fullscreen.spec.whatwg.org/#dom-fullscreennavigationui-hide")]
    Hide,
}

/// <summary>https://fullscreen.spec.whatwg.org/#enumdef-fullscreenkeyboardlock</summary>
/// <seealso href="https://fullscreen.spec.whatwg.org/#enumdef-fullscreenkeyboardlock"/>
[IdlInterface("FullscreenKeyboardLock", "https://fullscreen.spec.whatwg.org/#enumdef-fullscreenkeyboardlock")]
public enum FullscreenKeyboardLock
{
    /// <summary>The keys that the user agent handles while fullscreen (e.g. Escape) are given to the page.</summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-fullscreenkeyboardlock-browser"/>
    [IdlMember("browser", "https://fullscreen.spec.whatwg.org/#dom-fullscreenkeyboardlock-browser")]
    Browser,

    /// <summary>No keyboard lock.</summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-fullscreenkeyboardlock-none"/>
    [IdlMember("none", "https://fullscreen.spec.whatwg.org/#dom-fullscreenkeyboardlock-none")]
    None,
}

/// <summary>The options of <see cref="Element.RequestFullscreen"/> (https://fullscreen.spec.whatwg.org/#dictdef-fullscreenoptions).</summary>
/// <seealso href="https://fullscreen.spec.whatwg.org/#dictdef-fullscreenoptions"/>
[IdlInterface("FullscreenOptions", "https://fullscreen.spec.whatwg.org/#dictdef-fullscreenoptions")]
public sealed class FullscreenOptions
{
    /// <summary>Gets whether the keys that exit fullscreen are given to the page instead (see <see cref="FullscreenKeyboardLock"/>).</summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-fullscreenoptions-keyboardlock"/>
    [IdlMember("keyboardLock", "https://fullscreen.spec.whatwg.org/#dom-fullscreenoptions-keyboardlock")]
    public FullscreenKeyboardLock KeyboardLock { get; init; } = FullscreenKeyboardLock.None;

    /// <summary>Gets whether the navigation controls of the user agent are preferred while fullscreen.</summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-fullscreenoptions-navigationui"/>
    [IdlMember("navigationUI", "https://fullscreen.spec.whatwg.org/#dom-fullscreenoptions-navigationui")]
    public FullscreenNavigationUI NavigationUI { get; init; } = FullscreenNavigationUI.Auto;
}

/// <summary>
/// The size of the viewport of a top-level browsing context while its document is displayed fullscreen, in CSS pixels
/// (see <see cref="BrowsingContextOptions.EnterFullscreen"/>).
/// </summary>
/// <param name="Width">The width of the viewport.</param>
/// <param name="Height">The height of the viewport.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct FullscreenViewportSize(double Width, double Height);

public partial class Element
{
    /// <summary>https://fullscreen.spec.whatwg.org/#fullscreen-flag</summary>
    internal bool HasFullscreenFlag => RareDataOrNull is ElementRareData { FullscreenFlag: true };

    /// <summary>https://fullscreen.spec.whatwg.org/#iframe-fullscreen-flag</summary>
    internal bool HasIframeFullscreenFlag => RareDataOrNull is ElementRareData { IframeFullscreenFlag: true };

    /// <summary>https://fullscreen.spec.whatwg.org/#keyboard-lock-flag</summary>
    internal bool HasKeyboardLockFlag => RareDataOrNull is ElementRareData { KeyboardLockFlag: true };

    /// <summary>
    /// Displays the element fullscreen (https://fullscreen.spec.whatwg.org/#dom-element-requestfullscreen): the element and
    /// the containers of the ancestor navigables are added to the top layer of their document, and <c>fullscreenchange</c> is
    /// fired at them by the next rendering update. The window must have transient activation. The task fails with a
    /// <see cref="TypeErrorException"/> when the element cannot be displayed fullscreen (<c>fullscreenerror</c> is fired).
    /// </summary>
    /// <param name="options">The options, or null for the default options.</param>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-element-requestfullscreen"/>
    [IdlMember("requestFullscreen", "https://fullscreen.spec.whatwg.org/#dom-element-requestfullscreen")]
    public Task RequestFullscreen(FullscreenOptions? options = null) => FullscreenModel.Request(this, options ?? new FullscreenOptions());
}

public partial class Document
{
    /// <summary>The number of elements of the document whose fullscreen flag is set (most documents have none).</summary>
    internal int FullscreenFlagCount { get; set; }

    /// <summary>https://fullscreen.spec.whatwg.org/#list-of-pending-fullscreen-events</summary>
    internal List<(string Type, Element Element)>? PendingFullscreenEvents { get; set; }

    /// <summary>
    /// The number of queued tasks that run the steps of exit fullscreen (https://fullscreen.spec.whatwg.org/#exit-fullscreen)
    /// for the document. The fullscreen steps wait for them: the event of a fullscreen element that was removed is pending
    /// before the task runs, and a rendering update that another feature scheduled before the task (e.g. the one that
    /// dispatches the event timing entries of the click) would fire it while the document is still fullscreen.
    /// </summary>
    internal int PendingFullscreenExitCount { get; set; }

    /// <summary>
    /// https://fullscreen.spec.whatwg.org/#fullscreen-element: the topmost element of the top layer of the document whose
    /// fullscreen flag is set, or null.
    /// </summary>
    internal Element? FullscreenElementInternal
    {
        get
        {
            if (FullscreenFlagCount == 0 || TopLayerElements is not { } topLayer)
                return null;

            for (var i = topLayer.Count - 1; i >= 0; i--)
            {
                if (topLayer[i].HasFullscreenFlag)
                    return topLayer[i];
            }

            return null;
        }
    }

    /// <summary>
    /// Gets whether the document is allowed to display elements fullscreen (https://fullscreen.spec.whatwg.org/#dom-document-fullscreenenabled):
    /// its permissions policy allows the <c>fullscreen</c> feature.
    /// </summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-document-fullscreenenabled"/>
    [IdlMember("fullscreenEnabled", "https://fullscreen.spec.whatwg.org/#dom-document-fullscreenenabled")]
    public bool FullscreenEnabled => PermissionsPolicyAlgorithms.IsAllowedToUse(this, PolicyControlledFeature.Fullscreen, report: false);

    /// <summary>Gets whether the document has a fullscreen element (https://fullscreen.spec.whatwg.org/#dom-document-fullscreen, historical: use <see cref="FullscreenElement"/>).</summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-document-fullscreen"/>
    [IdlMember("fullscreen", "https://fullscreen.spec.whatwg.org/#dom-document-fullscreen")]
    public bool Fullscreen => FullscreenElementInternal is not null;

    /// <summary>
    /// Gets the fullscreen element of the document (https://fullscreen.spec.whatwg.org/#dom-document-fullscreenelement): the
    /// host of the shadow tree of the element when it is in a shadow tree.
    /// </summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-document-fullscreenelement"/>
    [IdlMember("fullscreenElement", "https://fullscreen.spec.whatwg.org/#dom-document-fullscreenelement")]
    public Element? FullscreenElement => FullscreenModel.GetFullscreenElement(this, this);

    /// <summary>
    /// Stops displaying the fullscreen element of the document fullscreen (https://fullscreen.spec.whatwg.org/#dom-document-exitfullscreen);
    /// <c>fullscreenchange</c> is fired by the next rendering update. The task fails with a <see cref="TypeErrorException"/>
    /// when the document has no fullscreen element.
    /// </summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-document-exitfullscreen"/>
    [IdlMember("exitFullscreen", "https://fullscreen.spec.whatwg.org/#dom-document-exitfullscreen")]
    public Task ExitFullscreen() => FullscreenModel.Exit(this);

    /// <summary>https://fullscreen.spec.whatwg.org/#run-the-fullscreen-steps</summary>
    internal void RunFullscreenSteps()
    {
        if (PendingFullscreenExitCount > 0 || PendingFullscreenEvents is not { Count: > 0 } pendingEvents)
            return;

        PendingFullscreenEvents = null;
        foreach (var (type, element) in pendingEvents)
        {
            EventTarget target = element.IsConnected && ReferenceEquals(element.NodeDocument, this) ? element : this;
            target.FireEvent(new Event(type, new EventInit { Bubbles = true, Composed = true }));
        }
    }
}

public partial class ShadowRoot
{
    /// <summary>
    /// Gets the fullscreen element of the shadow tree (https://fullscreen.spec.whatwg.org/#dom-document-fullscreenelement):
    /// the fullscreen element of the document retargeted against the shadow root, when it is in the shadow tree.
    /// </summary>
    /// <seealso href="https://fullscreen.spec.whatwg.org/#dom-document-fullscreenelement"/>
    [IdlMember("fullscreenElement", "https://fullscreen.spec.whatwg.org/#dom-document-fullscreenelement")]
    public Element? FullscreenElement => Host.IsConnected ? FullscreenModel.GetFullscreenElement(NodeDocument, this) : null;
}

/// <summary>
/// The Fullscreen API (https://fullscreen.spec.whatwg.org/). Nothing is displayed: the fullscreen elements are the elements
/// of the top layer whose fullscreen flag is set, styled by the rules of the user agent style sheet (<c>:fullscreen</c>), and
/// the host is told when the top-level browsing context enters and exits fullscreen
/// (<see cref="BrowsingContextOptions.EnterFullscreen"/>, <see cref="BrowsingContextOptions.ExitFullscreen"/>). The steps
/// that the specification runs in parallel are tasks of the event loop.
/// </summary>
internal static class FullscreenModel
{
    /// <summary>https://fullscreen.spec.whatwg.org/#dom-document-fullscreenelement</summary>
    public static Element? GetFullscreenElement(Document document, Node root)
    {
        if (document.FullscreenElementInternal is not { } element)
            return null;

        var candidate = EventDispatcher.Retarget(element, root) as Element;
        return candidate is not null && ReferenceEquals(candidate.GetRoot(), root) ? candidate : null;
    }

    /// <summary>https://fullscreen.spec.whatwg.org/#fullscreen-pseudo-class</summary>
    public static bool MatchesFullscreen(Element element)
    {
        var document = element.NodeDocument;
        if (document.FullscreenFlagCount == 0)
            return false;

        if (element.HasFullscreenFlag)
            return true;

        return element.AttachedShadowRoot is not null && document.FullscreenElementInternal is { } fullscreenElement
            && !ReferenceEquals(fullscreenElement, element) && ReferenceEquals(EventDispatcher.Retarget(fullscreenElement, element), element);
    }

    /// <summary>https://fullscreen.spec.whatwg.org/#fullscreen-element-ready-check</summary>
    private static bool IsReady(Element element, bool report)
        => element.IsConnected
            && PermissionsPolicyAlgorithms.IsAllowedToUse(element.NodeDocument, PolicyControlledFeature.Fullscreen, report)
            && (!element.IsHtmlNamespace || element is not HtmlElement { IsPopoverShowing: true });

    /// <summary>https://fullscreen.spec.whatwg.org/#dom-element-requestfullscreen</summary>
    public static Task Request(Element element, FullscreenOptions options)
    {
        var pendingDocument = element.NodeDocument;
        var promise = new TaskCompletionSource(TaskCreationOptions.None);
        if (!pendingDocument.IsFullyActive || pendingDocument.RelevantWindow is not { } window || pendingDocument.EventLoop is not { } eventLoop)
        {
            promise.SetException(new TypeErrorException("The document is not fully active"));
            return promise.Task;
        }

        string? error = null;
        if (!(element.IsHtmlNamespace || element is { NamespaceUri: Namespaces.Svg, LocalName: "svg" } or { NamespaceUri: Namespaces.MathML, LocalName: "math" }))
        {
            error = "The element cannot be displayed fullscreen";
        }
        else if (element is HtmlDialogElement)
        {
            error = "A dialog element cannot be displayed fullscreen";
        }
        else if (!IsReady(element, report: true))
        {
            error = "The element is not connected, is a showing popover, or its document is not allowed to use fullscreen";
        }
        else if (!window.HasTransientActivation && Permissions.GetPermissionState(window, "fullscreen") != PermissionState.Granted)
        {
            // https://github.com/explainers-by-googlers/html-fullscreen-without-a-gesture: the "fullscreen" permission (with
            // allowWithoutGesture) allows the request without a user activation
            error = "requestFullscreen() requires a user activation";
        }

        if (error is null)
        {
            window.ConsumeUserActivation();
        }

        eventLoop.QueueTask(() =>
        {
            // "resize pendingDoc's node navigable's top-level traversable's active document's viewport's dimensions"
            if (error is null && pendingDocument.BrowsingContext is { } navigable)
            {
                navigable.Top.EnterFullscreen(options);
            }

            if (error is null && (!ReferenceEquals(element.NodeDocument, pendingDocument) || !IsReady(element, report: false)))
            {
                error = "The element was removed from its document, or its document is no longer allowed to use fullscreen";
            }

            if (error is not null)
            {
                // The viewport was resized for an element that is not displayed fullscreen after all
                if (pendingDocument.BrowsingContext?.Top is { Document.FullscreenElementInternal: null } topLevel)
                {
                    topLevel.ExitFullscreen();
                }

                AppendPendingEvent(pendingDocument, "fullscreenerror", element);
                promise.SetException(new TypeErrorException(error));
                return;
            }

            // The element and the containers of the ancestor navigables
            var fullscreenElements = new List<Element> { element };
            while (fullscreenElements[^1].NodeDocument.BrowsingContext?.Container is { } container)
            {
                fullscreenElements.Add(container);
            }

            foreach (var fullscreenElement in fullscreenElements)
            {
                var document = fullscreenElement.NodeDocument;
                if (ReferenceEquals(fullscreenElement, document.FullscreenElementInternal))
                    continue;

                if (ReferenceEquals(fullscreenElement, element) && element is HtmlIFrameElement)
                {
                    element.ElementRareData.IframeFullscreenFlag = true;
                }

                FullscreenElement(fullscreenElement);
                AppendPendingEvent(document, "fullscreenchange", fullscreenElement);
            }

            element.ElementRareData.KeyboardLockFlag = options.KeyboardLock == FullscreenKeyboardLock.Browser;
            promise.SetResult();
        });

        return promise.Task;
    }

    /// <summary>https://fullscreen.spec.whatwg.org/#fullscreen-an-element</summary>
    private static void FullscreenElement(Element element)
    {
        var document = element.NodeDocument;
        var hideUntil = HtmlElement.GetTopmostPopoverAncestor(element, source: null, isPopover: false);
        document.HidePopoversUntil(hideUntil, focusPreviousElement: false, fireEvents: true);
        if (!element.HasFullscreenFlag)
        {
            element.ElementRareData.FullscreenFlag = true;
            document.FullscreenFlagCount++;
        }

        document.RemoveFromTopLayer(element);
        document.AddToTopLayer(element);
    }

    /// <summary>https://fullscreen.spec.whatwg.org/#unfullscreen-an-element</summary>
    /// <param name="document">The document whose top layer has the element (the node document of the element, unless the element was moved to another document).</param>
    /// <param name="element">The element.</param>
    private static void UnfullscreenElement(Document document, Element element)
    {
        if (element.HasFullscreenFlag)
        {
            var data = element.ElementRareData;
            data.FullscreenFlag = false;
            data.IframeFullscreenFlag = false;
            data.KeyboardLockFlag = false;
            document.FullscreenFlagCount--;

            // The :fullscreen pseudo-class of a disconnected element (the top layer does not change)
            document.IncrementElementStateVersion();
        }

        document.RemoveFromTopLayer(element);
    }

    /// <summary>https://fullscreen.spec.whatwg.org/#unfullscreen-a-document</summary>
    private static void UnfullscreenDocument(Document document)
    {
        foreach (var element in GetFullscreenElements(document))
        {
            UnfullscreenElement(document, element);
        }
    }

    /// <summary>The elements of the top layer of the document whose fullscreen flag is set, in the order of the top layer.</summary>
    private static List<Element> GetFullscreenElements(Document document)
    {
        var result = new List<Element>();
        if (document.FullscreenFlagCount > 0 && document.TopLayerElements is { } topLayer)
        {
            foreach (var element in topLayer)
            {
                if (element.HasFullscreenFlag)
                {
                    result.Add(element);
                }
            }
        }

        return result;
    }

    /// <summary>https://fullscreen.spec.whatwg.org/#simple-fullscreen-document</summary>
    private static bool IsSimpleFullscreenDocument(Document document) => GetFullscreenElements(document).Count == 1;

    /// <summary>https://fullscreen.spec.whatwg.org/#fully-exit-fullscreen</summary>
    public static void FullyExit(Document document)
    {
        if (document.FullscreenElementInternal is not { } fullscreenElement)
            return;

        foreach (var element in GetFullscreenElements(document))
        {
            if (!ReferenceEquals(element, fullscreenElement))
            {
                UnfullscreenElement(document, element);
            }
        }

        // The promise of the algorithm is not exposed
        _ = Exit(document, observed: false);
    }

    /// <summary>https://fullscreen.spec.whatwg.org/#collect-documents-to-unfullscreen</summary>
    private static List<Document> CollectDocumentsToUnfullscreen(Document document)
    {
        var documents = new List<Document> { document };
        while (true)
        {
            var last = documents[^1];
            if (!IsSimpleFullscreenDocument(last) || last.BrowsingContext?.Container is not { } container || container.HasIframeFullscreenFlag)
                break;

            documents.Add(container.NodeDocument);
        }

        return documents;
    }

    /// <summary>https://fullscreen.spec.whatwg.org/#exit-fullscreen</summary>
    /// <param name="document">The document.</param>
    /// <param name="observed">Whether the task is returned to a caller, which observes its failure.</param>
    public static Task Exit(Document document, bool observed = true)
    {
        var promise = new TaskCompletionSource(TaskCreationOptions.None);
        if (!document.IsFullyActive || document.FullscreenElementInternal is null || document.EventLoop is not { } eventLoop)
        {
            if (observed)
            {
                promise.SetException(new TypeErrorException("The document is not fully active or has no fullscreen element"));
            }

            return promise.Task;
        }

        var resize = false;
        var documents = CollectDocumentsToUnfullscreen(document);
        var topLevelDocument = document.BrowsingContext!.Top.Document;
        if (documents.Contains(topLevelDocument) && IsSimpleFullscreenDocument(topLevelDocument))
        {
            document = topLevelDocument;
            resize = true;
        }

        // The steps that the specification runs in parallel are queued first: the rendering opportunity of the pending
        // fullscreen events (queued by the steps below) follows them, so the events are fired once the documents exited.
        // The fullscreen steps of a rendering update that runs before the task wait for it.
        var topLevel = document.BrowsingContext!.Top;
        document.PendingFullscreenExitCount++;
        eventLoop.QueueTask(() =>
        {
            document.PendingFullscreenExitCount--;
            if (document.PendingFullscreenEvents is { Count: > 0 })
            {
                eventLoop.RequestFullscreenSteps(document);
            }

            // "If resize is true, resize doc's viewport to its "normal" dimensions"
            if (resize)
            {
                topLevel.ExitFullscreen();
            }

            if (document.FullscreenElementInternal is null)
            {
                promise.SetResult();
                return;
            }

            var exitDocuments = CollectDocumentsToUnfullscreen(document);
            var descendantDocuments = new List<Document>();
            if (document.BrowsingContext is { } navigable && ReferenceEquals(navigable.Document, document))
            {
                foreach (var descendant in navigable.GetDescendantNavigables())
                {
                    if (descendant.Document.FullscreenElementInternal is not null)
                    {
                        descendantDocuments.Add(descendant.Document);
                    }
                }
            }

            foreach (var exitDocument in exitDocuments)
            {
                if (exitDocument.FullscreenElementInternal is not { } exitElement)
                    continue;

                AppendPendingEvent(exitDocument, "fullscreenchange", exitElement);
                if (resize)
                {
                    UnfullscreenDocument(exitDocument);
                }
                else
                {
                    UnfullscreenElement(exitDocument, exitElement);
                }
            }

            foreach (var descendantDocument in descendantDocuments)
            {
                if (descendantDocument.FullscreenElementInternal is not { } descendantElement)
                    continue;

                AppendPendingEvent(descendantDocument, "fullscreenchange", descendantElement);
                UnfullscreenDocument(descendantDocument);
            }

            promise.SetResult();
        });

        if (document.FullscreenElementInternal is { IsConnected: false } disconnected)
        {
            AppendPendingEvent(document, "fullscreenchange", disconnected);
            UnfullscreenElement(document, disconnected);
        }

        return promise.Task;
    }

    /// <summary>
    /// The removing steps (https://fullscreen.spec.whatwg.org/#removing-steps): the fullscreen element of the document exits
    /// fullscreen when it is removed, and the other removed elements lose their fullscreen flag.
    /// </summary>
    public static void OnRemoved(Document document, Node removedNode)
    {
        List<Element>? elements = null;
        foreach (var node in removedNode.ShadowIncludingInclusiveDescendants())
        {
            if (node is Element { HasFullscreenFlag: true } element)
            {
                (elements ??= []).Add(element);
            }
        }

        if (elements is null)
            return;

        foreach (var element in elements)
        {
            if (ReferenceEquals(element, document.FullscreenElementInternal))
            {
                _ = Exit(document, observed: false);
            }

            // "Otherwise, unfullscreen node. If document's top layer contains node, remove from the top layer immediately
            // given node". The fullscreen element of a nested document keeps its flag when the document exits fullscreen (the
            // top-level document exits first), and so does the one of a document that is not fully active: the flag is
            // unset too, as the element is no longer in the top layer of the document (it may be inserted in another one).
            UnfullscreenElement(document, element);
        }
    }

    /// <summary>How long the Escape key is held to exit fullscreen while the keyboard lock is active (as in Chromium).</summary>
    private static readonly TimeSpan EscapeHoldDuration = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// https://fullscreen.spec.whatwg.org/#keyboard-lock-active: whether the keyboard lock flag of the innermost fullscreen
    /// element of the documents of a top-level browsing context is set.
    /// </summary>
    public static bool IsKeyboardLockActive(Document topLevelDocument)
    {
        if (topLevelDocument.FullscreenElementInternal is not { } element)
            return false;

        while (element.NodeDocument.BrowsingContext is not null && NavigableContainers.GetContentNavigable(element)?.Document.FullscreenElementInternal is { } inner)
        {
            element = inner;
        }

        return element.HasKeyboardLockFlag;
    }

    /// <summary>
    /// The Escape key while the keyboard lock is active: "user agents that use the Esc key to exit fullscreen use keyboard
    /// lock to prevent immediate exit on key press, and instead require a long press to exit fullscreen". The key is held
    /// from its first keydown event to its keyup event, on the clock of the event loop.
    /// </summary>
    public static void OnLockedEscapeKey(Document topLevelDocument, bool isDown)
    {
        if (topLevelDocument.BrowsingContext is not { } topLevel)
            return;

        if (!isDown)
        {
            topLevel.EscapeKeyHeldSince = null;
            return;
        }

        var timeProvider = topLevel.EventLoop.TimeProvider;
        if (topLevel.EscapeKeyHeldSince is not { } since)
        {
            topLevel.EscapeKeyHeldSince = timeProvider.GetTimestamp();
        }
        else if (timeProvider.GetElapsedTime(since) >= EscapeHoldDuration)
        {
            topLevel.EscapeKeyHeldSince = null;
            FullyExit(topLevelDocument);
            PointerLockModel.ExitByUserAgent(topLevel);
        }
    }

    private static void AppendPendingEvent(Document document, string type, Element element)
    {
        (document.PendingFullscreenEvents ??= []).Add((type, element));
        document.EventLoop?.RequestFullscreenSteps(document);
    }
}
