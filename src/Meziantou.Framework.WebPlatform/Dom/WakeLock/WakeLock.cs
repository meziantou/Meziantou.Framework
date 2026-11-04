namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The type of a wake lock (https://w3c.github.io/screen-wake-lock/#the-wakelocktype-enum).</summary>
/// <seealso href="https://w3c.github.io/screen-wake-lock/#dom-wakelocktype"/>
[IdlInterface("WakeLockType", "https://w3c.github.io/screen-wake-lock/#dom-wakelocktype")]
public enum WakeLockType
{
    /// <summary>The screen is kept on.</summary>
    /// <seealso href="https://w3c.github.io/screen-wake-lock/#dom-wakelocktype-screen"/>
    [IdlMember("screen", "https://w3c.github.io/screen-wake-lock/#dom-wakelocktype-screen")]
    Screen,
}

/// <summary>The host hooks of the Screen Wake Lock API (https://w3c.github.io/screen-wake-lock/, <see cref="BrowsingContextOptions.WakeLock"/>).</summary>
public sealed class WakeLockOptions
{
    /// <summary>
    /// Gets or sets the callback that asks the operating system to acquire the wake lock (https://w3c.github.io/screen-wake-lock/#dfn-acquire-a-wake-lock),
    /// e.g. to keep the screen on: it is called when a document acquires its first lock of a type
    /// (<c>navigator.wakeLock.request()</c>). The library checks the document (fully active, visible, allowed by the
    /// <c>screen-wake-lock</c> permissions policy) and the <c>screen-wake-lock</c> permission (see
    /// <see cref="BrowsingContextOptions.Permissions"/> and <see cref="BrowsingContextOptions.RequestPermission"/>) before
    /// calling it. The result of the request to the operating system is not reported to the document (the lock is
    /// advisory). The callback is invoked on the thread of the event loop, and must not block it. The default is null:
    /// nothing is kept on, but the documents get their locks.
    /// The first argument is the context of the window (see <see cref="CallbackContext"/>): its <see cref="CallbackContext.Window"/> is never null.
    /// </summary>
    public Action<CallbackContext, WakeLockType>? Acquire { get; init; }

    /// <summary>
    /// Gets or sets the callback that asks the operating system to release the wake lock (https://w3c.github.io/screen-wake-lock/#dfn-release-a-wake-lock):
    /// it is called when the last lock of a type of a document is released (<c>WakeLockSentinel.release()</c>, the
    /// document becomes hidden or is no longer fully active). The callback is invoked on the thread of the event loop. The
    /// default is null.
    /// The first argument is the context of the window (see <see cref="CallbackContext"/>): its <see cref="CallbackContext.Window"/> is never null.
    /// </summary>
    public Action<CallbackContext, WakeLockType>? Release { get; init; }
}

/// <summary>
/// The Screen Wake Lock API of a window (https://w3c.github.io/screen-wake-lock/#the-wakelock-interface,
/// <c>navigator.wakeLock</c>): the locks that prevent the screen from turning off, acquired with
/// <see cref="WakeLockOptions.Acquire"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/screen-wake-lock/#dom-wakelock"/>
[IdlInterface("WakeLock", "https://w3c.github.io/screen-wake-lock/#dom-wakelock")]
public sealed class WakeLock
{
    private const string PermissionName = "screen-wake-lock";

    private readonly Window _window;

    /// <summary>The [[ActiveLocks]]["screen"] slot of the document of the window (https://w3c.github.io/screen-wake-lock/#dfn-activelocks).</summary>
    private readonly List<WakeLockSentinel> _activeLocks = [];

    internal WakeLock(Window window) => _window = window;

    /// <summary>
    /// Requests a wake lock (https://w3c.github.io/screen-wake-lock/#the-request-method). The task fails with a
    /// <c>NotAllowedError</c> <see cref="DomException"/> when the document is not fully active, is not allowed to use the
    /// <c>screen-wake-lock</c> feature, is hidden, or when the <c>screen-wake-lock</c> permission is denied.
    /// </summary>
    /// <param name="type">The type of the lock.</param>
    /// <seealso href="https://w3c.github.io/screen-wake-lock/#dom-wakelock-request"/>
    [IdlMember("request", "https://w3c.github.io/screen-wake-lock/#dom-wakelock-request")]
    public Task<WakeLockSentinel> Request(WakeLockType type = WakeLockType.Screen)
    {
        var document = _window.Document;
        if (!document.IsFullyActive)
            return Task.FromException<WakeLockSentinel>(DomException.NotAllowed("The document is not fully active"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.ScreenWakeLock))
            return Task.FromException<WakeLockSentinel>(DomException.NotAllowed("The document is not allowed to use the screen-wake-lock feature"));

        if (document.Hidden)
            return Task.FromException<WakeLockSentinel>(DomException.NotAllowed("The document is hidden"));

        // The permission is requested in parallel, then the lock is created by a task
        var promise = new TaskCompletionSource<WakeLockSentinel>();
        _window.QueueGlobalTask(() =>
        {
            if (Permissions.RequestPermissionToUse(_window, PermissionName) == PermissionState.Denied)
            {
                promise.TrySetException(DomException.NotAllowed("The screen-wake-lock permission is denied"));
                return;
            }

            if (!document.IsFullyActive)
            {
                promise.TrySetException(DomException.NotAllowed("The document is not fully active"));
                return;
            }

            if (document.Hidden)
            {
                promise.TrySetException(DomException.NotAllowed("The document is hidden"));
                return;
            }

            if (_activeLocks.Count == 0)
            {
                _window.Options.WakeLock?.Acquire?.Invoke(CallbackContext.For(_window), type);
            }

            var sentinel = new WakeLockSentinel(this, type);
            _activeLocks.Add(sentinel);
            promise.TrySetResult(sentinel);
        });
        return promise.Task;
    }

    /// <summary>https://w3c.github.io/screen-wake-lock/#dfn-release-a-wake-lock</summary>
    internal void Release(WakeLockSentinel sentinel)
    {
        if (!_activeLocks.Remove(sentinel))
            return;

        if (_activeLocks.Count == 0)
        {
            _window.Options.WakeLock?.Release?.Invoke(CallbackContext.For(_window), sentinel.Type);
        }

        sentinel.MarkReleased();
    }

    /// <summary>
    /// Releases all the locks of the document: it becomes hidden (https://w3c.github.io/screen-wake-lock/#handling-document-loss-of-visibility)
    /// or is no longer fully active (https://w3c.github.io/screen-wake-lock/#handling-document-loss-of-full-activity).
    /// </summary>
    internal void ReleaseAll()
    {
        foreach (var sentinel in _activeLocks.ToArray())
        {
            Release(sentinel);
        }
    }

    internal Window Window => _window;
}

/// <summary>
/// A wake lock acquired by <see cref="WakeLock.Request"/> (https://w3c.github.io/screen-wake-lock/#the-wakelocksentinel-interface):
/// its <c>release</c> event is fired when it is released by <see cref="Release"/> or by the user agent.
/// </summary>
/// <seealso href="https://w3c.github.io/screen-wake-lock/#dom-wakelocksentinel"/>
[IdlInterface("WakeLockSentinel", "https://w3c.github.io/screen-wake-lock/#dom-wakelocksentinel")]
public sealed class WakeLockSentinel : EventTarget
{
    private readonly WakeLock _wakeLock;

    internal WakeLockSentinel(WakeLock wakeLock, WakeLockType type)
    {
        _wakeLock = wakeLock;
        Type = type;
    }

    internal override GlobalScope? RelevantGlobal => _wakeLock.Window;

    /// <summary>Gets whether the lock was released.</summary>
    /// <seealso href="https://w3c.github.io/screen-wake-lock/#dom-wakelocksentinel-released"/>
    [IdlMember("released", "https://w3c.github.io/screen-wake-lock/#dom-wakelocksentinel-released")]
    public bool Released { get; private set; }

    /// <summary>Gets the type of the lock.</summary>
    /// <seealso href="https://w3c.github.io/screen-wake-lock/#dom-wakelocksentinel-type"/>
    [IdlMember("type", "https://w3c.github.io/screen-wake-lock/#dom-wakelocksentinel-type")]
    public WakeLockType Type { get; }

    /// <summary>Gets or sets the event handler of the <c>release</c> event.</summary>
    /// <seealso href="https://w3c.github.io/screen-wake-lock/#dom-wakelocksentinel-onrelease"/>
    [IdlMember("onrelease", "https://w3c.github.io/screen-wake-lock/#dom-wakelocksentinel-onrelease")]
    public Action<Event>? OnRelease
    {
        get => GetEventHandler("release");
        set => SetEventHandler("release", value);
    }

    /// <summary>Releases the lock (https://w3c.github.io/screen-wake-lock/#the-release-method); the <c>release</c> event is fired before the method returns.</summary>
    /// <seealso href="https://w3c.github.io/screen-wake-lock/#dom-wakelocksentinel-release"/>
    [IdlMember("release", "https://w3c.github.io/screen-wake-lock/#dom-wakelocksentinel-release")]
    public Task Release()
    {
        if (!Released)
        {
            _wakeLock.Release(this);
        }

        return Task.CompletedTask;
    }

    internal void MarkReleased()
    {
        Released = true;
        FireEvent(new Event("release"));
    }
}

// The Screen Wake Lock API (https://w3c.github.io/screen-wake-lock/)
public sealed partial class Navigator
{
    private WakeLock? _wakeLock;

    /// <summary>Gets the Screen Wake Lock API of the window (https://w3c.github.io/screen-wake-lock/#extensions-to-the-navigator-interface).</summary>
    /// <seealso href="https://w3c.github.io/screen-wake-lock/#dom-navigator-wakelock"/>
    [IdlMember("wakeLock", "https://w3c.github.io/screen-wake-lock/#dom-navigator-wakelock")]
    public WakeLock WakeLock => _wakeLock ??= new WakeLock(_window);

    internal WakeLock? WakeLockOrNull => _wakeLock;
}
