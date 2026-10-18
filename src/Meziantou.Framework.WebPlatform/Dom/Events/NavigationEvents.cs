namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationinterceptoptions</summary>
[IdlInterface("NavigationInterceptHandler", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationintercepthandler")]
public delegate object? NavigationInterceptHandler();

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationprecommithandler</summary>
[IdlInterface("NavigationPrecommitHandler", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationprecommithandler")]
public delegate object? NavigationPrecommitHandler(NavigationPrecommitController controller);

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationfocusreset</summary>
[IdlInterface("NavigationFocusReset", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationfocusreset")]
public enum NavigationFocusReset
{
    /// <summary>Resets the focus after the navigation transition finishes (the default behavior).</summary>
    [IdlMember("after-transition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationfocusreset-after-transition")]
    AfterTransition,

    /// <summary>Leaves the focus unchanged: the page manages it.</summary>
    [IdlMember("manual", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationfocusreset-manual")]
    Manual,
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationscrollbehavior</summary>
[IdlInterface("NavigationScrollBehavior", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationscrollbehavior")]
public enum NavigationScrollBehavior
{
    /// <summary>
    /// Restores or resets the scroll position after the navigation transition finishes (the default behavior).
    /// </summary>
    [IdlMember("after-transition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationscrollbehavior-after-transition")]
    AfterTransition,

    /// <summary>Leaves the scroll position unchanged: the page manages it.</summary>
    [IdlMember("manual", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationscrollbehavior-manual")]
    Manual,
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationinterceptoptions</summary>
[IdlInterface("NavigationInterceptOptions", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationinterceptoptions")]
public sealed class NavigationInterceptOptions
{
    /// <summary>Gets the callback run before the navigation commits; it can redirect or delay the commit.</summary>
    [IdlMember("precommitHandler", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationinterceptoptions-precommithandler")]
    public NavigationPrecommitHandler? PrecommitHandler { get; init; }

    /// <summary>
    /// Gets the callback run after the navigation commits; the promise it returns settles the transition.
    /// </summary>
    [IdlMember("handler", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationinterceptoptions-handler")]
    public NavigationInterceptHandler? Handler { get; init; }

    /// <summary>Gets whether the focus is reset after the transition or left to the page.</summary>
    [IdlMember("focusReset", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationinterceptoptions-focusreset")]
    public NavigationFocusReset? FocusReset { get; init; }

    /// <summary>Gets whether the scroll position is restored after the transition or left to the page.</summary>
    [IdlMember("scroll", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationinterceptoptions-scroll")]
    public NavigationScrollBehavior? Scroll { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigateeventinit</summary>
[IdlInterface("NavigateEventInit", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigateeventinit")]
public sealed class NavigateEventInit : EventInit
{
    /// <summary>
    /// Gets the type of the navigation: <c>push</c>, <c>replace</c>, <c>reload</c> or <c>traverse</c>.
    /// </summary>
    [IdlMember("navigationType", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-navigationtype")]
    public NavigationType NavigationType { get; init; }

    /// <summary>Gets the destination of the navigation.</summary>
    [IdlMember("destination", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-destination")]
    public required NavigationDestination Destination { get; init; }

    /// <summary>
    /// Gets a value indicating whether the navigation can be intercepted to become a same-document navigation.
    /// </summary>
    [IdlMember("canIntercept", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-canintercept")]
    public bool CanIntercept { get; init; }

    /// <summary>Gets a value indicating whether the navigation is initiated by the user.</summary>
    [IdlMember("userInitiated", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-userinitiated")]
    public bool UserInitiated { get; init; }

    /// <summary>Gets a value indicating whether the navigation only changes the fragment of the URL.</summary>
    [IdlMember("hashChange", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-hashchange")]
    public bool HashChange { get; init; }

    /// <summary>Gets the signal that is aborted when the navigation is canceled.</summary>
    [IdlMember("signal", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-signal")]
    public required AbortSignal Signal { get; init; }

    /// <summary>Gets the data of the form submission that started the navigation, if any.</summary>
    [IdlMember("formData", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-formdata")]
    public FormData? FormData { get; init; }

    /// <summary>Gets the <c>download</c> attribute value of the link that started the navigation, or null.</summary>
    [IdlMember("downloadRequest", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-downloadrequest")]
    public string? DownloadRequest { get; init; }

    /// <summary>A value of the script engine, or null for undefined.</summary>
    [IdlMember("info", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-info")]
    public object? Info { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user agent already performed a visual transition for the navigation.
    /// </summary>
    [IdlMember("hasUAVisualTransition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-hasuavisualtransition")]
    public bool HasUAVisualTransition { get; init; }

    /// <summary>Gets the element that started the navigation, or null.</summary>
    [IdlMember("sourceElement", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateeventinit-sourceelement")]
    public Element? SourceElement { get; init; }
}

/// <summary>The interception state of a <see cref="NavigateEvent"/> (https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-navigateevent-interception-state).</summary>
internal enum NavigateEventInterceptionState
{
    None,
    Intercepted,
    Committed,
    Scrolled,
    Finished,
}

/// <summary>
/// https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-navigateevent-interface: fired at the navigation API
/// object of a window when its navigable navigates. Its listeners can cancel the navigation, or intercept it to convert it
/// into a same-document navigation.
/// </summary>
[IdlInterface("NavigateEvent", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigateevent")]
public sealed class NavigateEvent : Event
{
    /// <summary>Creates a <c>navigate</c> event of the given type with the given initialization dictionary.</summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-navigateevent")]
    public NavigateEvent(string type, NavigateEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        NavigationType = eventInitDict.NavigationType;
        Destination = eventInitDict.Destination;
        CanIntercept = eventInitDict.CanIntercept;
        UserInitiated = eventInitDict.UserInitiated;
        HashChange = eventInitDict.HashChange;
        Signal = eventInitDict.Signal;
        FormData = eventInitDict.FormData;
        DownloadRequest = eventInitDict.DownloadRequest;
        Info = eventInitDict.Info;
        HasUAVisualTransition = eventInitDict.HasUAVisualTransition;
        SourceElement = eventInitDict.SourceElement;
    }

    /// <summary>The navigation type; a precommit handler can change a "push" to a "replace" and conversely.</summary>
    [IdlMember("navigationType", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-navigationtype")]
    public NavigationType NavigationType { get; internal set; }

    /// <summary>Gets the destination of the navigation.</summary>
    [IdlMember("destination", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-destination")]
    public NavigationDestination Destination { get; }

    /// <summary>
    /// Gets a value indicating whether the navigation can be intercepted to become a same-document navigation.
    /// </summary>
    [IdlMember("canIntercept", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-canintercept")]
    public bool CanIntercept { get; }

    /// <summary>Gets a value indicating whether the navigation is initiated by the user.</summary>
    [IdlMember("userInitiated", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-userinitiated")]
    public bool UserInitiated { get; }

    /// <summary>Gets a value indicating whether the navigation only changes the fragment of the URL.</summary>
    [IdlMember("hashChange", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-hashchange")]
    public bool HashChange { get; }

    /// <summary>Gets the signal that is aborted when the navigation is canceled.</summary>
    [IdlMember("signal", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-signal")]
    public AbortSignal Signal { get; }

    /// <summary>Gets the data of the form submission that started the navigation, if any.</summary>
    [IdlMember("formData", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-formdata")]
    public FormData? FormData { get; }

    /// <summary>Gets the <c>download</c> attribute value of the link that started the navigation, or null.</summary>
    [IdlMember("downloadRequest", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-downloadrequest")]
    public string? DownloadRequest { get; }

    /// <summary>The info given to the navigation API method that started the navigation (a value of the script engine), or null for undefined.</summary>
    [IdlMember("info", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-info")]
    public object? Info { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the user agent already performed a visual transition for the navigation.
    /// </summary>
    [IdlMember("hasUAVisualTransition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-hasuavisualtransition")]
    public bool HasUAVisualTransition { get; }

    /// <summary>Gets the element that started the navigation, or null.</summary>
    [IdlMember("sourceElement", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-sourceelement")]
    public Element? SourceElement { get; }

    internal NavigateEventInterceptionState InterceptionState { get; set; }

    internal List<NavigationPrecommitHandler> PrecommitHandlers { get; } = [];

    internal List<NavigationInterceptHandler> Handlers { get; } = [];

    internal NavigationFocusReset? FocusResetBehavior { get; private set; }

    internal NavigationScrollBehavior? ScrollBehavior { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-navigateevent-abort-controller</summary>
    internal AbortController? AbortController { get; init; }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#concept-navigateevent-classic-history-api-state: the serialized state, or null (the serialization of null) for a navigation that is not started by the classic history API.</summary>
    internal object? ClassicHistoryApiState { get; init; }

    /// <summary>The navigation API object at which the event is fired (its relevant global object's navigation API).</summary>
    internal Navigation? Navigation { get; init; }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-intercept</summary>
    [IdlMember("intercept", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-intercept")]
    public void Intercept(NavigationInterceptOptions? options = null)
    {
        options ??= new NavigationInterceptOptions();
        PerformSharedChecks();
        if (!CanIntercept)
            throw new DomException("The navigation cannot be intercepted", "SecurityError");

        if (!DispatchFlag)
            throw new DomException("The navigation can only be intercepted while the navigate event is dispatched", "InvalidStateError");

        if (options.PrecommitHandler is { } precommitHandler)
        {
            if (!Cancelable)
                throw new DomException("A precommit handler requires a cancelable navigation", "InvalidStateError");

            PrecommitHandlers.Add(precommitHandler);
        }

        InterceptionState = NavigateEventInterceptionState.Intercepted;
        if (options.Handler is { } handler)
        {
            Handlers.Add(handler);
        }

        if (options.FocusReset is { } focusReset)
        {
            FocusResetBehavior = focusReset;
        }

        if (options.Scroll is { } scroll)
        {
            ScrollBehavior = scroll;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-scroll: processes the scroll behavior of the navigation now.</summary>
    [IdlMember("scroll", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigateevent-scroll")]
    public void Scroll()
    {
        PerformSharedChecks();
        if (InterceptionState != NavigateEventInterceptionState.Committed)
            throw new DomException("The navigation is not committed", "InvalidStateError");

        ProcessScrollBehavior();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigateevent-perform-shared-checks</summary>
    internal void PerformSharedChecks()
    {
        if (Navigation?.Window.Document.IsFullyActive == false)
            throw new DomException("The document is not fully active", "InvalidStateError");

        if (!IsTrusted)
            throw new DomException("The event is not trusted", "SecurityError");

        if (CanceledFlag)
            throw new DomException("The navigation was canceled", "InvalidStateError");
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigateevent-finish</summary>
    internal void Finish(bool didFulfill)
    {
        if (InterceptionState == NavigateEventInterceptionState.Intercepted)
        {
            InterceptionState = NavigateEventInterceptionState.Finished;
            return;
        }

        if (InterceptionState is NavigateEventInterceptionState.None or NavigateEventInterceptionState.Finished)
            return;

        PotentiallyResetTheFocus();
        if (didFulfill && InterceptionState != NavigateEventInterceptionState.Scrolled && ScrollBehavior != NavigationScrollBehavior.Manual)
        {
            ProcessScrollBehavior();
        }

        InterceptionState = NavigateEventInterceptionState.Finished;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#potentially-reset-the-focus</summary>
    private void PotentiallyResetTheFocus()
    {
        if (Navigation is not { } navigation)
            return;

        var focusChanged = navigation.FocusChangedDuringOngoingNavigation;
        navigation.FocusChangedDuringOngoingNavigation = false;
        if (focusChanged || FocusResetBehavior == NavigationFocusReset.Manual)
            return;

        var document = navigation.Window.Document;
        Node? focusTarget = FocusModel.GetAutofocusDelegate(document, FocusTrigger.Other) ?? document.Body ?? document.DocumentElement;
        if (focusTarget is not null)
        {
            FocusModel.RunFocusingSteps(focusTarget, document);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/nav-history-apis.html#process-scroll-behavior: a traversal or a reload restores the
    /// scroll position of the active entry, the other navigations scroll to the fragment (or to the beginning of the document).
    /// </summary>
    private void ProcessScrollBehavior()
    {
        InterceptionState = NavigateEventInterceptionState.Scrolled;
        if (Navigation?.Window is not { BrowsingContext: { } browsingContext } window)
            return;

        if (NavigationType is Dom.NavigationType.Traverse or Dom.NavigationType.Reload)
        {
            // The traversal may not be applied yet when the handlers settle: the destination entry becomes the active entry
            browsingContext.RestoreScrollPositionData(NavigationType == Dom.NavigationType.Traverse ? Destination.Entry?.SessionHistoryEntry ?? browsingContext.ActiveEntry : browsingContext.ActiveEntry);
        }
        else
        {
            BrowsingContext.ScrollToTheFragment(window.Document, scrollToBeginning: true);
        }
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationcurrententrychangeeventinit</summary>
[IdlInterface("NavigationCurrentEntryChangeEventInit", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationcurrententrychangeeventinit")]
public sealed class NavigationCurrentEntryChangeEventInit : EventInit
{
    /// <summary>
    /// Gets the type of the navigation that changed the current entry, or null when it is not a navigation.
    /// </summary>
    [IdlMember("navigationType", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationcurrententrychangeeventinit-navigationtype")]
    public NavigationType? NavigationType { get; init; }

    /// <summary>Gets the entry that was the current entry before the change.</summary>
    [IdlMember("from", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationcurrententrychangeeventinit-from")]
    public required NavigationHistoryEntry From { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationcurrententrychangeevent</summary>
[IdlInterface("NavigationCurrentEntryChangeEvent", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationcurrententrychangeevent")]
public sealed class NavigationCurrentEntryChangeEvent : Event
{
    /// <summary>
    /// Creates a <c>currententrychange</c> event of the given type with the given initialization dictionary.
    /// </summary>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationcurrententrychangeevent-navigationcurrententrychangeevent")]
    public NavigationCurrentEntryChangeEvent(string type, NavigationCurrentEntryChangeEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        NavigationType = eventInitDict.NavigationType;
        From = eventInitDict.From;
    }

    /// <summary>
    /// Gets the type of the navigation that changed the current entry, or null when it is not a navigation.
    /// </summary>
    [IdlMember("navigationType", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationcurrententrychangeevent-navigationtype")]
    public NavigationType? NavigationType { get; }

    /// <summary>Gets the entry that was the current entry before the change.</summary>
    [IdlMember("from", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationcurrententrychangeevent-from")]
    public NavigationHistoryEntry From { get; }
}
