using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationtype</summary>
[IdlInterface("NavigationType", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationtype")]
public enum NavigationType
{
    /// <summary>A navigation that adds a new entry to the session history.</summary>
    [IdlMember("push", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationtype-push")]
    Push,

    /// <summary>A navigation that replaces the current entry of the session history.</summary>
    [IdlMember("replace", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationtype-replace")]
    Replace,

    /// <summary>A reload of the current entry.</summary>
    [IdlMember("reload", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationtype-reload")]
    Reload,

    /// <summary>A traversal of the session history to an existing entry.</summary>
    [IdlMember("traverse", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationtype-traverse")]
    Traverse,
}

/// <summary>https://html.spec.whatwg.org/multipage/browsing-the-web.html#user-navigation-involvement</summary>
internal enum UserNavigationInvolvement
{
    None,
    Activation,
    BrowserUI,
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationupdatecurrententryoptions</summary>
[IdlInterface("NavigationUpdateCurrentEntryOptions", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationupdatecurrententryoptions")]
public sealed class NavigationUpdateCurrentEntryOptions
{
    /// <summary>The state: a value of the script engine, or null for undefined.</summary>
    [IdlMember("state", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationupdatecurrententryoptions-state")]
    public required object? State { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationoptions</summary>
[IdlInterface("NavigationOptions", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationoptions")]
public class NavigationOptions
{
    /// <summary>The info given to the navigate event: a value of the script engine, or null for undefined.</summary>
    [IdlMember("info", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationoptions-info")]
    public object? Info { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationnavigateoptions</summary>
[IdlInterface("NavigationNavigateOptions", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationnavigateoptions")]
public sealed class NavigationNavigateOptions : NavigationOptions
{
    /// <summary>The state of the new entry: a value of the script engine.</summary>
    [IdlMember("state", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationnavigateoptions-state")]
    public object? State
    {
        get;
        init
        {
            field = value;
            HasState = true;
        }
    }

    /// <summary>Whether the member exists.</summary>
    internal bool HasState { get; private init; }

    /// <summary>Gets whether the navigation pushes a new entry or replaces the current one.</summary>
    [IdlMember("history", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationnavigateoptions-history")]
    public NavigationHistoryBehavior History { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationreloadoptions</summary>
[IdlInterface("NavigationReloadOptions", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationreloadoptions")]
public sealed class NavigationReloadOptions : NavigationOptions
{
    /// <summary>The new state of the current entry: a value of the script engine.</summary>
    [IdlMember("state", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationreloadoptions-state")]
    public object? State
    {
        get;
        init
        {
            field = value;
            HasState = true;
        }
    }

    /// <summary>Whether the member exists.</summary>
    internal bool HasState { get; private init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationresult</summary>
[IdlInterface("NavigationResult", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationresult")]
public sealed class NavigationResult
{
    /// <summary>Gets the task that completes with the new current entry when the navigation is committed.</summary>
    [IdlMember("committed", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationresult-committed")]
    public required Task<NavigationHistoryEntry> Committed { get; init; }

    /// <summary>
    /// Gets the task that completes with the entry of the navigation when it has finished successfully.
    /// </summary>
    [IdlMember("finished", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationresult-finished")]
    public required Task<NavigationHistoryEntry> Finished { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigation-api-method-tracker</summary>
internal sealed class NavigationApiMethodTracker(Navigation navigation, string? key, object? info, bool hasSerializedState, object? serializedState)
{
    public Navigation Navigation { get; } = navigation;

    public string? Key { get; } = key;

    public object? Info { get; } = info;

    /// <summary>Whether the tracker has a serialized state (a traversal does not change the state).</summary>
    public bool HasSerializedState { get; set; } = hasSerializedState;

    public object? SerializedState { get; set; } = serializedState;

    public NavigationHistoryEntry? CommittedToEntry { get; set; }

    public TaskCompletionSource<NavigationHistoryEntry> CommittedPromise { get; } = new();

    public TaskCompletionSource<NavigationHistoryEntry> FinishedPromise { get; } = new();

    public bool Pending { get; set; }
}

/// <summary>
/// The navigation API of a window (https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigation-api): the
/// same-origin session history entries of its navigable, the navigations of the navigable (the navigate event, which can
/// cancel or intercept them), and the methods that navigate and traverse.
/// </summary>
/// <remarks>
/// The states are serialized by the script engine of the window. The promises of the results and of the transitions are
/// settled by the tasks of the event loop. The exceptions that reject them are created in the realm of the window.
/// </remarks>
[IdlInterface("Navigation", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigation")]
public sealed class Navigation : EventTarget
{
    private readonly List<NavigationHistoryEntry> _entryList = [];
    private const int MaxNavigateEventNesting = 32;

    private readonly Dictionary<string, NavigationApiMethodTracker> _upcomingTraverseApiMethodTrackers = new(StringComparer.Ordinal);
    private int _currentEntryIndex = -1;
    private int _navigateEventNesting;

    internal Navigation(Window window)
    {
        Window = window;
    }

    internal Window Window { get; }

    internal override Window? RelevantGlobal => Window;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#ongoing-navigate-event</summary>
    internal NavigateEvent? OngoingNavigateEvent { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#focus-changed-during-ongoing-navigation</summary>
    internal bool FocusChangedDuringOngoingNavigation { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#suppress-normal-scroll-restoration-during-ongoing-navigation</summary>
    internal bool SuppressNormalScrollRestorationDuringOngoingNavigation { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#ongoing-api-method-tracker</summary>
    internal NavigationApiMethodTracker? OngoingApiMethodTracker { get; set; }

    /// <summary>
    /// Whether the navigate event is dispatched too many times recursively (e.g. its listener navigates again): as the
    /// browsers throttle the navigations, the nested navigations are refused.
    /// </summary>
    internal bool IsNavigateEventNestingTooDeep => _navigateEventNesting >= MaxNavigateEventNesting;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#has-entries-and-events-disabled</summary>
    internal bool HasEntriesAndEventsDisabled
    {
        get
        {
            var document = Window.Document;
            return !document.IsFullyActive || document.IsInitialAboutBlank || document.Origin.IsOpaque;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigation-current-entry</summary>
    internal NavigationHistoryEntry? CurrentEntryInternal => HasEntriesAndEventsDisabled || _currentEntryIndex < 0 || _currentEntryIndex >= _entryList.Count ? null : _entryList[_currentEntryIndex];

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#getting-the-navigation-api-entry-index</summary>
    /// <summary>Returns the entry of the entry list whose session history entry is the entry, or null.</summary>
    internal NavigationHistoryEntry? FindEntry(SessionHistoryEntry sessionHistoryEntry) => GetEntryIndex(sessionHistoryEntry) is >= 0 and var index ? _entryList[index] : null;

    internal int GetEntryIndex(SessionHistoryEntry sessionHistoryEntry) => _entryList.FindIndex(entry => ReferenceEquals(entry.SessionHistoryEntry, sessionHistoryEntry));

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-entries</summary>
    [IdlMember("entries", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-entries")]
    public IReadOnlyList<NavigationHistoryEntry> Entries() => HasEntriesAndEventsDisabled ? [] : [.. _entryList];

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-currententry</summary>
    [IdlMember("currentEntry", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-currententry")]
    public NavigationHistoryEntry? CurrentEntry => CurrentEntryInternal;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-updatecurrententry</summary>
    [IdlMember("updateCurrentEntry", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-updatecurrententry")]
    public void UpdateCurrentEntry(NavigationUpdateCurrentEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var current = CurrentEntryInternal ?? throw new DomException("There is no current entry", "InvalidStateError");
        var serializedState = SerializeState(options.State);
        current.SessionHistoryEntry.NavigationApiState = serializedState;
        FireEvent(new NavigationCurrentEntryChangeEvent("currententrychange", new NavigationCurrentEntryChangeEventInit { NavigationType = null, From = current }));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-transition</summary>
    [IdlMember("transition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-transition")]
    public NavigationTransition? Transition { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-activation</summary>
    [IdlMember("activation", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-activation")]
    public NavigationActivation? Activation { get; private set; }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-cangoback</summary>
    [IdlMember("canGoBack", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-cangoback")]
    public bool CanGoBack => !HasEntriesAndEventsDisabled && _currentEntryIndex > 0;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-cangoforward</summary>
    [IdlMember("canGoForward", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-cangoforward")]
    public bool CanGoForward => !HasEntriesAndEventsDisabled && _currentEntryIndex >= 0 && _currentEntryIndex < _entryList.Count - 1;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-navigate</summary>
    [IdlMember("navigate", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-navigate")]
    public NavigationResult Navigate(string url, NavigationNavigateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        options ??= new NavigationNavigateOptions();
        var document = Window.Document;
        if (UrlParser.Parse(url, document.DocumentBaseUrl) is not { } urlRecord)
            return EarlyErrorResult(CreateException($"'{url}' is not a valid URL", "SyntaxError"));

        if (urlRecord.Scheme == "javascript")
            return EarlyErrorResult(CreateException("The navigation API cannot navigate to a javascript: URL", "NotSupportedError"));

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#navigation-must-be-a-replace
        if (options.History == NavigationHistoryBehavior.Push && document.IsInitialAboutBlank)
            return EarlyErrorResult(CreateException("The navigation must be a replace", "NotSupportedError"));

        object? serializedState;
        try
        {
            serializedState = SerializeState(options.HasState ? options.State : null);
        }
#pragma warning disable CA1031 // The exceptions of the serialization are the early error of the result
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return EarlyErrorResult(ex);
        }

        if (!document.IsFullyActive || document.BrowsingContext is not { } navigable)
            return EarlyErrorResult(CreateException("The document is not fully active", "InvalidStateError"));

        if (document.UnloadCounter > 0)
            return EarlyErrorResult(CreateException("The document is unloading", "InvalidStateError"));

        var apiMethodTracker = SetUpNavigateReloadApiMethodTracker(options.Info, serializedState);
        navigable.Navigate(urlRecord, document, options.History, navigationApiState: new NavigationApiStateValue(serializedState), apiMethodTracker: apiMethodTracker);
        return GetApiMethodTrackerDerivedResult(apiMethodTracker);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-reload</summary>
    [IdlMember("reload", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-reload")]
    public NavigationResult Reload(NavigationReloadOptions? options = null)
    {
        options ??= new NavigationReloadOptions();
        var document = Window.Document;
        object? serializedState = null;
        if (options.HasState)
        {
            try
            {
                serializedState = SerializeState(options.State);
            }
#pragma warning disable CA1031 // The exceptions of the serialization are the early error of the result
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return EarlyErrorResult(ex);
            }
        }
        else if (CurrentEntryInternal is { } current)
        {
            serializedState = current.SessionHistoryEntry.NavigationApiState;
        }

        if (!document.IsFullyActive || document.BrowsingContext is not { } navigable)
            return EarlyErrorResult(CreateException("The document is not fully active", "InvalidStateError"));

        if (document.UnloadCounter > 0)
            return EarlyErrorResult(CreateException("The document is unloading", "InvalidStateError"));

        var apiMethodTracker = SetUpNavigateReloadApiMethodTracker(options.Info, serializedState);
        navigable.Reload(new NavigationApiStateValue(serializedState), apiMethodTracker);
        return GetApiMethodTrackerDerivedResult(apiMethodTracker);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-traverseto</summary>
    [IdlMember("traverseTo", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-traverseto")]
    public NavigationResult TraverseTo(string key, NavigationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_currentEntryIndex == -1 || !_entryList.Exists(entry => entry.SessionHistoryEntry.NavigationApiKey == key))
            return EarlyErrorResult(CreateException("There is no entry with this key", "InvalidStateError"));

        return PerformTraversal(key, options);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-back</summary>
    [IdlMember("back", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-back")]
    public NavigationResult Back(NavigationOptions? options = null)
    {
        if (_currentEntryIndex is -1 or 0 || _currentEntryIndex > _entryList.Count)
            return EarlyErrorResult(CreateException("There is no previous entry", "InvalidStateError"));

        return PerformTraversal(_entryList[_currentEntryIndex - 1].SessionHistoryEntry.NavigationApiKey, options);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-forward</summary>
    [IdlMember("forward", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigation-forward")]
    public NavigationResult Forward(NavigationOptions? options = null)
    {
        if (_currentEntryIndex == -1 || _currentEntryIndex >= _entryList.Count - 1)
            return EarlyErrorResult(CreateException("There is no next entry", "InvalidStateError"));

        return PerformTraversal(_entryList[_currentEntryIndex + 1].SessionHistoryEntry.NavigationApiKey, options);
    }

    /// <summary>Gets or sets the event handler of the <c>navigate</c> event.</summary>
    [IdlMember("onnavigate", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#handler-navigation-onnavigate")]
    public Action<Event>? OnNavigate
    {
        get => GetEventHandler("navigate");
        set => SetEventHandler("navigate", value);
    }

    /// <summary>Gets or sets the event handler of the <c>navigatesuccess</c> event.</summary>
    [IdlMember("onnavigatesuccess", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#handler-navigation-onnavigatesuccess")]
    public Action<Event>? OnNavigateSuccess
    {
        get => GetEventHandler("navigatesuccess");
        set => SetEventHandler("navigatesuccess", value);
    }

    /// <summary>Gets or sets the event handler of the <c>navigateerror</c> event.</summary>
    [IdlMember("onnavigateerror", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#handler-navigation-onnavigateerror")]
    public Action<Event>? OnNavigateError
    {
        get => GetEventHandler("navigateerror");
        set => SetEventHandler("navigateerror", value);
    }

    /// <summary>Gets or sets the event handler of the <c>currententrychange</c> event.</summary>
    [IdlMember("oncurrententrychange", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#handler-navigation-oncurrententrychange")]
    public Action<Event>? OnCurrentEntryChange
    {
        get => GetEventHandler("currententrychange");
        set => SetEventHandler("currententrychange", value);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Serialized states
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>StructuredSerializeForStorage, by the script engine of the window; undefined (null) is not serialized.</summary>
    internal object? SerializeState(object? value)
    {
        if (value is null)
            return null;

        return Window.ScriptEngine is { } engine ? engine.StructuredSerializeForStorage(Window, value) : StructuredClone.Serialize(value);
    }

    /// <summary>StructuredDeserialize, in the realm of the window: the serialization of undefined is <see cref="UndefinedValue"/>.</summary>
    internal object? DeserializeState(object? serialized)
    {
        if (serialized is null)
            return UndefinedValue.Instance;

        return Window.ScriptEngine is { } engine ? engine.StructuredDeserialize(Window, serialized) : StructuredClone.Deserialize(serialized);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Entry list
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#initialize-the-navigation-api-entries-for-a-new-document</summary>
    internal void InitializeEntriesForNewDocument(IReadOnlyList<SessionHistoryEntry> newEntries, SessionHistoryEntry initialEntry)
    {
        _entryList.Clear();
        _currentEntryIndex = -1;
        if (HasEntriesAndEventsDisabled)
            return;

        foreach (var entry in newEntries)
        {
            _entryList.Add(new NavigationHistoryEntry(this, entry));
        }

        _currentEntryIndex = GetEntryIndex(initialEntry);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/nav-history-apis.html#update-the-navigation-api-entries-for-a-same-document-navigation:
    /// fires currententrychange and the dispose events, and runs the handlers of the intercepted navigation.
    /// </summary>
    internal void UpdateEntriesForSameDocumentNavigation(SessionHistoryEntry destinationEntry, NavigationType navigationType)
    {
        if (HasEntriesAndEventsDisabled || CurrentEntryInternal is not { } oldCurrentEntry)
            return;

        var disposedEntries = new List<NavigationHistoryEntry>();
        if (navigationType == NavigationType.Traverse)
        {
            var index = GetEntryIndex(destinationEntry);
            if (index < 0)
                return;

            _currentEntryIndex = index;
        }
        else if (navigationType == NavigationType.Push)
        {
            _currentEntryIndex++;
            for (var i = _currentEntryIndex; i < _entryList.Count; i++)
            {
                disposedEntries.Add(_entryList[i]);
            }

            _entryList.RemoveRange(_currentEntryIndex, _entryList.Count - _currentEntryIndex);
        }
        else if (navigationType == NavigationType.Replace)
        {
            disposedEntries.Add(oldCurrentEntry);
        }

        if (navigationType is NavigationType.Push or NavigationType.Replace)
        {
            var newEntry = new NavigationHistoryEntry(this, destinationEntry);
            if (_currentEntryIndex == _entryList.Count)
            {
                _entryList.Add(newEntry);
            }
            else
            {
                _entryList[_currentEntryIndex] = newEntry;
            }
        }

        if (OngoingApiMethodTracker is { } tracker && CurrentEntryInternal is { } currentEntry)
        {
            NotifyAboutCommittedToEntry(tracker, currentEntry);
        }

        var navigateEvent = OngoingNavigateEvent;
        var apiMethodTracker = OngoingApiMethodTracker;
        using (PrepareToRunScript())
        {
            FireEvent(new NavigationCurrentEntryChangeEvent("currententrychange", new NavigationCurrentEntryChangeEventInit { NavigationType = navigationType, From = oldCurrentEntry }));
            foreach (var disposedEntry in disposedEntries)
            {
                disposedEntry.FireEvent(new Event("dispose"));
            }

            RunInterceptCommitHandlerSteps(navigateEvent, apiMethodTracker);
        }
    }

    /// <summary>Whether the entry list has an entry of one of the session history entries.</summary>
    internal bool HasEntryOf(List<SessionHistoryEntry> sessionHistoryEntries) => _entryList.Exists(entry => sessionHistoryEntries.Contains(entry.SessionHistoryEntry));

    /// <summary>
    /// Removes the forward entries whose session history entries were removed from the session history by the navigation of
    /// another navigable, then fires their dispose events (the entry list is updated before the events are fired).
    /// </summary>
    internal void DisposeEntriesRemovedFromSessionHistory(List<SessionHistoryEntry> removedEntries)
    {
        if (HasEntriesAndEventsDisabled)
            return;

        var disposedEntries = new List<NavigationHistoryEntry>();
        for (var i = _currentEntryIndex + 1; i < _entryList.Count; i++)
        {
            if (removedEntries.Contains(_entryList[i].SessionHistoryEntry))
            {
                disposedEntries.Add(_entryList[i]);
            }
        }

        if (disposedEntries.Count == 0)
            return;

        _entryList.RemoveAll(disposedEntries.Contains);
        using (PrepareToRunScript())
        {
            foreach (var disposedEntry in disposedEntries)
            {
                // A dispose event handler may destroy the navigable (e.g. it removes the iframe)
                if (!Window.Document.IsFullyActive)
                    break;

                disposedEntry.FireEvent(new Event("dispose"));
            }
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/browsing-the-web.html#update-document-for-history-step-application, step 6: the
    /// activation of a document by a cross-document navigation.
    /// </summary>
    internal void UpdateActivation(SessionHistoryEntry? previousEntry, NavigationType navigationType, Document document)
    {
        if (previousEntry is null || (navigationType != NavigationType.Reload && ReferenceEquals(previousEntry.Document, document)))
            return;

        var activation = Activation ??= new NavigationActivation();
        var previousEntryIndex = GetEntryIndex(previousEntry);
        if (previousEntryIndex >= 0)
        {
            activation.From = _entryList[previousEntryIndex];
        }
        else if (navigationType == NavigationType.Replace && previousEntry.DocumentState.Origin is { } origin && OriginRecord.IsSameOrigin(origin, document.Origin) && !previousEntry.DocumentState.IsInitialAboutBlank)
        {
            activation.From = new NavigationHistoryEntry(this, previousEntry);
        }

        activation.Entry = CurrentEntryInternal;
        activation.NavigationType = navigationType;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // API method trackers
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigation-api-early-error-result</summary>
    private static NavigationResult EarlyErrorResult(Exception exception) => new()
    {
        Committed = Task.FromException<NavigationHistoryEntry>(exception),
        Finished = Task.FromException<NavigationHistoryEntry>(exception),
    };

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigation-api-method-tracker-derived-result</summary>
    private static NavigationResult GetApiMethodTrackerDerivedResult(NavigationApiMethodTracker apiMethodTracker)
    {
        if (apiMethodTracker.Pending)
            return EarlyErrorResult(apiMethodTracker.Navigation.CreateException("The navigation was aborted", "AbortError"));

        return new NavigationResult { Committed = apiMethodTracker.CommittedPromise.Task, Finished = apiMethodTracker.FinishedPromise.Task };
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#set-up-a-navigate/reload-api-method-tracker</summary>
    private NavigationApiMethodTracker SetUpNavigateReloadApiMethodTracker(object? info, object? serializedState)
    {
        var tracker = new NavigationApiMethodTracker(this, key: null, info, hasSerializedState: true, serializedState) { Pending = !HasEntriesAndEventsDisabled };
        PromiseSlots.MarkAsHandled(tracker.FinishedPromise.Task);
        return tracker;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#add-an-upcoming-traverse-api-method-tracker</summary>
    private NavigationApiMethodTracker AddUpcomingTraverseApiMethodTracker(string destinationKey, object? info)
    {
        var tracker = new NavigationApiMethodTracker(this, destinationKey, info, hasSerializedState: false, serializedState: null);
        PromiseSlots.MarkAsHandled(tracker.FinishedPromise.Task);
        _upcomingTraverseApiMethodTrackers[destinationKey] = tracker;
        return tracker;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigation-api-method-tracker-clean-up</summary>
    private void CleanUp(NavigationApiMethodTracker apiMethodTracker)
    {
        if (ReferenceEquals(OngoingApiMethodTracker, apiMethodTracker))
        {
            OngoingApiMethodTracker = null;
        }
        else if (apiMethodTracker.Key is { } key && _upcomingTraverseApiMethodTrackers.TryGetValue(key, out var upcoming) && ReferenceEquals(upcoming, apiMethodTracker))
        {
            _upcomingTraverseApiMethodTrackers.Remove(key);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#notify-about-the-committed-to-entry</summary>
    private static void NotifyAboutCommittedToEntry(NavigationApiMethodTracker apiMethodTracker, NavigationHistoryEntry entry)
    {
        apiMethodTracker.CommittedToEntry = entry;
        if (apiMethodTracker.HasSerializedState)
        {
            entry.SessionHistoryEntry.NavigationApiState = apiMethodTracker.SerializedState;
        }

        apiMethodTracker.CommittedPromise.TrySetResult(entry);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#resolve-the-finished-promise</summary>
    private void ResolveFinishedPromise(NavigationApiMethodTracker apiMethodTracker)
    {
        if (apiMethodTracker.CommittedToEntry is { } entry)
        {
            apiMethodTracker.FinishedPromise.TrySetResult(entry);
        }

        CleanUp(apiMethodTracker);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#reject-the-finished-promise</summary>
    private void RejectFinishedPromise(NavigationApiMethodTracker apiMethodTracker, object? reason)
    {
        var exception = ToException(reason);
        apiMethodTracker.CommittedPromise.TrySetException(exception);
        apiMethodTracker.FinishedPromise.TrySetException(exception);
        CleanUp(apiMethodTracker);
    }

    /// <summary>The exception of a rejection reason: an exception, or a value of the script engine.</summary>
    private static Exception ToException(object? reason) => reason as Exception ?? new AbortSignalException(reason);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#performing-a-navigation-api-traversal</summary>
    private NavigationResult PerformTraversal(string key, NavigationOptions? options)
    {
        var document = Window.Document;
        if (!document.IsFullyActive || document.BrowsingContext is not { } navigable)
            return EarlyErrorResult(CreateException("The document is not fully active", "InvalidStateError"));

        if (document.UnloadCounter > 0)
            return EarlyErrorResult(CreateException("The document is unloading", "InvalidStateError"));

        if (CurrentEntryInternal is { } current && current.SessionHistoryEntry.NavigationApiKey == key)
        {
            return new NavigationResult { Committed = Task.FromResult(current), Finished = Task.FromResult(current) };
        }

        if (_upcomingTraverseApiMethodTrackers.TryGetValue(key, out var upcoming))
            return GetApiMethodTrackerDerivedResult(upcoming);

        var apiMethodTracker = AddUpcomingTraverseApiMethodTracker(key, options?.Info);
        var hasTransientActivation = Window.HasTransientActivation;
        navigable.Traversable.QueueSessionHistoryTraversal(() =>
        {
            var targetEntry = navigable.GetSessionHistoryEntries()?.Find(entry => entry.NavigationApiKey == key);
            if (targetEntry is null || ReferenceEquals(targetEntry, navigable.ActiveEntry))
            {
                document.QueueGlobalTask(() => RejectFinishedPromise(apiMethodTracker, CreateException("The entry cannot be traversed to", "InvalidStateError")));
                return;
            }

            // The step is the nearest step of the joint session history where the navigable shows the entry (as the browsers
            // do: the step of the entry itself could also change the other navigables)
            var traversable = navigable.Traversable;
            var targetStep = targetEntry.Step;
            foreach (var step in traversable.GetAllUsedHistorySteps())
            {
                if (ReferenceEquals(navigable.GetTargetHistoryEntry(step), targetEntry) && Math.Abs(step - traversable.CurrentStep) < Math.Abs(targetStep - traversable.CurrentStep))
                {
                    targetStep = step;
                }
            }

            var result = traversable.ApplyTraverseHistoryStep(targetStep, changingNavigable => Sandboxing.IsAllowedToNavigate(navigable, changingNavigable, document.ActiveSandboxingFlags, hasTransientActivation));
            if (result == HistoryStepResult.InitiatorDisallowed)
            {
                document.QueueGlobalTask(() => RejectFinishedPromise(apiMethodTracker, CreateException("The traversal is not allowed", "SecurityError")));
            }
            else if (result == HistoryStepResult.CanceledByNavigate && _upcomingTraverseApiMethodTrackers.TryGetValue(key, out var canceled) && ReferenceEquals(canceled, apiMethodTracker))
            {
                // The navigate event of the traversable canceled the traversal before the navigate event of this navigable was
                // fired: the specification leaves the promises pending; as in Chromium and WebKit, they are rejected
                document.QueueGlobalTask(() => RejectFinishedPromise(apiMethodTracker, CreateException("The traversal was canceled", "AbortError")));
            }
        });

        return GetApiMethodTrackerDerivedResult(apiMethodTracker);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Ongoing navigation
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#abort-the-ongoing-navigation</summary>
    internal void AbortOngoingNavigation(object? error = null)
    {
        if (OngoingNavigateEvent is not { } @event)
            return;

        FocusChangedDuringOngoingNavigation = false;
        SuppressNormalScrollRestorationDuringOngoingNavigation = false;

        // The location of the error of the navigateerror event is the one of the script that aborts the navigation (e.g.
        // the call of window.stop(), or the removal of the iframe)
        ScriptLocation? location = null;
        if (error is null)
        {
            error = CreateException("The navigation was aborted", "AbortError");
            location = Window.ScriptEngine?.GetCurrentScriptLocation();
        }

        if (@event.DispatchFlag)
        {
            @event.CanceledFlag = true;
        }

        AbortNavigateEvent(@event, error, location);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#abort-a-navigateevent</summary>
    /// <param name="event">The navigate event.</param>
    /// <param name="reason">The error.</param>
    /// <param name="location">The location of the script that created the error, when the error does not have one.</param>
    private void AbortNavigateEvent(NavigateEvent @event, object? reason, ScriptLocation? location = null)
    {
        // The signal is aborted while the event is still the ongoing navigate event: a navigation started by an abort event
        // listener aborts the ongoing navigation again, which rejects the promises of the ongoing API method tracker
        @event.AbortController?.Abort(reason);

        // https://html.spec.whatwg.org/multipage/webappapis.html#extract-error: the location is implementation-defined. As
        // in Chromium, an error without location (e.g. the AbortError created when no script is running, or a rejection
        // reason that is not an error object) is located in the document
        var errorInfo = Window.ScriptEngine?.ExtractErrorInformation(reason);
        var filename = errorInfo?.Filename ?? "";
        var lineNumber = errorInfo?.LineNumber ?? 0;
        var columnNumber = errorInfo?.ColumnNumber ?? 0;
        if (filename.Length == 0)
        {
            (filename, lineNumber, columnNumber) = location is { } scriptLocation
                ? (scriptLocation.Url, scriptLocation.LineNumber, scriptLocation.ColumnNumber)
                : (Window.Document.Url, 0, 0);
        }

        OngoingNavigateEvent = null;
        if (OngoingApiMethodTracker is { } apiMethodTracker)
        {
            RejectFinishedPromise(apiMethodTracker, reason);
        }

        FireEvent(new ErrorEvent("navigateerror", new ErrorEventInit
        {
            Message = errorInfo?.Message ?? (reason as Exception)?.Message ?? "",
            Filename = filename,
            Lineno = (uint)lineNumber,
            Colno = (uint)columnNumber,
            Error = reason,
        }));

        if (Transition is not { } transition)
            return;

        var exception = ToException(reason);
        transition.CommittedPromise.TrySetException(exception);
        transition.FinishedPromise.TrySetException(exception);
        Transition = null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#inform-the-navigation-api-about-aborting-navigation</summary>
    internal void InformAboutAbortingNavigation()
    {
        while (OngoingNavigateEvent is not null)
        {
            AbortOngoingNavigation();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#inform-the-navigation-api-about-child-navigable-destruction</summary>
    internal void InformAboutChildNavigableDestruction()
    {
        InformAboutAbortingNavigation();
        foreach (var apiMethodTracker in _upcomingTraverseApiMethodTrackers.Values.ToList())
        {
            RejectFinishedPromise(apiMethodTracker, CreateException("The navigable was destroyed", "AbortError"));
        }
    }

    /// <summary>The ongoing navigation of a cross-document navigation is no longer tracked when the document changes (no navigateerror).</summary>
    internal void ClearOngoingNavigation()
    {
        OngoingNavigateEvent = null;
        OngoingApiMethodTracker = null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Firing the navigate event
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#fire-a-traverse-navigate-event</summary>
    internal bool FireTraverseNavigateEvent(SessionHistoryEntry destinationEntry, UserNavigationInvolvement userInvolvement = UserNavigationInvolvement.None)
    {
        var destinationHistoryEntry = _entryList.Find(entry => ReferenceEquals(entry.SessionHistoryEntry, destinationEntry));
        var destination = new NavigationDestination(this, destinationEntry.Url)
        {
            Entry = destinationHistoryEntry,
            State = destinationHistoryEntry is null ? null : destinationEntry.NavigationApiState,
            IsSameDocument = ReferenceEquals(destinationEntry.Document, Window.Document),
        };

        return InnerNavigateEventFiringAlgorithm(NavigationType.Traverse, destination, userInvolvement, sourceElement: null, formData: null, downloadRequestFilename: null, classicHistoryApiState: null, apiMethodTracker: null);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#fire-a-download-request-navigate-event</summary>
    internal bool FireDownloadRequestNavigateEvent(UrlRecord destinationUrl, UserNavigationInvolvement userInvolvement, Element? sourceElement, string filename)
    {
        if (IsNavigateEventNestingTooDeep)
            return false;

        // The inner navigate event firing algorithm asserts that there is no ongoing navigate event, but the specification
        // only aborts the ongoing navigation once the event of the download request is not canceled: as in Chromium and
        // Firefox, the ongoing (intercepted) navigation is aborted before the event is fired
        InformAboutAbortingNavigation();
        if (!Window.Document.IsFullyActive)
            return false;

        var destination = new NavigationDestination(this, destinationUrl) { IsSameDocument = false };
        return InnerNavigateEventFiringAlgorithm(NavigationType.Push, destination, userInvolvement, sourceElement, formData: null, downloadRequestFilename: filename, classicHistoryApiState: null, apiMethodTracker: null);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#fire-a-push/replace/reload-navigate-event</summary>
    internal bool FirePushReplaceReloadNavigateEvent(NavigationType navigationType, UrlRecord destinationUrl, bool isSameDocument, UserNavigationInvolvement userInvolvement = UserNavigationInvolvement.None, Element? sourceElement = null, FormData? formData = null, object? navigationApiState = null, NavigationApiStateValue? classicHistoryApiState = null, NavigationApiMethodTracker? apiMethodTracker = null)
    {
        if (IsNavigateEventNestingTooDeep)
            return false;

        InformAboutAbortingNavigation();
        if (HasEntriesAndEventsDisabled && apiMethodTracker is not null)
        {
            apiMethodTracker.Pending = false;
            apiMethodTracker = null;
        }

        if (!Window.Document.IsFullyActive)
            return false;

        // As in the browsers, the element of a cross-origin initiator is not exposed (e.g. a hyperlink of the parent document
        // that navigates a cross-origin iframe to a fragment); the specification does not say so
        if (sourceElement is not null && !Document.IsSameOriginDomain(sourceElement.NodeDocument, Window.Document))
        {
            sourceElement = null;
        }

        var destination = new NavigationDestination(this, destinationUrl) { State = navigationApiState, IsSameDocument = isSameDocument };
        return InnerNavigateEventFiringAlgorithm(navigationType, destination, userInvolvement, sourceElement, formData, downloadRequestFilename: null, classicHistoryApiState, apiMethodTracker);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#inner-navigate-event-firing-algorithm</summary>
    private bool InnerNavigateEventFiringAlgorithm(NavigationType navigationType, NavigationDestination destination, UserNavigationInvolvement userInvolvement, Element? sourceElement, FormData? formData, string? downloadRequestFilename, NavigationApiStateValue? classicHistoryApiState, NavigationApiMethodTracker? apiMethodTracker)
    {
        if (HasEntriesAndEventsDisabled)
            return true;

        if (destination.Entry is { } destinationEntry && _upcomingTraverseApiMethodTrackers.Remove(destinationEntry.SessionHistoryEntry.NavigationApiKey, out var upcoming))
        {
            apiMethodTracker = upcoming;
        }

        if (apiMethodTracker is not null)
        {
            OngoingApiMethodTracker = apiMethodTracker;
            apiMethodTracker.Pending = false;
        }

        var document = Window.Document;
        var navigable = document.BrowsingContext;
        var canIntercept = History.CanHaveUrlRewritten(document.UrlRecord, destination.UrlRecord) && (destination.IsSameDocument || navigationType != NavigationType.Traverse);
        var traverseCanBeCanceled = navigable is { Parent: null } && destination.IsSameDocument && (userInvolvement != UserNavigationInvolvement.BrowserUI || Window.HasHistoryActionActivation);
        var currentUrl = document.UrlRecord;
        var hashChange = classicHistoryApiState is null && destination.IsSameDocument && BrowsingContext.UrlEquals(destination.UrlRecord, currentUrl, excludeFragments: true) && destination.UrlRecord.Fragment != currentUrl.Fragment;
        var abortController = new AbortController();
        var @event = new NavigateEvent("navigate", new NavigateEventInit
        {
            Cancelable = navigationType != NavigationType.Traverse || traverseCanBeCanceled,
            NavigationType = navigationType,
            Destination = destination,
            CanIntercept = canIntercept,
            UserInitiated = userInvolvement != UserNavigationInvolvement.None,
            HashChange = hashChange,
            Signal = abortController.Signal,
            FormData = formData,
            DownloadRequest = downloadRequestFilename,
            Info = apiMethodTracker?.Info,
            SourceElement = sourceElement,
        })
        {
            AbortController = abortController,
            ClassicHistoryApiState = classicHistoryApiState?.Value,
            Navigation = this,
        };

        OngoingNavigateEvent = @event;
        FocusChangedDuringOngoingNavigation = false;
        SuppressNormalScrollRestorationDuringOngoingNavigation = false;
        bool dispatchResult;
        _navigateEventNesting++;
        try
        {
            dispatchResult = FireEvent(@event);
        }
        finally
        {
            _navigateEventNesting--;
        }

        if (!dispatchResult)
        {
            if (navigationType == NavigationType.Traverse)
            {
                Window.ConsumeHistoryActionUserActivation();
            }

            if (!abortController.Signal.Aborted)
            {
                AbortOngoingNavigation();
            }

            return false;
        }

        if (@event.InterceptionState != NavigateEventInterceptionState.None && CurrentEntryInternal is { } fromEntry)
        {
            Transition = new NavigationTransition(navigationType, fromEntry, destination);
        }

        if (@event.PrecommitHandlers.Count == 0)
        {
            CommitNavigateEvent(@event);
        }
        else
        {
            var precommitController = new NavigationPrecommitController(@event);
            var promises = new List<object?>();
            foreach (var handler in @event.PrecommitHandlers)
            {
                promises.Add(InvokePromiseCallback(() => handler(precommitController)));
            }

            WaitForAll(promises, () => CommitNavigateEvent(@event), reason => ProcessNavigateEventHandlerFailure(@event, reason));
        }

        return @event.InterceptionState == NavigateEventInterceptionState.None;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#commit-a-navigate-event</summary>
    private void CommitNavigateEvent(NavigateEvent @event)
    {
        var document = Window.Document;
        if (!document.IsFullyActive || document.BrowsingContext is not { } navigable || @event.Signal.Aborted)
            return;

        using (PrepareToRunScript())
        {
            if (@event.InterceptionState != NavigateEventInterceptionState.None)
            {
                @event.InterceptionState = NavigateEventInterceptionState.Committed;
                switch (@event.NavigationType)
                {
                    case NavigationType.Push or NavigationType.Replace:
                        // As in the browsers, the classic history API state of the entry of an intercepted navigation that was
                        // not started by history.pushState() or history.replaceState() is null (the specification keeps the
                        // state of the active entry)
                        navigable.UpdateUrlAndHistory(@event.Destination.UrlRecord, @event.ClassicHistoryApiState, hasSerializedData: true, @event.NavigationType == NavigationType.Push ? NavigationHistoryBehavior.Push : NavigationHistoryBehavior.Replace);
                        break;

                    case NavigationType.Reload:
                        UpdateEntriesForSameDocumentNavigation(navigable.ActiveEntry, NavigationType.Reload);
                        break;

                    case NavigationType.Traverse:
                        SuppressNormalScrollRestorationDuringOngoingNavigation = true;
                        var userInvolvement = @event.UserInitiated ? UserNavigationInvolvement.Activation : UserNavigationInvolvement.None;
                        if (@event.Destination.Entry?.SessionHistoryEntry is { } targetEntry)
                        {
                            navigable.Traversable.QueueSessionHistoryTraversal(() => navigable.Traversable.ResumeApplyingTraverseHistoryStep(targetEntry.Step, userInvolvement));
                        }

                        break;
                }
            }

            // The tracker of a cross-document navigation stays the ongoing one until the document changes (as the browsers do,
            // the specification cleans it up here): its promises are rejected when the navigation is aborted
            Transition?.CommittedPromise.TrySetResult();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigate-event-intercept-commit-handler-steps</summary>
    private void RunInterceptCommitHandlerSteps(NavigateEvent? @event, NavigationApiMethodTracker? apiMethodTracker)
    {
        if (@event is null)
            return;

        var promises = new List<object?>();
        foreach (var handler in @event.Handlers)
        {
            promises.Add(InvokePromiseCallback(() => handler()));
        }

        if (promises.Count == 0)
        {
            promises.Add(null);
        }

        WaitForAll(promises, () =>
        {
            if (!Window.Document.IsFullyActive || @event.Signal.Aborted)
                return;

            OngoingNavigateEvent = null;
            @event.Finish(didFulfill: true);
            if (apiMethodTracker is not null)
            {
                ResolveFinishedPromise(apiMethodTracker);
            }

            FireEvent(new Event("navigatesuccess"));
            if (Transition is { } transition)
            {
                transition.FinishedPromise.TrySetResult();
                Transition = null;
            }
        }, reason => ProcessNavigateEventHandlerFailure(@event, reason));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#process-navigate-event-handler-failure</summary>
    private void ProcessNavigateEventHandlerFailure(NavigateEvent @event, object? reason)
    {
        if (!Window.Document.IsFullyActive || @event.Signal.Aborted)
            return;

        if (@event.InterceptionState != NavigateEventInterceptionState.Intercepted)
        {
            @event.Finish(didFulfill: false);
        }

        AbortNavigateEvent(@event, reason);
    }

    /// <summary>Invokes a callback whose return type is a promise: an exception is a rejected promise (https://webidl.spec.whatwg.org/#invoke-a-callback-function).</summary>
    private static object? InvokePromiseCallback(Func<object?> callback)
    {
        try
        {
            return callback();
        }
#pragma warning disable CA1031 // The exception rejects the promise
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return new RejectedPromise(ex);
        }
    }

    private void WaitForAll(List<object?> promises, Action successSteps, Action<object?> failureSteps)
    {
        // The steps do nothing when the document is no longer fully active (and the realm of a destroyed window is gone)
        if (!Window.Document.IsFullyActive)
            return;

        if (Window.ScriptEngine is { } engine)
        {
            engine.WaitForAll(Window, promises, successSteps, failureSteps);
            return;
        }

        var rejected = promises.OfType<RejectedPromise>().FirstOrDefault();
        Window.EventLoop.QueueMicrotask(rejected is null ? successSteps : () => failureSteps(rejected.Reason));
    }

    private IDisposable? PrepareToRunScript() => Window.Document.IsFullyActive ? Window.ScriptEngine?.PrepareToRunScript(Window) : null;

    /// <summary>Creates a DOMException in the relevant realm of the navigation (e.g. the realm of the window of an iframe, for a method called by its parent).</summary>
    private DomException CreateException(string message, string name) => new(message, name) { RealmWindow = Window };
}

/// <summary>A serialized state of the navigation API (null is the serialization of undefined), given to the navigation algorithms.</summary>
internal sealed record NavigationApiStateValue(object? Value);

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-navigationhistoryentry-interface</summary>
[IdlInterface("NavigationHistoryEntry", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationhistoryentry")]
public sealed class NavigationHistoryEntry : EventTarget
{
    private readonly Navigation _navigation;

    internal NavigationHistoryEntry(Navigation navigation, SessionHistoryEntry sessionHistoryEntry)
    {
        _navigation = navigation;
        SessionHistoryEntry = sessionHistoryEntry;
    }

    internal SessionHistoryEntry SessionHistoryEntry { get; }

    internal override Window? RelevantGlobal => _navigation.Window;

    private bool IsDocumentFullyActive => _navigation.Window.Document.IsFullyActive;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-url</summary>
    [IdlMember("url", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-url")]
    public string? Url
    {
        get
        {
            var document = _navigation.Window.Document;
            if (!document.IsFullyActive)
                return "";

            // The URL of another document is censored when its request (or, as in the browsers, the document itself) does not send referrers
            var documentState = SessionHistoryEntry.DocumentState;
            if (!ReferenceEquals(SessionHistoryEntry.Document, document)
                && (documentState.RequestReferrerPolicy is "no-referrer" or "origin" || (documentState.Document?.ReferrerPolicy ?? documentState.DocumentReferrerPolicy) is "no-referrer" or "origin"))
            {
                return null;
            }

            return SessionHistoryEntry.Url.Serialize();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-key</summary>
    [IdlMember("key", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-key")]
    public string Key => IsDocumentFullyActive ? SessionHistoryEntry.NavigationApiKey : "";

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-id</summary>
    [IdlMember("id", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-id")]
    public string Id => IsDocumentFullyActive ? SessionHistoryEntry.NavigationApiId : "";

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-index</summary>
    [IdlMember("index", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-index")]
    public long Index => IsDocumentFullyActive ? _navigation.GetEntryIndex(SessionHistoryEntry) : -1;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-samedocument</summary>
    [IdlMember("sameDocument", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-samedocument")]
    public bool SameDocument => IsDocumentFullyActive && ReferenceEquals(SessionHistoryEntry.Document, _navigation.Window.Document);

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-getstate: a new deserialization of the state (null for undefined).</summary>
    [IdlMember("getState", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationhistoryentry-getstate")]
    public object? GetState() => IsDocumentFullyActive ? _navigation.DeserializeState(SessionHistoryEntry.NavigationApiState) : null;

    /// <summary>Gets or sets the event handler of the <c>dispose</c> event.</summary>
    [IdlMember("ondispose", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#handler-navigationhistoryentry-ondispose")]
    public Action<Event>? OnDispose
    {
        get => GetEventHandler("dispose");
        set => SetEventHandler("dispose", value);
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-navigationtransition-interface</summary>
[IdlInterface("NavigationTransition", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationtransition")]
public sealed class NavigationTransition
{
    internal NavigationTransition(NavigationType navigationType, NavigationHistoryEntry from, NavigationDestination to)
    {
        NavigationType = navigationType;
        From = from;
        To = to;
        PromiseSlots.Register(CommittedPromise.Task);
        PromiseSlots.Register(FinishedPromise.Task);
        PromiseSlots.MarkAsHandled(CommittedPromise.Task);
        PromiseSlots.MarkAsHandled(FinishedPromise.Task);
    }

    /// <summary>Gets the type of the ongoing navigation.</summary>
    [IdlMember("navigationType", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationtransition-navigationtype")]
    public NavigationType NavigationType { get; }

    /// <summary>Gets the entry that was current when the navigation started.</summary>
    [IdlMember("from", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationtransition-from")]
    public NavigationHistoryEntry From { get; }

    /// <summary>Gets the destination of the navigation.</summary>
    [IdlMember("to", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationtransition-to")]
    public NavigationDestination To { get; }

    /// <summary>Gets the task that completes when the navigation is committed (the current entry is updated).</summary>
    [IdlMember("committed", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationtransition-committed")]
    public Task Committed => CommittedPromise.Task;

    /// <summary>Gets the task that completes when the navigation has finished.</summary>
    [IdlMember("finished", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationtransition-finished")]
    public Task Finished => FinishedPromise.Task;

    internal TaskCompletionSource CommittedPromise { get; } = new();

    internal TaskCompletionSource FinishedPromise { get; } = new();
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-navigationactivation-interface</summary>
[IdlInterface("NavigationActivation", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationactivation")]
public sealed class NavigationActivation
{
    internal NavigationActivation()
    {
    }

    /// <summary>Gets the entry that was current in the previous document, or null.</summary>
    [IdlMember("from", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationactivation-from")]
    public NavigationHistoryEntry? From { get; internal set; }

    /// <summary>Gets the current entry of the activated document.</summary>
    [IdlMember("entry", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationactivation-entry")]
    public NavigationHistoryEntry? Entry { get; internal set; }

    /// <summary>Gets the type of the navigation that activated the document.</summary>
    [IdlMember("navigationType", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationactivation-navigationtype")]
    public NavigationType NavigationType { get; internal set; }
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-navigationdestination-interface</summary>
[IdlInterface("NavigationDestination", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationdestination")]
public sealed class NavigationDestination
{
    private readonly Navigation _navigation;

    internal NavigationDestination(Navigation navigation, UrlRecord url)
    {
        _navigation = navigation;
        UrlRecord = url;
    }

    internal UrlRecord UrlRecord { get; set; }

    /// <summary>The navigation history entry of a "traverse" navigation, or null.</summary>
    internal NavigationHistoryEntry? Entry { get; init; }

    /// <summary>The serialized state (null for undefined).</summary>
    internal object? State { get; set; }

    internal bool IsSameDocument { get; init; }

    /// <summary>Gets the serialized URL the navigation goes to.</summary>
    [IdlMember("url", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationdestination-url")]
    public string Url => UrlRecord.Serialize();

    /// <summary>Gets the key of the destination entry of a traversal; otherwise an empty string.</summary>
    [IdlMember("key", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationdestination-key")]
    public string Key => Entry?.Key ?? "";

    /// <summary>Gets the ID of the destination entry of a traversal; otherwise an empty string.</summary>
    [IdlMember("id", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationdestination-id")]
    public string Id => Entry?.Id ?? "";

    /// <summary>Gets the index of the destination entry of a traversal; otherwise -1.</summary>
    [IdlMember("index", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationdestination-index")]
    public long Index => Entry?.Index ?? -1;

    /// <summary>Gets a value indicating whether the navigation stays in the current document.</summary>
    [IdlMember("sameDocument", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationdestination-samedocument")]
    public bool SameDocument => IsSameDocument;

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationdestination-getstate</summary>
    [IdlMember("getState", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationdestination-getstate")]
    public object? GetState() => _navigation.DeserializeState(State);
}

/// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-navigationprecommitcontroller-interface</summary>
[IdlInterface("NavigationPrecommitController", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#navigationprecommitcontroller")]
public sealed class NavigationPrecommitController
{
    private readonly NavigateEvent _event;

    internal NavigationPrecommitController(NavigateEvent @event)
    {
        _event = @event;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationprecommitcontroller-redirect</summary>
    [IdlMember("redirect", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationprecommitcontroller-redirect")]
    public void Redirect(string url, NavigationNavigateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        options ??= new NavigationNavigateOptions();
        _event.PerformSharedChecks();
        if (_event.InterceptionState != NavigateEventInterceptionState.Intercepted)
            throw new DomException("The navigation is not intercepted", "InvalidStateError");

        if (_event.NavigationType is not (NavigationType.Push or NavigationType.Replace))
            throw new DomException("Only a push or replace navigation can be redirected", "InvalidStateError");

        var navigation = _event.Navigation!;
        var document = navigation.Window.Document;
        var destinationUrl = UrlParser.Parse(url, document.DocumentBaseUrl) ?? throw new DomException($"'{url}' is not a valid URL", "SyntaxError");
        if (!History.CanHaveUrlRewritten(document.UrlRecord, destinationUrl))
            throw new DomException("The URL of the document cannot be rewritten to this URL", "SecurityError");

        if (options.History is NavigationHistoryBehavior.Push or NavigationHistoryBehavior.Replace)
        {
            _event.NavigationType = options.History == NavigationHistoryBehavior.Push ? NavigationType.Push : NavigationType.Replace;
        }

        if (options.HasState)
        {
            var serializedState = navigation.SerializeState(options.State);
            _event.Destination.State = serializedState;
            if (navigation.OngoingApiMethodTracker is { } tracker)
            {
                tracker.SerializedState = serializedState;
                tracker.HasSerializedState = true;
            }
        }

        _event.Destination.UrlRecord = destinationUrl;
        if (options.Info is { } info)
        {
            _event.Info = info;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationprecommitcontroller-addhandler</summary>
    [IdlMember("addHandler", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-navigationprecommitcontroller-addhandler")]
    public void AddHandler(NavigationInterceptHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _event.PerformSharedChecks();
        if (_event.InterceptionState != NavigateEventInterceptionState.Intercepted)
            throw new DomException("The navigation is not intercepted", "InvalidStateError");

        _event.Handlers.Add(handler);
    }
}
