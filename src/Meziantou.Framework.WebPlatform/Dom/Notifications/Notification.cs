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

/// <summary>An action of a notification (https://notifications.spec.whatwg.org/#dictdef-notificationaction). The library does not support the actions.</summary>
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

    /// <summary>Gets the actions of the notification. The library does not support the actions: the list must be empty.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notificationoptions-actions"/>
    [IdlMember("actions", "https://notifications.spec.whatwg.org/#dom-notificationoptions-actions")]
    public IReadOnlyList<NotificationAction> Actions { get; init; } = [];
}

/// <summary>
/// A notification shown to the user (https://notifications.spec.whatwg.org/#notification), by
/// <see cref="BrowsingContextOptions.ShowNotification"/> once the <c>notifications</c> permission is granted. The host
/// calls <see cref="Click"/> when the user activates the notification and <see cref="Dismiss"/> when the user closes it.
/// </summary>
/// <remarks>
/// The <c>error</c> event is fired when the permission is not granted. A notification whose tag is not empty replaces the
/// notification of the same origin with the same tag (its <c>close</c> event is fired). The persistent notifications of
/// the service workers (<c>showNotification()</c>) and the actions are not supported.
/// </remarks>
/// <seealso href="https://notifications.spec.whatwg.org/#notification"/>
[IdlInterface("Notification", "https://notifications.spec.whatwg.org/#notification")]
public sealed class Notification : EventTarget
{
    private const string PermissionName = "notifications";

    private readonly GlobalScope _global;
    private readonly object? _serializedData;
    private readonly UrlRecord? _navigate;
    private readonly UrlRecord? _image;
    private readonly UrlRecord? _icon;
    private readonly UrlRecord? _badge;
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

        // https://notifications.spec.whatwg.org/#create-a-notification
        if (options.Renotify && options.Tag.Length == 0)
            throw new TypeErrorException("A notification without tag cannot renotify");

        _global = global;
        _serializedData = options.Data is null ? null : global.ScriptEngine is { } engine ? engine.StructuredSerializeForStorage(global, options.Data) : StructuredClone.Serialize(options.Data);
        Title = title;
        Dir = options.Dir;
        Lang = IsValidLanguageTag(options.Lang) ? options.Lang : "";
        Body = options.Body;
        Tag = options.Tag;
        var baseUrl = global.ApiBaseUrl;
        _navigate = ParseUrl(options.Navigate, baseUrl);
        _image = ParseUrl(options.Image, baseUrl);
        _icon = ParseUrl(options.Icon, baseUrl);
        _badge = ParseUrl(options.Badge, baseUrl);
        Timestamp = options.Timestamp ?? (ulong)Math.Max(0, (global.EventLoop.TimeProvider.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds);
        Renotify = options.Renotify;
        Silent = options.Silent;
        RequireInteraction = options.RequireInteraction;
        Origin = global.EnvironmentDocument.Origin.Serialize();

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

    internal override GlobalScope? RelevantGlobal => _global;

    /// <summary>The serialized origin of the notification: a notification only replaces the notifications of its origin.</summary>
    private string Origin { get; }

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

    /// <summary>Gets the maximum number of actions of a notification (https://notifications.spec.whatwg.org/#dom-notification-maxactions): 0, the actions are not supported.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-maxactions"/>
    [IdlMember("maxActions", "https://notifications.spec.whatwg.org/#dom-notification-maxactions")]
    public static uint MaxActions => 0;

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
    public string Title { get; }

    /// <summary>Gets the direction of the text.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-dir"/>
    [IdlMember("dir", "https://notifications.spec.whatwg.org/#dom-notification-dir")]
    public NotificationDirection Dir { get; }

    /// <summary>Gets the language of the text.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-lang"/>
    [IdlMember("lang", "https://notifications.spec.whatwg.org/#dom-notification-lang")]
    public string Lang { get; }

    /// <summary>Gets the body of the notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-body"/>
    [IdlMember("body", "https://notifications.spec.whatwg.org/#dom-notification-body")]
    public string Body { get; }

    /// <summary>Gets the absolute URL to navigate to when the notification is activated, or the empty string.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-navigate"/>
    [IdlMember("navigate", "https://notifications.spec.whatwg.org/#dom-notification-navigate")]
    public string Navigate => _navigate?.Serialize() ?? "";

    /// <summary>Gets the tag of the notification.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-tag"/>
    [IdlMember("tag", "https://notifications.spec.whatwg.org/#dom-notification-tag")]
    public string Tag { get; }

    /// <summary>Gets the absolute URL of the image, or the empty string.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-image"/>
    [IdlMember("image", "https://notifications.spec.whatwg.org/#dom-notification-image")]
    public string Image => _image?.Serialize() ?? "";

    /// <summary>Gets the absolute URL of the icon, or the empty string.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-icon"/>
    [IdlMember("icon", "https://notifications.spec.whatwg.org/#dom-notification-icon")]
    public string Icon => _icon?.Serialize() ?? "";

    /// <summary>Gets the absolute URL of the badge, or the empty string.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-badge"/>
    [IdlMember("badge", "https://notifications.spec.whatwg.org/#dom-notification-badge")]
    public string Badge => _badge?.Serialize() ?? "";

    /// <summary>Gets the time of the notification, in milliseconds since the Unix epoch.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-timestamp"/>
    [IdlMember("timestamp", "https://notifications.spec.whatwg.org/#dom-notification-timestamp")]
    public ulong Timestamp { get; }

    /// <summary>Gets whether the user is alerted again when the notification replaces another one.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-renotify"/>
    [IdlMember("renotify", "https://notifications.spec.whatwg.org/#dom-notification-renotify")]
    public bool Renotify { get; }

    /// <summary>Gets whether the notification is silent, or null for the default of the device.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-silent"/>
    [IdlMember("silent", "https://notifications.spec.whatwg.org/#dom-notification-silent")]
    public bool? Silent { get; }

    /// <summary>Gets whether the notification stays until the user activates or closes it.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-requireinteraction"/>
    [IdlMember("requireInteraction", "https://notifications.spec.whatwg.org/#dom-notification-requireinteraction")]
    public bool RequireInteraction { get; }

    /// <summary>Gets a deserialization of the data of the notification (the same value each time).</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-data"/>
    [IdlMember("data", "https://notifications.spec.whatwg.org/#dom-notification-data")]
    public object? Data
    {
        get
        {
            if (!_isDataDeserialized)
            {
                _data = _serializedData is null ? null : _global.ScriptEngine is { } engine ? engine.StructuredDeserialize(_global, _serializedData) : StructuredClone.Deserialize(_serializedData);
                _isDataDeserialized = true;
            }

            return _data;
        }
    }

    /// <summary>Gets the actions of the notification (https://notifications.spec.whatwg.org/#dom-notification-actions): always empty.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-actions"/>
    [IdlMember("actions", "https://notifications.spec.whatwg.org/#dom-notification-actions")]
    public IReadOnlyList<NotificationAction> Actions => [];

    /// <summary>Gets whether the notification is shown: it is in the list of notifications of the browsing contexts.</summary>
    [NotInIdl]
    public bool IsShown
    {
        get
        {
            var list = _global.Options.ShownNotifications;
            lock (list)
            {
                return list.Contains(this);
            }
        }
    }

    /// <summary>Closes the notification (https://notifications.spec.whatwg.org/#dom-notification-close), and fires its <c>close</c> event.</summary>
    /// <seealso href="https://notifications.spec.whatwg.org/#dom-notification-close"/>
    [IdlMember("close", "https://notifications.spec.whatwg.org/#dom-notification-close")]
    public void Close()
    {
        // A notification closed before it is shown is never shown (https://notifications.spec.whatwg.org/#show-steps)
        _closeRequested = true;
        if (RunCloseSteps())
        {
            InvokeHost(_global.Options.CloseNotification);
        }
    }

    /// <summary>
    /// Activates the notification, as when the user clicks it (https://notifications.spec.whatwg.org/#activating-a-notification):
    /// fires its <c>click</c> event, and focuses its window unless the event is canceled. It must be called on the thread of
    /// the event loop of the browsing context.
    /// </summary>
    [NotInIdl]
    public void Click()
    {
        if (!FireEvent(new Event("click", new EventInit { Cancelable = true })))
            return;

        if (_global is Window window && window.Document.IsFullyActive)
        {
            window.Focus();
        }
    }

    /// <summary>
    /// Closes the notification, as when the user dismisses it (https://notifications.spec.whatwg.org/#closing-a-notification):
    /// fires its <c>close</c> event. <see cref="BrowsingContextOptions.CloseNotification"/> is not called. It must be called
    /// on the thread of the event loop of the browsing context.
    /// </summary>
    [NotInIdl]
    public void Dismiss() => RunCloseSteps();

    /// <summary>https://notifications.spec.whatwg.org/#show-steps</summary>
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

        Notification? oldNotification = null;
        var list = _global.Options.ShownNotifications;
        lock (list)
        {
            if (Tag.Length > 0)
            {
                var index = list.FindIndex(notification => notification.Tag == Tag && notification.Origin == Origin);
                if (index >= 0)
                {
                    oldNotification = list[index];
                    list[index] = this;
                }
            }

            if (oldNotification is null)
            {
                list.Add(this);
            }
        }

        if (oldNotification is not null)
        {
            oldNotification.InvokeHost(_global.Options.CloseNotification);
            oldNotification.QueueCloseEvent();
        }

        // The show event is fired by a task, after the close event of the replaced notification
        InvokeHost(_global.Options.ShowNotification);
        _global.QueueGlobalTask(() => FireEvent(new Event("show")));
    }

    /// <summary>https://notifications.spec.whatwg.org/#close-steps: returns false when the notification was not shown.</summary>
    private bool RunCloseSteps()
    {
        var list = _global.Options.ShownNotifications;
        lock (list)
        {
            if (!list.Remove(this))
                return false;
        }

        QueueCloseEvent();
        return true;
    }

    /// <summary>https://notifications.spec.whatwg.org/#handle-close-events</summary>
    private void QueueCloseEvent() => _global.QueueGlobalTask(() => FireEvent(new Event("close")));

    private void InvokeHost(Action<CallbackContext, Notification>? callback) => callback?.Invoke(CallbackContext.For(_global), this);

    /// <summary>
    /// https://notifications.spec.whatwg.org/#get-the-notifications-permission-state: as in the browsers, the documents that
    /// are not same origin with all their ancestors (and their workers) cannot show notifications.
    /// </summary>
    private static PermissionState GetPermissionState(GlobalScope global) => IsThirdPartyContext(global) ? PermissionState.Denied : Permissions.GetPermissionState(global, PermissionName);

    private static bool IsThirdPartyContext(GlobalScope global)
    {
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

    private static UrlRecord? ParseUrl(string? url, UrlRecord baseUrl) => url is null ? null : UrlParser.Parse(url, baseUrl);

    private static NotificationPermission ToNotificationPermission(PermissionState state) => state switch
    {
        PermissionState.Granted => NotificationPermission.Granted,
        PermissionState.Denied => NotificationPermission.Denied,
        _ => NotificationPermission.Default,
    };
}
