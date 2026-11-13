using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Whether the user allows the origin to show notifications (https://notifications.spec.whatwg.org/#enumdef-notificationpermission).</summary>
/// <seealso href="https://notifications.spec.whatwg.org/#enumdef-notificationpermission"/>
[IdlInterface("NotificationPermission", "https://notifications.spec.whatwg.org/#enumdef-notificationpermission")]
public enum NotificationPermission
{
    /// <summary>The user was not asked: the notifications are not shown.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationpermission-default"/>
    [IdlMember("default", "https://notifications.spec.whatwg.org/#dom-notificationpermission-default")]
    Default,

    /// <summary>The user denied the permission.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationpermission-denied"/>
    [IdlMember("denied", "https://notifications.spec.whatwg.org/#dom-notificationpermission-denied")]
    Denied,

    /// <summary>The user granted the permission.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationpermission-granted"/>
    [IdlMember("granted", "https://notifications.spec.whatwg.org/#dom-notificationpermission-granted")]
    Granted,
}

/// <summary>The direction of the text of a notification (https://notifications.spec.whatwg.org/#enumdef-notificationdirection).</summary>
/// <seealso href="https://notifications.spec.whatwg.org/#enumdef-notificationdirection"/>
[IdlInterface("NotificationDirection", "https://notifications.spec.whatwg.org/#enumdef-notificationdirection")]
public enum NotificationDirection
{
    /// <summary>The direction is determined by the content.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationdirection-auto"/>
    [IdlMember("auto", "https://notifications.spec.whatwg.org/#dom-notificationdirection-auto")]
    Auto,

    /// <summary>Left to right.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationdirection-ltr"/>
    [IdlMember("ltr", "https://notifications.spec.whatwg.org/#dom-notificationdirection-ltr")]
    Ltr,

    /// <summary>Right to left.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationdirection-rtl"/>
    [IdlMember("rtl", "https://notifications.spec.whatwg.org/#dom-notificationdirection-rtl")]
    Rtl,
}

/// <summary>The callback of <c>Notification.requestPermission()</c> (https://notifications.spec.whatwg.org/#callbackdef-notificationpermissioncallback).</summary>
/// <seealso href="https://notifications.spec.whatwg.org/#callbackdef-notificationpermissioncallback"/>
[IdlInterface("NotificationPermissionCallback", "https://notifications.spec.whatwg.org/#callbackdef-notificationpermissioncallback")]
public delegate void NotificationPermissionCallback(NotificationPermission permission);

/// <summary>An action of a persistent notification (https://notifications.spec.whatwg.org/#dictdef-notificationaction): a button that the user can activate (see <see cref="Notification.Click(string)"/>).</summary>
/// <seealso href="https://notifications.spec.whatwg.org/#dictdef-notificationaction"/>
[IdlInterface("NotificationAction", "https://notifications.spec.whatwg.org/#dictdef-notificationaction")]
public sealed class NotificationAction
{
    /// <summary>Gets the name of the action.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationaction-action"/>
    [IdlMember("action", "https://notifications.spec.whatwg.org/#dom-notificationaction-action")]
    public required string Action { get; init; }

    /// <summary>Gets the title of the action.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationaction-title"/>
    [IdlMember("title", "https://notifications.spec.whatwg.org/#dom-notificationaction-title")]
    public required string Title { get; init; }

    /// <summary>Gets the URL to navigate to when the action is activated.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationaction-navigate"/>
    [IdlMember("navigate", "https://notifications.spec.whatwg.org/#dom-notificationaction-navigate")]
    public string? Navigate { get; init; }

    /// <summary>Gets the URL of the icon of the action.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationaction-icon"/>
    [IdlMember("icon", "https://notifications.spec.whatwg.org/#dom-notificationaction-icon")]
    public string? Icon { get; init; }
}

/// <summary>The options of a notification (https://notifications.spec.whatwg.org/#dictdef-notificationoptions).</summary>
/// <seealso href="https://notifications.spec.whatwg.org/#dictdef-notificationoptions"/>
[IdlInterface("NotificationOptions", "https://notifications.spec.whatwg.org/#dictdef-notificationoptions")]
public sealed class NotificationOptions
{
    /// <summary>Gets the direction of the text.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-dir"/>
    [IdlMember("dir", "https://notifications.spec.whatwg.org/#dom-notificationoptions-dir")]
    public NotificationDirection Dir { get; init; }

    /// <summary>Gets the language of the text (a BCP 47 language tag, or the empty string).</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-lang"/>
    [IdlMember("lang", "https://notifications.spec.whatwg.org/#dom-notificationoptions-lang")]
    public string Lang { get; init; } = "";

    /// <summary>Gets the body of the notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-body"/>
    [IdlMember("body", "https://notifications.spec.whatwg.org/#dom-notificationoptions-body")]
    public string Body { get; init; } = "";

    /// <summary>Gets the URL to navigate to when the notification is activated.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-navigate"/>
    [IdlMember("navigate", "https://notifications.spec.whatwg.org/#dom-notificationoptions-navigate")]
    public string? Navigate { get; init; }

    /// <summary>Gets the tag of the notification: a notification replaces the notification of the same origin with the same tag.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-tag"/>
    [IdlMember("tag", "https://notifications.spec.whatwg.org/#dom-notificationoptions-tag")]
    public string Tag { get; init; } = "";

    /// <summary>Gets the URL of the image of the notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-image"/>
    [IdlMember("image", "https://notifications.spec.whatwg.org/#dom-notificationoptions-image")]
    public string? Image { get; init; }

    /// <summary>Gets the URL of the icon of the notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-icon"/>
    [IdlMember("icon", "https://notifications.spec.whatwg.org/#dom-notificationoptions-icon")]
    public string? Icon { get; init; }

    /// <summary>Gets the URL of the badge of the notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-badge"/>
    [IdlMember("badge", "https://notifications.spec.whatwg.org/#dom-notificationoptions-badge")]
    public string? Badge { get; init; }

    /// <summary>Gets the time of the notification, in milliseconds since the Unix epoch (the current time by default).</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-timestamp"/>
    [IdlMember("timestamp", "https://notifications.spec.whatwg.org/#dom-notificationoptions-timestamp")]
    public ulong? Timestamp { get; init; }

    /// <summary>Gets whether the user is alerted again when the notification replaces another one.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-renotify"/>
    [IdlMember("renotify", "https://notifications.spec.whatwg.org/#dom-notificationoptions-renotify")]
    public bool Renotify { get; init; }

    /// <summary>Gets whether the notification is silent (no sound nor vibration), or null for the default of the device.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-silent"/>
    [IdlMember("silent", "https://notifications.spec.whatwg.org/#dom-notificationoptions-silent")]
    public bool? Silent { get; init; }

    /// <summary>Gets whether the notification stays until the user activates or closes it.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-requireinteraction"/>
    [IdlMember("requireInteraction", "https://notifications.spec.whatwg.org/#dom-notificationoptions-requireinteraction")]
    public bool RequireInteraction { get; init; }

    /// <summary>Gets the data of the notification (a value that can be serialized by the structured clone algorithm).</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-data"/>
    [IdlMember("data", "https://notifications.spec.whatwg.org/#dom-notificationoptions-data")]
    public object? Data { get; init; }

    /// <summary>Gets the actions of the notification: only a persistent notification can have actions (at most <see cref="Notification.MaxActions"/>, the next ones are ignored).</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-actions"/>
    [IdlMember("actions", "https://notifications.spec.whatwg.org/#dom-notificationoptions-actions")]
    public IReadOnlyList<NotificationAction> Actions { get; init; } = [];
}

/// <summary>The filter of <c>ServiceWorkerRegistration.getNotifications()</c> (https://notifications.spec.whatwg.org/#dictdef-getnotificationoptions).</summary>
/// <seealso href="https://notifications.spec.whatwg.org/#dictdef-getnotificationoptions"/>
[IdlInterface("GetNotificationOptions", "https://notifications.spec.whatwg.org/#dictdef-getnotificationoptions")]
public sealed class GetNotificationOptions
{
    /// <summary>Gets the tag of the notifications to return, or the empty string for all the notifications.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-getnotificationoptions-tag"/>
    [IdlMember("tag", "https://notifications.spec.whatwg.org/#dom-getnotificationoptions-tag")]
    public string Tag { get; init; } = "";
}

/// <summary>
/// A notification shown to the user (https://notifications.spec.whatwg.org/#notification), by
/// <see cref="BrowsingContextOptions.ShowNotification"/> once the <c>notifications</c> permission is granted. The host
/// calls <see cref="Click()"/> when the user activates the notification and <see cref="Dismiss"/> when the user closes it.
/// </summary>
/// <remarks>
/// <para>
/// A non-persistent notification is created by the constructor: the <c>error</c> event is fired when the permission is
/// not granted, and its <c>show</c>, <c>click</c> and <c>close</c> events are fired at the object. A persistent notification
/// is created by <see cref="ServiceWorkerRegistration.ShowNotification"/>: it belongs to the service worker registration,
/// whose active worker receives its <c>notificationclick</c> and <c>notificationclose</c> events
/// (<see cref="NotificationEvent"/>), and <see cref="ServiceWorkerRegistration.GetNotifications"/> returns new objects that
/// represent it. Only the persistent notifications have actions (at most <see cref="MaxActions"/>).
/// </para>
/// <para>
/// A notification whose tag is not empty replaces the notification of the same origin with the same tag (the
/// <c>close</c> event of a replaced non-persistent notification is fired). The resources of the notifications (the icon,
/// the image, the badge) are not fetched: the host fetches them if it displays them.
/// </para>
/// </remarks>
/// <seealso href="https://notifications.spec.whatwg.org/#notification"/>
[IdlInterface("Notification", "https://notifications.spec.whatwg.org/#notification")]
public sealed class Notification : EventTarget
{
    /// <summary>The maximum number of actions of a notification (as in Chromium).</summary>
    internal const int MaxActionsCount = 2;

    private const string PermissionName = "notifications";

    private readonly GlobalScope _global;
    private object? _data;
    private bool _isDataDeserialized;
    private bool _closeRequested;

    /// <summary>
    /// Creates a notification and shows it (https://notifications.spec.whatwg.org/#dom-notification-notification): the
    /// <c>show</c> event is fired once it is shown, or the <c>error</c> event when the permission is not granted.
    /// </summary>
    /// <param name="title">The title of the notification.</param>
    /// <param name="options">The options of the notification.</param>
    /// <exception cref="TypeErrorException">The constructor is called in a service worker, the options have actions, or <c>renotify</c> is set without tag.</exception>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-notification"/>
    [IdlMember("constructor", "https://notifications.spec.whatwg.org/#dom-notification-notification")]
    public Notification(string title, NotificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        options ??= new NotificationOptions();
        var global = GlobalScope.Current ?? throw new InvalidOperationException("There is no current global object");
        if (global is ServiceWorkerGlobalScope)
            throw new TypeErrorException("Notifications cannot be constructed in a service worker");

        if (options.Actions.Count > 0)
            throw new TypeErrorException("Only persistent notifications can have actions");

        _global = global;
        Record = NotificationRecord.Create(global, title, options, registration: null);
        Record.HostObject = this;

        // https://wicg.github.io/nav-speculation/prerendering.html#patch-notifications: a prerendering document shows its notifications once activated
        if (global is Window { Document: { Prerendering: true } document })
        {
            document.RunAfterPrerendering(() => global.QueueGlobalTask(Show));
        }
        else
        {
            global.QueueGlobalTask(Show);
        }
    }

    /// <summary>Creates an object that represents a notification in the realm of <paramref name="global"/>.</summary>
    internal Notification(GlobalScope global, NotificationRecord record)
    {
        _global = global;
        Record = record;
    }

    internal override GlobalScope? RelevantGlobal => _global;

    /// <summary>The notification that the object represents.</summary>
    internal NotificationRecord Record { get; }

    /// <summary>Gets whether the notification is a persistent notification: it was shown by <see cref="ServiceWorkerRegistration.ShowNotification"/>.</summary>
    [NotInIdl]
    public bool IsPersistent => Record.Registration is not null;

    /// <summary>
    /// Gets whether the user allows the origin of the current global object to show notifications
    /// (https://notifications.spec.whatwg.org/#dom-notification-permission), read from the permission store
    /// (<see cref="BrowsingContextOptions.Permissions"/>).
    /// </summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-permission"/>
    [IdlMember("permission", "https://notifications.spec.whatwg.org/#dom-notification-permission")]
    public static NotificationPermission Permission => GlobalScope.Current switch
    {
        // https://wicg.github.io/nav-speculation/prerendering.html#patch-notifications
        Window { Document.Prerendering: true } => NotificationPermission.Default,
        { } global => ToNotificationPermission(GetPermissionState(global)),
        _ => NotificationPermission.Denied,
    };

    /// <summary>Gets the maximum number of actions of a notification (https://notifications.spec.whatwg.org/#dom-notification-maxactions): 2, as in Chromium. The actions after the first two are ignored.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-maxactions"/>
    [IdlMember("maxActions", "https://notifications.spec.whatwg.org/#dom-notification-maxactions")]
    public static uint MaxActions => MaxActionsCount;

    /// <summary>
    /// Asks the user to allow the origin to show notifications (https://notifications.spec.whatwg.org/#dom-notification-requestpermission),
    /// with <see cref="BrowsingContextOptions.RequestPermission"/>, unless the permission is already granted or denied.
    /// </summary>
    /// <param name="deprecatedCallback">A callback invoked with the permission, before the task completes.</param>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-requestpermission"/>
    [IdlMember("requestPermission", "https://notifications.spec.whatwg.org/#dom-notification-requestpermission")]
    public static Task<NotificationPermission> RequestPermission(NotificationPermissionCallback? deprecatedCallback = null)
    {
        var global = GlobalScope.Current ?? throw new InvalidOperationException("There is no current global object");

        // [DelayWhilePrerendering] (https://wicg.github.io/nav-speculation/prerendering.html#patch-notifications)
        return Document.DelayWhilePrerendering(global, () => RequestPermissionCore(global, deprecatedCallback));
    }

    private static Task<NotificationPermission> RequestPermissionCore(GlobalScope global, NotificationPermissionCallback? deprecatedCallback)
    {
        var completion = new TaskCompletionSource<NotificationPermission>();
        global.QueueGlobalTask(() =>
        {
            var permission = ToNotificationPermission(IsThirdPartyContext(global) ? PermissionState.Denied : Permissions.RequestPermissionToUse(global, PermissionName));
            if (deprecatedCallback is not null)
            {
                try
                {
                    deprecatedCallback(permission);
                }
#pragma warning disable CA1031 // The exceptions of the callbacks are reported
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    ScriptErrors.Report(ex, global);
                }
            }

            completion.TrySetResult(permission);
        });
        return completion.Task;
    }

    /// <summary>Gets or sets the event handler of the <c>click</c> event.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-onclick"/>
    [IdlMember("onclick", "https://notifications.spec.whatwg.org/#dom-notification-onclick")]
    public Action<Event>? OnClick
    {
        get => GetEventHandler("click");
        set => SetEventHandler("click", value);
    }

    /// <summary>Gets or sets the event handler of the <c>show</c> event.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-onshow"/>
    [IdlMember("onshow", "https://notifications.spec.whatwg.org/#dom-notification-onshow")]
    public Action<Event>? OnShow
    {
        get => GetEventHandler("show");
        set => SetEventHandler("show", value);
    }

    /// <summary>Gets or sets the event handler of the <c>error</c> event.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-onerror"/>
    [IdlMember("onerror", "https://notifications.spec.whatwg.org/#dom-notification-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>Gets or sets the event handler of the <c>close</c> event.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-onclose"/>
    [IdlMember("onclose", "https://notifications.spec.whatwg.org/#dom-notification-onclose")]
    public Action<Event>? OnClose
    {
        get => GetEventHandler("close");
        set => SetEventHandler("close", value);
    }

    /// <summary>Gets the title of the notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-title"/>
    [IdlMember("title", "https://notifications.spec.whatwg.org/#dom-notification-title")]
    public string Title => Record.Title;

    /// <summary>Gets the direction of the text.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-dir"/>
    [IdlMember("dir", "https://notifications.spec.whatwg.org/#dom-notification-dir")]
    public NotificationDirection Dir => Record.Dir;

    /// <summary>Gets the language of the text.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-lang"/>
    [IdlMember("lang", "https://notifications.spec.whatwg.org/#dom-notification-lang")]
    public string Lang => Record.Lang;

    /// <summary>Gets the body of the notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-body"/>
    [IdlMember("body", "https://notifications.spec.whatwg.org/#dom-notification-body")]
    public string Body => Record.Body;

    /// <summary>Gets the absolute URL to navigate to when the notification is activated, or the empty string.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-navigate"/>
    [IdlMember("navigate", "https://notifications.spec.whatwg.org/#dom-notification-navigate")]
    public string Navigate => Record.NavigationUrl?.Serialize() ?? "";

    /// <summary>Gets the tag of the notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-tag"/>
    [IdlMember("tag", "https://notifications.spec.whatwg.org/#dom-notification-tag")]
    public string Tag => Record.Tag;

    /// <summary>Gets the absolute URL of the image, or the empty string.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-image"/>
    [IdlMember("image", "https://notifications.spec.whatwg.org/#dom-notification-image")]
    public string Image => Record.ImageUrl?.Serialize() ?? "";

    /// <summary>Gets the absolute URL of the icon, or the empty string.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-icon"/>
    [IdlMember("icon", "https://notifications.spec.whatwg.org/#dom-notification-icon")]
    public string Icon => Record.IconUrl?.Serialize() ?? "";

    /// <summary>Gets the absolute URL of the badge, or the empty string.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-badge"/>
    [IdlMember("badge", "https://notifications.spec.whatwg.org/#dom-notification-badge")]
    public string Badge => Record.BadgeUrl?.Serialize() ?? "";

    /// <summary>Gets the time of the notification, in milliseconds since the Unix epoch.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-timestamp"/>
    [IdlMember("timestamp", "https://notifications.spec.whatwg.org/#dom-notification-timestamp")]
    public ulong Timestamp => Record.Timestamp;

    /// <summary>Gets whether the user is alerted again when the notification replaces another one.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-renotify"/>
    [IdlMember("renotify", "https://notifications.spec.whatwg.org/#dom-notification-renotify")]
    public bool Renotify => Record.Renotify;

    /// <summary>Gets whether the notification is silent, or null for the default of the device.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-silent"/>
    [IdlMember("silent", "https://notifications.spec.whatwg.org/#dom-notification-silent")]
    public bool? Silent => Record.Silent;

    /// <summary>Gets whether the notification stays until the user activates or closes it.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-requireinteraction"/>
    [IdlMember("requireInteraction", "https://notifications.spec.whatwg.org/#dom-notification-requireinteraction")]
    public bool RequireInteraction => Record.RequireInteraction;

    /// <summary>Gets a deserialization of the data of the notification (the same value each time for an object).</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-data"/>
    [IdlMember("data", "https://notifications.spec.whatwg.org/#dom-notification-data")]
    public object? Data
    {
        get
        {
            if (!_isDataDeserialized)
            {
                var serializedData = Record.SerializedData;
                _data = serializedData is null ? null : _global.ScriptEngine is { } engine ? engine.StructuredDeserialize(_global, serializedData) : StructuredClone.Deserialize(serializedData);
                _isDataDeserialized = true;
            }

            return _data;
        }
    }

    /// <summary>Gets the actions of the notification (https://notifications.spec.whatwg.org/#dom-notification-actions): always empty for a non-persistent notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-actions"/>
    [IdlMember("actions", "https://notifications.spec.whatwg.org/#dom-notification-actions")]
    public IReadOnlyList<NotificationAction> Actions => [.. Record.Actions.Select(static action => new NotificationAction
    {
        Action = action.Name,
        Title = action.Title,
        Navigate = action.NavigationUrl?.Serialize(),
        Icon = action.IconUrl?.Serialize(),
    })];

    /// <summary>Gets whether the notification is shown: it is in the list of notifications of the browsing contexts.</summary>
    [NotInIdl]
    public bool IsShown
    {
        get
        {
            var list = _global.Options.ShownNotifications;
            lock (list)
            {
                return list.Contains(Record);
            }
        }
    }

    /// <summary>
    /// Closes the notification (https://notifications.spec.whatwg.org/#dom-notification-close): the <c>close</c> event of a
    /// non-persistent notification is fired. Closing a persistent notification fires no event.
    /// </summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-close"/>
    [IdlMember("close", "https://notifications.spec.whatwg.org/#dom-notification-close")]
    public void Close()
    {
        if (IsPersistent)
        {
            if (Remove(_global.Options, Record))
            {
                InvokeHost(Record, _global.Options.CloseNotification);
            }

            return;
        }

        // A notification closed before it is shown is never shown (https://notifications.spec.whatwg.org/#show-steps)
        _closeRequested = true;
        if (RunCloseSteps())
        {
            InvokeHost(Record, _global.Options.CloseNotification);
        }
    }

    /// <summary>
    /// Activates the notification, as when the user clicks it (https://notifications.spec.whatwg.org/#activating-a-notification):
    /// fires the <c>click</c> event of a non-persistent notification, and focuses its window unless the event is canceled. It
    /// must be called on the thread of the event loop of the browsing context. For a persistent notification, fires the
    /// <c>notificationclick</c> event at the active worker of its registration (from any thread).
    /// </summary>
    [NotInIdl]
    public void Click()
    {
        if (IsPersistent)
        {
            FireServiceWorkerNotificationEvent("notificationclick", Record, action: "");
            return;
        }

        if (!FireEvent(new Event("click", new EventInit { Cancelable = true })))
            return;

        if (_global is Window window && window.Document.IsFullyActive)
        {
            window.Focus();
        }
    }

    /// <summary>
    /// Activates an action of a persistent notification, as when the user clicks its button (https://notifications.spec.whatwg.org/#activating-a-notification):
    /// fires the <c>notificationclick</c> event, whose <c>action</c> is the name of the action, at the active worker of its registration.
    /// </summary>
    /// <param name="action">The name of the action (<see cref="NotificationAction.Action"/>).</param>
    /// <exception cref="InvalidOperationException">The notification is not persistent.</exception>
    /// <exception cref="ArgumentException">The notification has no action with this name.</exception>
    [NotInIdl]
    public void Click(string action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsPersistent)
            throw new InvalidOperationException("Only the persistent notifications have actions");

        if (!Record.Actions.Any(entry => entry.Name == action))
            throw new ArgumentException($"The notification has no action named '{action}'", nameof(action));

        FireServiceWorkerNotificationEvent("notificationclick", Record, action);
    }

    /// <summary>
    /// Closes the notification, as when the user dismisses it (https://notifications.spec.whatwg.org/#closing-a-notification):
    /// fires the <c>close</c> event of a non-persistent notification (it must be called on the thread of the event loop of
    /// the browsing context), or the <c>notificationclose</c> event at the active worker of the registration of a persistent
    /// notification. <see cref="BrowsingContextOptions.CloseNotification"/> is not called.
    /// </summary>
    [NotInIdl]
    public void Dismiss()
    {
        if (IsPersistent)
        {
            if (Remove(_global.Options, Record))
            {
                FireServiceWorkerNotificationEvent("notificationclose", Record, action: "");
            }

            return;
        }

        RunCloseSteps();
    }

    /// <summary>
    /// The steps of <c>showNotification()</c> after the active worker check (https://notifications.spec.whatwg.org/#dom-serviceworkerregistration-shownotification):
    /// <paramref name="global"/> is the relevant global object of the registration object.
    /// </summary>
    internal static Task ShowPersistentNotification(GlobalScope global, ServiceWorkerRegistrationRecord registration, string title, NotificationOptions options)
    {
        NotificationRecord record;
        try
        {
            record = NotificationRecord.Create(global, title, options, registration);
        }
        catch (Exception ex) when (ex is TypeErrorException or DomException)
        {
            return Task.FromException(ex);
        }

        // The host receives an object of the realm that showed the notification
        record.HostObject = new Notification(global, record);
        var promise = new TaskCompletionSource();
        global.QueueGlobalTask(() =>
        {
            if (GetPermissionState(global) != PermissionState.Granted)
            {
                promise.TrySetException(new TypeErrorException("The notifications permission is not granted"));
                return;
            }

            ShowCore(record, global.Options);
            promise.TrySetResult();
        });
        return promise.Task;
    }

    /// <summary>https://notifications.spec.whatwg.org/#dom-serviceworkerregistration-getnotifications</summary>
    internal static Task<IReadOnlyList<Notification>> GetPersistentNotifications(GlobalScope global, ServiceWorkerRegistrationRecord registration, string tag)
    {
        var origin = global.EnvironmentDocument.Origin.Serialize();
        var promise = new TaskCompletionSource<IReadOnlyList<Notification>>();
        global.QueueGlobalTask(() =>
        {
            NotificationRecord[] records;
            var list = global.Options.ShownNotifications;
            lock (list)
            {
                records = [.. list.Where(record => record.Registration == registration && record.Origin == origin && (tag.Length == 0 || record.Tag == tag))];
            }

            promise.TrySetResult([.. records.Select(record => new Notification(global, record))]);
        });
        return promise.Task;
    }

    /// <summary>Closes the persistent notifications of a registration that is unregistered (the host callbacks are invoked by a task of <paramref name="eventLoop"/>).</summary>
    internal static void CloseRegistrationNotifications(BrowsingContextOptions options, ServiceWorkerRegistrationRecord registration, DomEventLoop eventLoop)
    {
        var list = options.ShownNotifications;
        List<NotificationRecord> removed;
        lock (list)
        {
            removed = [.. list.Where(record => record.Registration == registration)];
            if (removed.Count == 0)
                return;

            list.RemoveAll(record => record.Registration == registration);
        }

        if (options.CloseNotification is { } closeNotification)
        {
            eventLoop.QueueTask(() =>
            {
                foreach (var record in removed)
                {
                    InvokeHost(record, closeNotification);
                }
            });
        }
    }

    /// <summary>https://notifications.spec.whatwg.org/#show-steps (the steps of a non-persistent notification)</summary>
    private void Show()
    {
        if (GetPermissionState(_global) != PermissionState.Granted)
        {
            FireEvent(new Event("error"));
            return;
        }

        if (_closeRequested)
        {
            FireEvent(new Event("close"));
            return;
        }

        ShowCore(Record, _global.Options);

        // The show event is fired by a task, after the close event of the replaced notification
        _global.QueueGlobalTask(() => FireEvent(new Event("show")));
    }

    /// <summary>
    /// https://notifications.spec.whatwg.org/#show-steps: adds the notification to the list of notifications, where it replaces
    /// the notification of the same origin with the same tag, and shows it with the host.
    /// </summary>
    private static void ShowCore(NotificationRecord record, BrowsingContextOptions options)
    {
        NotificationRecord? oldRecord = null;
        var list = options.ShownNotifications;
        lock (list)
        {
            if (record.Tag.Length > 0)
            {
                var index = list.FindIndex(notification => notification.Tag == record.Tag && notification.Origin == record.Origin);
                if (index >= 0)
                {
                    oldRecord = list[index];
                    list[index] = record;
                }
            }

            if (oldRecord is null)
            {
                list.Add(record);
            }
        }

        if (oldRecord is not null)
        {
            // https://notifications.spec.whatwg.org/#handle-close-events: a replaced notification is not closed by the user
            InvokeHost(oldRecord, options.CloseNotification);
            if (oldRecord.Registration is null)
            {
                oldRecord.HostObject?.QueueCloseEvent();
            }
        }

        InvokeHost(record, options.ShowNotification);
    }

    /// <summary>Removes a notification from the list of notifications: returns false when it was not shown.</summary>
    private static bool Remove(BrowsingContextOptions options, NotificationRecord record)
    {
        var list = options.ShownNotifications;
        lock (list)
        {
            return list.Remove(record);
        }
    }

    /// <summary>https://notifications.spec.whatwg.org/#close-steps: returns false when the notification was not shown.</summary>
    private bool RunCloseSteps()
    {
        if (!Remove(_global.Options, Record))
            return false;

        QueueCloseEvent();
        return true;
    }

    /// <summary>https://notifications.spec.whatwg.org/#handle-close-events</summary>
    private void QueueCloseEvent() => _global.QueueGlobalTask(() => FireEvent(new Event("close")));

    /// <summary>
    /// https://notifications.spec.whatwg.org/#fire-a-service-worker-notification-event: fires a functional event at the
    /// active worker of the registration of a persistent notification, with a new object that represents the notification.
    /// </summary>
    private static void FireServiceWorkerNotificationEvent(string name, NotificationRecord record, string action)
    {
        var registration = record.Registration!;
        registration.Registry.FireFunctionalEvent(registration, name, global => new NotificationEvent(name, new NotificationEventInit { Notification = new Notification(global, record), Action = action }));
    }

    private static void InvokeHost(NotificationRecord record, Action<CallbackContext, Notification>? callback)
    {
        if (callback is not null && record.HostObject is { } notification)
        {
            callback(CallbackContext.For(notification._global), notification);
        }
    }

    /// <summary>
    /// https://notifications.spec.whatwg.org/#get-the-notifications-permission-state: as in the browsers, the documents that
    /// are not same origin with all their ancestors (and their workers, and the service workers that they registered) cannot
    /// show notifications.
    /// </summary>
    private static PermissionState GetPermissionState(GlobalScope global) => IsThirdPartyContext(global) ? PermissionState.Denied : Permissions.GetPermissionState(global, PermissionName);

    private static bool IsThirdPartyContext(GlobalScope global)
    {
        // A service worker registered by a third-party context has a partitioned storage key
        if (global is ServiceWorkerGlobalScope serviceWorker)
            return !serviceWorker.ServiceWorkerRecord.Registration.StorageKey.IsFirstParty;

        // A dedicated worker has the context of the document that created it
        while (global is WorkerGlobalScope { Agent.Owner: { } owner })
        {
            global = owner;
        }

        var document = global.EnvironmentDocument;
        for (var context = document.BrowsingContext; context?.Parent is { } parent; context = parent)
        {
            if (!Document.IsSameOrigin(parent.Document, document))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether a language tag is the empty string or a valid BCP 47 language tag (https://www.rfc-editor.org/rfc/rfc5646#section-2.2.9),
    /// without the registry: a well-formed tag whose language subtag has 2 or 3 letters (the 4-letter subtags are reserved,
    /// and no 5-to-8-letter subtag is registered), with at most one extended language subtag, and without duplicate variants
    /// nor extensions.
    /// </summary>
    internal static bool IsValidLanguageTag(string tag)
    {
        if (tag.Length == 0)
            return true;

        var subtags = tag.Split('-');
        foreach (var subtag in subtags)
        {
            if (subtag.Length is 0 or > 8 || !subtag.All(char.IsAsciiLetterOrDigit))
                return false;
        }

        var index = 0;
        if (!IsPrivateUse(subtags[index]))
        {
            // language ["-" extlang]
            if (subtags[index].Length is < 2 or > 3 || !subtags[index].All(char.IsAsciiLetter))
                return false;

            index++;
            if (index < subtags.Length && subtags[index].Length == 3 && subtags[index].All(char.IsAsciiLetter))
            {
                index++;
            }

            // ["-" script]
            if (index < subtags.Length && subtags[index].Length == 4 && subtags[index].All(char.IsAsciiLetter))
            {
                index++;
            }

            // ["-" region]
            if (index < subtags.Length && ((subtags[index].Length == 2 && subtags[index].All(char.IsAsciiLetter)) || (subtags[index].Length == 3 && subtags[index].All(char.IsAsciiDigit))))
            {
                index++;
            }

            // *("-" variant)
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (index < subtags.Length && (subtags[index].Length >= 5 || (subtags[index].Length == 4 && char.IsAsciiDigit(subtags[index][0]))))
            {
                if (!variants.Add(subtags[index]))
                    return false;

                index++;
            }

            // *("-" extension)
            var singletons = new HashSet<char>();
            while (index < subtags.Length && subtags[index].Length == 1 && !IsPrivateUse(subtags[index]))
            {
                if (!singletons.Add(char.ToLowerInvariant(subtags[index][0])))
                    return false;

                index++;
                var start = index;
                while (index < subtags.Length && subtags[index].Length >= 2)
                {
                    index++;
                }

                if (index == start)
                    return false;
            }

            if (index == subtags.Length)
                return true;
        }

        // ["-" privateuse]
        if (!IsPrivateUse(subtags[index]) || index == subtags.Length - 1)
            return false;

        return true;

        static bool IsPrivateUse(string subtag) => subtag is "x" or "X";
    }

    private static NotificationPermission ToNotificationPermission(PermissionState state) => state switch
    {
        PermissionState.Granted => NotificationPermission.Granted,
        PermissionState.Denied => NotificationPermission.Denied,
        _ => NotificationPermission.Default,
    };
}

/// <summary>
/// A notification (https://notifications.spec.whatwg.org/#concept-notification): the data shared by the <see cref="Notification"/>
/// objects that represent it. A persistent notification has a service worker registration. The members are immutable,
/// except <see cref="HostObject"/>.
/// </summary>
internal sealed class NotificationRecord
{
    public required string Title { get; init; }

    public NotificationDirection Dir { get; init; }

    public required string Lang { get; init; }

    public required string Body { get; init; }

    public required string Tag { get; init; }

    public UrlRecord? NavigationUrl { get; init; }

    public UrlRecord? ImageUrl { get; init; }

    public UrlRecord? IconUrl { get; init; }

    public UrlRecord? BadgeUrl { get; init; }

    public ulong Timestamp { get; init; }

    public bool Renotify { get; init; }

    public bool? Silent { get; init; }

    public bool RequireInteraction { get; init; }

    /// <summary>The data, serialized by StructuredSerializeForStorage, or null.</summary>
    public object? SerializedData { get; init; }

    public required IReadOnlyList<NotificationActionRecord> Actions { get; init; }

    /// <summary>The serialized origin of the notification: a notification only replaces the notifications of its origin.</summary>
    public required string Origin { get; init; }

    /// <summary>The service worker registration of a persistent notification, or null.</summary>
    public ServiceWorkerRegistrationRecord? Registration { get; init; }

    /// <summary>The object given to the host callbacks: the object created by the constructor, or for a persistent notification, an object of the realm that showed it.</summary>
    public Notification? HostObject { get; set; }

    /// <summary>https://notifications.spec.whatwg.org/#create-a-notification-with-a-settings-object</summary>
    public static NotificationRecord Create(GlobalScope global, string title, NotificationOptions options, ServiceWorkerRegistrationRecord? registration)
    {
        if (options.Renotify && options.Tag.Length == 0)
            throw new TypeErrorException("A notification without tag cannot renotify");

        var serializedData = options.Data is null ? null : global.ScriptEngine is { } engine ? engine.StructuredSerializeForStorage(global, options.Data) : StructuredClone.Serialize(options.Data);
        var baseUrl = global.ApiBaseUrl;
        var actions = new List<NotificationActionRecord>();
        foreach (var entry in options.Actions)
        {
            // The actions that the platform does not support are skipped
            if (actions.Count >= Notification.MaxActionsCount)
                break;

            actions.Add(new NotificationActionRecord(entry.Action, entry.Title, ParseUrl(entry.Navigate, baseUrl), ParseUrl(entry.Icon, baseUrl)));
        }

        return new NotificationRecord
        {
            Title = title,
            Dir = options.Dir,
            Lang = Notification.IsValidLanguageTag(options.Lang) ? options.Lang : "",
            Body = options.Body,
            Tag = options.Tag,
            NavigationUrl = ParseUrl(options.Navigate, baseUrl),
            ImageUrl = ParseUrl(options.Image, baseUrl),
            IconUrl = ParseUrl(options.Icon, baseUrl),
            BadgeUrl = ParseUrl(options.Badge, baseUrl),
            Timestamp = options.Timestamp ?? (ulong)Math.Max(0, (global.EventLoop.TimeProvider.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds),
            Renotify = options.Renotify,
            Silent = options.Silent,
            RequireInteraction = options.RequireInteraction,
            SerializedData = serializedData,
            Actions = actions,
            Origin = global.EnvironmentDocument.Origin.Serialize(),
            Registration = registration,
        };
    }

    /// <summary>Parses a URL of the options: a URL that cannot be parsed is ignored.</summary>
    private static UrlRecord? ParseUrl(string? url, UrlRecord baseUrl) => url is null ? null : UrlParser.Parse(url, baseUrl);
}

/// <summary>A notification action (https://notifications.spec.whatwg.org/#action) of a persistent notification.</summary>
internal sealed record NotificationActionRecord(string Name, string Title, UrlRecord? NavigationUrl, UrlRecord? IconUrl);

/// <summary>The initialization dictionary of a <see cref="NotificationEvent"/> (https://notifications.spec.whatwg.org/#dictdef-notificationeventinit).</summary>
/// <seealso href="https://notifications.spec.whatwg.org/#dictdef-notificationeventinit"/>
[IdlInterface("NotificationEventInit", "https://notifications.spec.whatwg.org/#dictdef-notificationeventinit")]
public sealed class NotificationEventInit : ExtendableEventInit
{
    /// <summary>Gets the notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationeventinit-notification"/>
    [IdlMember("notification", "https://notifications.spec.whatwg.org/#dom-notificationeventinit-notification")]
    public required Notification Notification { get; init; }

    /// <summary>Gets the name of the activated action, or the empty string.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationeventinit-action"/>
    [IdlMember("action", "https://notifications.spec.whatwg.org/#dom-notificationeventinit-action")]
    public string Action { get; init; } = "";
}

/// <summary>
/// The <c>notificationclick</c> and <c>notificationclose</c> events of a service worker (https://notifications.spec.whatwg.org/#notificationevent):
/// the user activated (<see cref="Notification.Click()"/>) or closed (<see cref="Notification.Dismiss"/>) a persistent
/// notification of its registration.
/// </summary>
/// <seealso href="https://notifications.spec.whatwg.org/#notificationevent"/>
[IdlInterface("NotificationEvent", "https://notifications.spec.whatwg.org/#notificationevent")]
public sealed class NotificationEvent : ExtendableEvent
{
    /// <summary>Creates a notification event.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationevent-notificationevent"/>
    [IdlMember("constructor", "https://notifications.spec.whatwg.org/#dom-notificationevent-notificationevent")]
    public NotificationEvent(string type, NotificationEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        Notification = eventInitDict.Notification;
        Action = eventInitDict.Action;
    }

    /// <summary>Gets the notification: a new object that represents the persistent notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationevent-notification"/>
    [IdlMember("notification", "https://notifications.spec.whatwg.org/#dom-notificationevent-notification")]
    public Notification Notification { get; }

    /// <summary>Gets the name of the activated action, or the empty string when the notification itself was activated (or closed).</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationevent-action"/>
    [IdlMember("action", "https://notifications.spec.whatwg.org/#dom-notificationevent-action")]
    public string Action { get; }
}
