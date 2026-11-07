namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/interaction.html#closewatcheroptions</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#closewatcheroptions"/>
[IdlInterface("CloseWatcherOptions", "https://html.spec.whatwg.org/multipage/interaction.html#closewatcheroptions")]
public sealed class CloseWatcherOptions
{
    /// <summary>Gets the signal that destroys the close watcher when it is aborted.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcheroptions-signal"/>
    [IdlMember("signal", "https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcheroptions-signal")]
    public AbortSignal? Signal { get; init; }
}

/// <summary>
/// Listens to the close requests of the user (https://html.spec.whatwg.org/multipage/interaction.html#the-closewatcher-interface),
/// e.g. the Escape key: a custom component (a picker, a sidebar) closes itself in the <c>close</c> event, as the dialogs
/// and the popovers do with their own close watchers. A close request closes the close watchers of the most recent group
/// of the window: the close watchers created without user activation are grouped with the previous one, so that a page
/// cannot trap the close requests of the user.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#closewatcher"/>
[IdlInterface("CloseWatcher", "https://html.spec.whatwg.org/multipage/interaction.html#closewatcher")]
public sealed class CloseWatcher : EventTarget
{
    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#internal-close-watcher</summary>
    private readonly InternalCloseWatcher _closeWatcher;

    /// <summary>Creates a close watcher of the window of the current global object (https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher).</summary>
    /// <param name="options">The options, or null.</param>
    /// <exception cref="DomException">There is no current window, or its document is not fully active (<c>InvalidStateError</c>).</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher")]
    public CloseWatcher(CloseWatcherOptions? options = null)
        : this(GlobalScope.Current as Window ?? throw DomException.InvalidState("There is no window to watch the close requests of"), options)
    {
    }

    /// <summary>Creates a close watcher of a window (https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher).</summary>
    /// <param name="window">The window whose close requests are watched.</param>
    /// <param name="options">The options, or null.</param>
    /// <exception cref="DomException">The document of the window is not fully active (<c>InvalidStateError</c>).</exception>
    [NotInIdl("The .NET overload of the constructor whose window is explicit")]
    public CloseWatcher(Window window, CloseWatcherOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        SetRelevantGlobalCore(window);
        if (!window.Document.IsFullyActive)
            throw DomException.InvalidState("The document of the window is not fully active");

        var closeWatcher = InternalCloseWatcher.Establish(
            window,
            cancelAction: canPreventClose => FireEvent(new Event("cancel", new EventInit { Cancelable = canPreventClose })),
            closeAction: () => FireEvent(new Event("close")),
            getEnabledState: static () => true);

        if (options?.Signal is { } signal)
        {
            if (signal.Aborted)
            {
                closeWatcher.Destroy();
            }

            signal.AddAbortAlgorithm(closeWatcher.Destroy);
        }

        _closeWatcher = closeWatcher;
    }

    /// <summary>
    /// Acts as if the user sent a close request to this close watcher: fires <c>cancel</c> (it can always be canceled),
    /// then closes the watcher (https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher-requestclose).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher-requestclose"/>
    [IdlMember("requestClose", "https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher-requestclose")]
    public void RequestClose() => _closeWatcher.RequestClose(requireHistoryActionActivation: false);

    /// <summary>Closes the watcher: fires <c>close</c>, without <c>cancel</c>, and destroys it (https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher-close).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher-close"/>
    [IdlMember("close", "https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher-close")]
    public void Close() => _closeWatcher.Close();

    /// <summary>Destroys the watcher: it no longer receives the close requests and fires no event (https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher-destroy).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher-destroy"/>
    [IdlMember("destroy", "https://html.spec.whatwg.org/multipage/interaction.html#dom-closewatcher-destroy")]
    public void Destroy() => _closeWatcher.Destroy();

    /// <summary>Gets or sets the event handler of the <c>cancel</c> event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#handler-closewatcher-oncancel"/>
    [IdlMember("oncancel", "https://html.spec.whatwg.org/multipage/interaction.html#handler-closewatcher-oncancel")]
    public Action<Event>? OnCancel
    {
        get => GetEventHandler("cancel");
        set => SetEventHandler("cancel", value);
    }

    /// <summary>Gets or sets the event handler of the <c>close</c> event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#handler-closewatcher-onclose"/>
    [IdlMember("onclose", "https://html.spec.whatwg.org/multipage/interaction.html#handler-closewatcher-onclose")]
    public Action<Event>? OnClose
    {
        get => GetEventHandler("close");
        set => SetEventHandler("close", value);
    }
}

/// <summary>
/// The close watcher manager of a window (https://html.spec.whatwg.org/multipage/interaction.html#close-watcher-manager):
/// the groups of the active close watchers. A close request closes the watchers of the last group; a new group is only
/// allowed after a user activation, so that the scripts cannot stack the close watchers to trap the close requests.
/// </summary>
internal sealed class CloseWatcherManager
{
    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#close-watcher-manager-groups</summary>
    public List<List<InternalCloseWatcher>> Groups { get; } = [];

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#close-watcher-manager-allowed-number-of-groups</summary>
    public int AllowedNumberOfGroups { get; set; } = 1;

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#close-watcher-manager-next-user-interaction-allows-a-new-group</summary>
    public bool NextUserInteractionAllowsNewGroup { get; set; } = true;

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#notify-the-close-watcher-manager-about-user-activation</summary>
    public void NotifyAboutUserActivation()
    {
        if (NextUserInteractionAllowsNewGroup)
        {
            AllowedNumberOfGroups++;
        }

        NextUserInteractionAllowsNewGroup = false;
    }
}

/// <summary>
/// A close watcher (https://html.spec.whatwg.org/multipage/interaction.html#close-watcher): the one of a
/// <see cref="CloseWatcher"/> object, of an open dialog or of a showing auto or hint popover.
/// </summary>
internal sealed class InternalCloseWatcher
{
    private readonly Window _window;
    private readonly Func<bool, bool> _cancelAction;
    private readonly Action _closeAction;
    private readonly Func<bool> _getEnabledState;
    private bool _isRunningCancelAction;

    private InternalCloseWatcher(Window window, Func<bool, bool> cancelAction, Action closeAction, Func<bool> getEnabledState)
    {
        _window = window;
        _cancelAction = cancelAction;
        _closeAction = closeAction;
        _getEnabledState = getEnabledState;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#close-watcher-get-enabled-state</summary>
    public bool IsEnabled => _getEnabledState();

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#close-watcher-active</summary>
    public bool IsActive
    {
        get
        {
            foreach (var group in _window.CloseWatcherManager.Groups)
            {
                if (group.Contains(this))
                    return true;
            }

            return false;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#establish-a-close-watcher</summary>
    /// <param name="window">The window, whose document is fully active.</param>
    /// <param name="cancelAction">The cancel action: its argument is whether it can prevent the close, and it returns false to prevent it.</param>
    /// <param name="closeAction">The close action.</param>
    /// <param name="getEnabledState">Whether the close watcher handles the close requests.</param>
    public static InternalCloseWatcher Establish(Window window, Func<bool, bool> cancelAction, Action closeAction, Func<bool> getEnabledState)
    {
        var closeWatcher = new InternalCloseWatcher(window, cancelAction, closeAction, getEnabledState);
        var manager = window.CloseWatcherManager;
        if (manager.Groups.Count < manager.AllowedNumberOfGroups)
        {
            manager.Groups.Add([closeWatcher]);
        }
        else
        {
            // The watcher is grouped with the previous ones: a close request closes them all
            manager.Groups[^1].Add(closeWatcher);
        }

        manager.NextUserInteractionAllowsNewGroup = true;
        return closeWatcher;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#close-watcher-request-close: the cancel action, then the
    /// watcher is closed. Returns false when the cancel action prevented the close.
    /// </summary>
    /// <param name="requireHistoryActionActivation">Whether the cancel action can only prevent the close when the window has history-action activation (a close request of the user).</param>
    public bool RequestClose(bool requireHistoryActionActivation)
    {
        if (!IsActive || !IsEnabled || _isRunningCancelAction)
            return true;

        if (!_window.Document.IsFullyActive)
            return true;

        var manager = _window.CloseWatcherManager;
        var canPreventClose = !requireHistoryActionActivation || (manager.Groups.Count < manager.AllowedNumberOfGroups && _window.HasHistoryActionActivation);
        _isRunningCancelAction = true;
        bool shouldContinue;
        try
        {
            shouldContinue = _cancelAction(canPreventClose);
        }
        finally
        {
            _isRunningCancelAction = false;
        }

        if (!shouldContinue)
        {
            // A second close request without user activation in between cannot be prevented
            _window.ConsumeHistoryActionUserActivation();
            return false;
        }

        Close();
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#close-watcher-close</summary>
    public void Close()
    {
        if (!IsActive || !IsEnabled || !_window.Document.IsFullyActive)
            return;

        Destroy();
        _closeAction();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#close-watcher-destroy</summary>
    public void Destroy()
    {
        var groups = _window.CloseWatcherManager.Groups;
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            groups[i].Remove(this);
            if (groups[i].Count == 0)
            {
                groups.RemoveAt(i);
            }
        }
    }
}

public partial class Window
{
    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#close-watcher-manager</summary>
    internal CloseWatcherManager CloseWatcherManager { get; } = new();

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#process-close-watchers: a close request of the user (e.g. the
    /// Escape key) requests to close the close watchers of the last group, from the most recent one, until one prevents it.
    /// Returns false when no enabled close watcher was processed.
    /// </summary>
    internal bool ProcessCloseWatchers()
    {
        var manager = CloseWatcherManager;
        var processedACloseWatcher = false;
        if (manager.Groups.Count > 0)
        {
            // The actions run scripts that can create and destroy close watchers: iterate over a copy of the group
            var group = manager.Groups[^1].ToArray();
            for (var i = group.Length - 1; i >= 0; i--)
            {
                var closeWatcher = group[i];
                if (closeWatcher.IsEnabled)
                {
                    processedACloseWatcher = true;
                }

                if (!closeWatcher.RequestClose(requireHistoryActionActivation: true))
                    break;
            }
        }

        if (manager.AllowedNumberOfGroups > 1)
        {
            manager.AllowedNumberOfGroups--;
        }

        return processedACloseWatcher;
    }
}
