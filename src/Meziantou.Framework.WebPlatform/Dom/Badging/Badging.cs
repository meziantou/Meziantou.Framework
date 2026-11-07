using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The kind of an application badge (https://w3c.github.io/badging/#the-badge).</summary>
public enum AppBadgeKind
{
    /// <summary>No badge: the badge is cleared.</summary>
    Nothing,

    /// <summary>A badge without a number (e.g. a dot): <c>setAppBadge()</c> was called without argument.</summary>
    Flag,

    /// <summary>A badge that shows a number (<see cref="AppBadge.Count"/>).</summary>
    Count,
}

/// <summary>An application badge set by <c>navigator.setAppBadge()</c> or <c>navigator.clearAppBadge()</c> (https://w3c.github.io/badging/#the-badge).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AppBadge
{
    private AppBadge(AppBadgeKind kind, ulong count)
    {
        Kind = kind;
        Count = count;
    }

    /// <summary>Gets the badge that clears the badge.</summary>
    public static AppBadge Nothing => new(AppBadgeKind.Nothing, 0);

    /// <summary>Gets the badge without a number.</summary>
    public static AppBadge Flag => new(AppBadgeKind.Flag, 0);

    /// <summary>Gets the kind of the badge.</summary>
    public AppBadgeKind Kind { get; }

    /// <summary>Gets the number shown by the badge, when <see cref="Kind"/> is <see cref="AppBadgeKind.Count"/> (0 otherwise).</summary>
    public ulong Count { get; }

    /// <summary>Creates a badge that shows a number; 0 clears the badge.</summary>
    /// <param name="count">The number to show.</param>
    public static AppBadge FromCount(ulong count) => count is 0 ? Nothing : new(AppBadgeKind.Count, count);
}

/// <summary>The host hooks of the Badging API (https://w3c.github.io/badging/, <see cref="BrowsingContextOptions.Badging"/>).</summary>
public sealed class BadgingOptions
{
    /// <summary>
    /// Gets or sets the callback that sets the badge of the application (<c>navigator.setAppBadge()</c> and
    /// <c>navigator.clearAppBadge()</c>), e.g. on the icon of the installed web application. The library checks the
    /// document (fully active, same origin as the top-level document) before calling it. The callback is invoked on the
    /// thread of the event loop, and must not block it. The default is null: the badge is not shown anywhere, and the
    /// promises are resolved.
    /// The first argument is the context of the global object that set the badge (see <see cref="CallbackContext"/>): its
    /// <see cref="CallbackContext.Global"/> is the window or the worker.
    /// </summary>
    public Action<CallbackContext, AppBadge>? SetBadge { get; init; }

    /// <summary>
    /// Gets or sets whether setting the badge requires the <c>notifications</c> permission (as Safari): the promises are
    /// rejected with a <c>NotAllowedError</c> when the permission is not granted. The default is false (as Chromium).
    /// </summary>
    public bool RequiresNotificationsPermission { get; init; }

    /// <summary>https://w3c.github.io/badging/#setting-the-application-badge</summary>
    internal static Task SetAppBadge(GlobalScope global, AppBadge badge)
    {
        if (global is Window window)
        {
            var document = window.Document;
            if (!document.IsFullyActive)
                return Task.FromException(DomException.InvalidState("The document is not fully active"));

            if (document.BrowsingContext?.Top.Document is { } top && !Document.IsSameOriginDomain(document, top))
                return Task.FromException(new DomException("The document is not same origin with the top-level document", "SecurityError"));
        }

        var completion = new TaskCompletionSource();
        global.QueueGlobalTask(() =>
        {
            var options = global.Options.Badging;
            if (options is { RequiresNotificationsPermission: true } && Permissions.GetPermissionState(global, "notifications") != PermissionState.Granted)
            {
                completion.TrySetException(DomException.NotAllowed("The notifications permission is not granted"));
                return;
            }

            options?.SetBadge?.Invoke(CallbackContext.For(global), badge);
            completion.TrySetResult();
        });
        return completion.Task;
    }
}

// The Badging API (https://w3c.github.io/badging/)
public sealed partial class Navigator
{
    /// <summary>
    /// Sets the badge of the application (https://w3c.github.io/badging/#setappbadge-method), with
    /// <see cref="BadgingOptions.SetBadge"/>: a flag without argument, a number, or nothing when the number is 0. The task
    /// fails with an <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully active, and a
    /// <c>SecurityError</c> when it is not same origin with the top-level document.
    /// </summary>
    /// <param name="contents">The number to show, or null to show a flag.</param>
    /// <seealso href="https://w3c.github.io/badging/#dom-navigatorbadge-setappbadge"/>
    [IdlMember("setAppBadge", "https://w3c.github.io/badging/#dom-navigatorbadge-setappbadge")]
    public Task SetAppBadge(ulong? contents = null) => BadgingOptions.SetAppBadge(_window, contents is { } count ? AppBadge.FromCount(count) : AppBadge.Flag);

    /// <summary>Clears the badge of the application (https://w3c.github.io/badging/#clearappbadge-method), as <c>setAppBadge(0)</c>.</summary>
    /// <seealso href="https://w3c.github.io/badging/#dom-navigatorbadge-clearappbadge"/>
    [IdlMember("clearAppBadge", "https://w3c.github.io/badging/#dom-navigatorbadge-clearappbadge")]
    public Task ClearAppBadge() => BadgingOptions.SetAppBadge(_window, AppBadge.Nothing);
}

// The Badging API (https://w3c.github.io/badging/)
public sealed partial class WorkerNavigator
{
    /// <summary>Sets the badge of the application (https://w3c.github.io/badging/#setappbadge-method), with <see cref="BadgingOptions.SetBadge"/>.</summary>
    /// <param name="contents">The number to show, or null to show a flag.</param>
    /// <seealso href="https://w3c.github.io/badging/#dom-navigatorbadge-setappbadge"/>
    [IdlMember("setAppBadge", "https://w3c.github.io/badging/#dom-navigatorbadge-setappbadge")]
    public Task SetAppBadge(ulong? contents = null) => BadgingOptions.SetAppBadge(_global, contents is { } count ? AppBadge.FromCount(count) : AppBadge.Flag);

    /// <summary>Clears the badge of the application (https://w3c.github.io/badging/#clearappbadge-method), as <c>setAppBadge(0)</c>.</summary>
    /// <seealso href="https://w3c.github.io/badging/#dom-navigatorbadge-clearappbadge"/>
    [IdlMember("clearAppBadge", "https://w3c.github.io/badging/#dom-navigatorbadge-clearappbadge")]
    public Task ClearAppBadge() => BadgingOptions.SetAppBadge(_global, AppBadge.Nothing);
}
