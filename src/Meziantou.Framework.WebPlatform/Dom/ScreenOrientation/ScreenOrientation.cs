namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>An orientation of the screen (https://w3c.github.io/screen-orientation/#orientationtype-enum).</summary>
/// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationtype"/>
[IdlInterface("OrientationType", "https://w3c.github.io/screen-orientation/#dom-orientationtype")]
public enum OrientationType
{
    /// <summary>The primary portrait orientation.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationtype-portrait-primary"/>
    [IdlMember("portrait-primary", "https://w3c.github.io/screen-orientation/#dom-orientationtype-portrait-primary")]
    PortraitPrimary,

    /// <summary>The secondary portrait orientation (upside down).</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationtype-portrait-secondary"/>
    [IdlMember("portrait-secondary", "https://w3c.github.io/screen-orientation/#dom-orientationtype-portrait-secondary")]
    PortraitSecondary,

    /// <summary>The primary landscape orientation.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationtype-landscape-primary"/>
    [IdlMember("landscape-primary", "https://w3c.github.io/screen-orientation/#dom-orientationtype-landscape-primary")]
    LandscapePrimary,

    /// <summary>The secondary landscape orientation.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationtype-landscape-secondary"/>
    [IdlMember("landscape-secondary", "https://w3c.github.io/screen-orientation/#dom-orientationtype-landscape-secondary")]
    LandscapeSecondary,
}

/// <summary>An orientation to which the screen can be locked (https://w3c.github.io/screen-orientation/#orientationlocktype-enum).</summary>
/// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationlocktype"/>
[IdlInterface("OrientationLockType", "https://w3c.github.io/screen-orientation/#dom-orientationlocktype")]
public enum OrientationLockType
{
    /// <summary>The screen can be rotated to any orientation.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationlocktype-any"/>
    [IdlMember("any", "https://w3c.github.io/screen-orientation/#dom-orientationlocktype-any")]
    Any,

    /// <summary>The natural orientation of the screen.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationlocktype-natural"/>
    [IdlMember("natural", "https://w3c.github.io/screen-orientation/#dom-orientationlocktype-natural")]
    Natural,

    /// <summary>A landscape orientation, primary or secondary.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationlocktype-landscape"/>
    [IdlMember("landscape", "https://w3c.github.io/screen-orientation/#dom-orientationlocktype-landscape")]
    Landscape,

    /// <summary>A portrait orientation, primary or secondary.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationlocktype-portrait"/>
    [IdlMember("portrait", "https://w3c.github.io/screen-orientation/#dom-orientationlocktype-portrait")]
    Portrait,

    /// <summary>The primary portrait orientation.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationlocktype-portrait-primary"/>
    [IdlMember("portrait-primary", "https://w3c.github.io/screen-orientation/#dom-orientationlocktype-portrait-primary")]
    PortraitPrimary,

    /// <summary>The secondary portrait orientation.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationlocktype-portrait-secondary"/>
    [IdlMember("portrait-secondary", "https://w3c.github.io/screen-orientation/#dom-orientationlocktype-portrait-secondary")]
    PortraitSecondary,

    /// <summary>The primary landscape orientation.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationlocktype-landscape-primary"/>
    [IdlMember("landscape-primary", "https://w3c.github.io/screen-orientation/#dom-orientationlocktype-landscape-primary")]
    LandscapePrimary,

    /// <summary>The secondary landscape orientation.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-orientationlocktype-landscape-secondary"/>
    [IdlMember("landscape-secondary", "https://w3c.github.io/screen-orientation/#dom-orientationlocktype-landscape-secondary")]
    LandscapeSecondary,
}

/// <summary>The host hooks of the Screen Orientation API (https://w3c.github.io/screen-orientation/, <see cref="BrowsingContextOptions.ScreenOrientation"/>).</summary>
public sealed class ScreenOrientationOptions
{
    /// <summary>
    /// Gets or sets the screen of the device (<c>screen.orientation</c>): its orientation, and the locks of the documents, e.g.
    /// a <see cref="ScreenOrientationProvider"/> whose <see cref="ScreenOrientationProvider.DeviceOrientation"/> is set by the
    /// application to simulate a rotation, or a subclass that locks the screen of the operating system. A provider can be
    /// shared by several browsing contexts. The default is null: the screen of a desktop computer, whose orientation is the
    /// primary orientation of the shape of the screen (angle 0), and which cannot be locked (<c>lock()</c> is rejected with a
    /// <c>NotSupportedError</c>).
    /// </summary>
    public ScreenOrientationProvider? Provider { get; init; }
}

/// <summary>
/// The screen of the device (https://w3c.github.io/screen-orientation/#the-current-screen-orientation-type-and-angle), read
/// by <c>screen.orientation</c> and locked by <c>screen.orientation.lock()</c>, of the browsing contexts that share a
/// <see cref="BrowsingContextOptions"/> (<see cref="ScreenOrientationOptions.Provider"/>). The default implementation simulates
/// a screen that rotates to the orientation of each lock: set <see cref="DeviceOrientation"/> to simulate the user rotating
/// the device. Override <see cref="TryLock"/> to lock the screen of the operating system, and call
/// <see cref="OnOrientationChanged"/> when the orientation changes.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class ScreenOrientationProvider
{
    private readonly Lock _lock = new();
    private readonly List<WeakReference<BrowsingContext>> _browsingContexts = [];
    private OrientationType _deviceOrientation;
    private OrientationLockType? _activeLock;

    /// <summary>Initializes a new instance of the <see cref="ScreenOrientationProvider"/> class, for a screen whose natural orientation is <see cref="OrientationType.LandscapePrimary"/>.</summary>
    public ScreenOrientationProvider()
        : this(OrientationType.LandscapePrimary)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ScreenOrientationProvider"/> class.</summary>
    /// <param name="naturalOrientation">
    /// The natural orientation of the screen (https://w3c.github.io/screen-orientation/#dfn-natural), from which the angles are
    /// measured: <see cref="OrientationType.PortraitPrimary"/> for a phone, <see cref="OrientationType.LandscapePrimary"/> for a
    /// tablet or a computer.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="naturalOrientation"/> is not a primary orientation.</exception>
    public ScreenOrientationProvider(OrientationType naturalOrientation)
    {
        if (naturalOrientation is not (OrientationType.PortraitPrimary or OrientationType.LandscapePrimary))
            throw new ArgumentOutOfRangeException(nameof(naturalOrientation), naturalOrientation, "The natural orientation must be a primary orientation");

        NaturalOrientation = naturalOrientation;
        _deviceOrientation = naturalOrientation;
    }

    /// <summary>Raised when the orientation of the screen changes (<see cref="OnOrientationChanged"/>).</summary>
    public event EventHandler? OrientationChanged;

    /// <summary>Gets the natural orientation of the screen (https://w3c.github.io/screen-orientation/#dfn-natural).</summary>
    public OrientationType NaturalOrientation { get; }

    /// <summary>
    /// Gets or sets the orientation of the device, as the user holds it: the orientation of the screen when it is not locked
    /// (or locked to <see cref="OrientationLockType.Any"/>). It is the natural orientation by default. Setting it rotates the
    /// screen, and the documents fire their <c>change</c> events.
    /// </summary>
    public OrientationType DeviceOrientation
    {
        get
        {
            lock (_lock)
            {
                return _deviceOrientation;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_deviceOrientation == value)
                    return;

                _deviceOrientation = value;
            }

            OnOrientationChanged();
        }
    }

    /// <summary>Gets the active orientation lock of the screen (https://w3c.github.io/screen-orientation/#dfn-active-orientation-lock), or null when it is unlocked.</summary>
    public OrientationLockType? ActiveLock
    {
        get
        {
            lock (_lock)
            {
                return _activeLock;
            }
        }
    }

    /// <summary>Gets the current orientation type of the screen (https://w3c.github.io/screen-orientation/#dfn-current-orientation-type).</summary>
    public OrientationType CurrentType
    {
        get
        {
            lock (_lock)
            {
                return GetLockedOrientation(_activeLock, _deviceOrientation, NaturalOrientation);
            }
        }
    }

    /// <summary>Gets the current orientation angle of the screen in degrees (https://w3c.github.io/screen-orientation/#dfn-current-orientation-angle), from the screen orientation values lists.</summary>
    public ushort CurrentAngle => GetAngle(CurrentType, NaturalOrientation);

    /// <summary>
    /// Returns whether the screen can be locked to an orientation (the lock request is rejected with a <c>NotSupportedError</c>
    /// otherwise). The default implementation returns true. It is called on the thread of an event loop, and must not block it.
    /// </summary>
    /// <param name="orientation">The orientation.</param>
    public virtual bool IsSupported(OrientationLockType orientation) => true;

    /// <summary>
    /// Attempts to lock the screen to an orientation, or to unlock it (https://w3c.github.io/screen-orientation/#dfn-lock-the-screen-orientation),
    /// and returns false when the platform refuses (the lock request is rejected with a <c>NotSupportedError</c>). The default
    /// implementation returns true: the screen rotates to the orientation of the lock (see <see cref="CurrentType"/>). It is
    /// called on the thread of an event loop, and must not block it.
    /// </summary>
    /// <param name="orientation">The orientation, or null to unlock the screen.</param>
    protected virtual bool TryLock(OrientationLockType? orientation) => true;

    /// <summary>
    /// Raises <see cref="OrientationChanged"/>, then runs the screen orientation change steps
    /// (https://w3c.github.io/screen-orientation/#dfn-screen-orientation-change-steps) of the documents: they read
    /// <see cref="CurrentType"/> and <see cref="CurrentAngle"/>, and fire their <c>change</c> events. It can be called from any thread.
    /// </summary>
    protected void OnOrientationChanged()
    {
        OrientationChanged?.Invoke(this, EventArgs.Empty);
        List<BrowsingContext> browsingContexts = [];
        lock (_lock)
        {
            foreach (var reference in _browsingContexts)
            {
                if (reference.TryGetTarget(out var browsingContext))
                {
                    browsingContexts.Add(browsingContext);
                }
            }
        }

        foreach (var browsingContext in browsingContexts)
        {
            if (browsingContext.IsDestroyed)
                continue;

            browsingContext.EventLoop.QueueTask(() =>
            {
                if (!browsingContext.IsDestroyed)
                {
                    ScreenOrientationModel.RunChangeSteps(browsingContext.Document, recursive: true);
                }
            });
        }
    }

    /// <summary>https://w3c.github.io/screen-orientation/#dfn-lock-the-screen-orientation: sets the active orientation lock when the attempt succeeds.</summary>
    internal bool ApplyLock(OrientationLockType? orientation)
    {
        var succeeded = TryLock(orientation);
        lock (_lock)
        {
            _activeLock = succeeded ? orientation : null;
        }

        return succeeded;
    }

    /// <summary>Registers a top-level browsing context, whose documents are notified when the orientation changes.</summary>
    internal void Register(BrowsingContext topLevel)
    {
        lock (_lock)
        {
            _browsingContexts.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in _browsingContexts)
            {
                if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, topLevel))
                    return;
            }

            _browsingContexts.Add(new WeakReference<BrowsingContext>(topLevel));
        }
    }

    /// <summary>The orientation of a screen locked to an orientation (or unlocked: null).</summary>
    internal static OrientationType GetLockedOrientation(OrientationLockType? orientation, OrientationType deviceOrientation, OrientationType naturalOrientation) => orientation switch
    {
        OrientationLockType.Natural => naturalOrientation,
        OrientationLockType.Portrait => IsPortrait(deviceOrientation) ? deviceOrientation : OrientationType.PortraitPrimary,
        OrientationLockType.Landscape => IsPortrait(deviceOrientation) ? OrientationType.LandscapePrimary : deviceOrientation,
        OrientationLockType.PortraitPrimary => OrientationType.PortraitPrimary,
        OrientationLockType.PortraitSecondary => OrientationType.PortraitSecondary,
        OrientationLockType.LandscapePrimary => OrientationType.LandscapePrimary,
        OrientationLockType.LandscapeSecondary => OrientationType.LandscapeSecondary,
        _ => deviceOrientation,
    };

    /// <summary>The angle of an orientation (https://w3c.github.io/screen-orientation/#dfn-screen-orientation-values-lists).</summary>
    internal static ushort GetAngle(OrientationType type, OrientationType naturalOrientation)
    {
        var naturalIsPortrait = IsPortrait(naturalOrientation);
        return type switch
        {
            OrientationType.PortraitPrimary => naturalIsPortrait ? (ushort)0 : (ushort)90,
            OrientationType.LandscapePrimary => naturalIsPortrait ? (ushort)90 : (ushort)0,
            OrientationType.PortraitSecondary => naturalIsPortrait ? (ushort)180 : (ushort)270,
            _ => naturalIsPortrait ? (ushort)270 : (ushort)180,
        };
    }

    internal static bool IsPortrait(OrientationType type) => type is OrientationType.PortraitPrimary or OrientationType.PortraitSecondary;
}

/// <summary>
/// The orientation of the screen of a window (https://w3c.github.io/screen-orientation/#screenorientation-interface,
/// <c>screen.orientation</c>), read from the <see cref="ScreenOrientationProvider"/> of the browsing context
/// (<see cref="ScreenOrientationOptions.Provider"/>): its type and angle, its <c>change</c> event, and the orientation locks.
/// </summary>
/// <seealso href="https://w3c.github.io/screen-orientation/#dom-screenorientation"/>
[IdlInterface("ScreenOrientation", "https://w3c.github.io/screen-orientation/#dom-screenorientation")]
public sealed class ScreenOrientation : EventTarget
{
    private readonly Window _window;

    internal ScreenOrientation(Window window)
    {
        _window = window;
        (Type, Angle) = ScreenOrientationModel.GetCurrentOrientation(window);
        if (window.Options.ScreenOrientation?.Provider is { } provider && window.BrowsingContext?.Top is { } top)
        {
            provider.Register(top);
        }
    }

    internal override GlobalScope? RelevantGlobal => _window;

    internal Window Window => _window;

    /// <summary>Gets the last known orientation of the screen (the [[type]] slot).</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-screenorientation-type"/>
    [IdlMember("type", "https://w3c.github.io/screen-orientation/#dom-screenorientation-type")]
    public OrientationType Type { get; internal set; }

    /// <summary>Gets the last known angle of the screen in degrees, from its natural orientation (the [[angle]] slot).</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-screenorientation-angle"/>
    [IdlMember("angle", "https://w3c.github.io/screen-orientation/#dom-screenorientation-angle")]
    public ushort Angle { get; internal set; }

    /// <summary>Gets or sets the event handler of the <c>change</c> event.</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-screenorientation-onchange"/>
    [IdlMember("onchange", "https://w3c.github.io/screen-orientation/#dom-screenorientation-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    /// <summary>
    /// Locks the orientation of the screen (https://w3c.github.io/screen-orientation/#lock-method), with the
    /// <see cref="ScreenOrientationProvider"/> of the browsing context. The task completes once the screen is locked, after
    /// the <c>change</c> event. It fails with an <c>InvalidStateError</c> <see cref="DomException"/> when the document is not
    /// fully active, a <c>SecurityError</c> when it is sandboxed without <c>allow-orientation-lock</c> or hidden, a
    /// <c>NotSupportedError</c> when the screen cannot be locked, a <c>NotAllowedError</c> when the top-level document is not
    /// fullscreen, and an <c>AbortError</c> when another lock request or <see cref="Unlock"/> replaces it, or the document is
    /// unloaded.
    /// </summary>
    /// <param name="orientation">The orientation.</param>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-screenorientation-lock"/>
    [IdlMember("lock", "https://w3c.github.io/screen-orientation/#dom-screenorientation-lock")]
    public Task Lock(OrientationLockType orientation) => ScreenOrientationModel.Lock(_window.Document, orientation);

    /// <summary>
    /// Unlocks the orientation of the screen (https://w3c.github.io/screen-orientation/#unlock-method): the screen gets the
    /// orientation of the device back, and a pending lock request is aborted.
    /// </summary>
    /// <exception cref="DomException">The document is not fully active (<c>InvalidStateError</c>), or is sandboxed or hidden (<c>SecurityError</c>).</exception>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-screenorientation-unlock"/>
    [IdlMember("unlock", "https://w3c.github.io/screen-orientation/#dom-screenorientation-unlock")]
    public void Unlock() => ScreenOrientationModel.Unlock(_window.Document);
}

// The Screen Orientation API (https://w3c.github.io/screen-orientation/#extensions-to-the-screen-interface)
public sealed partial class Screen
{
    private ScreenOrientation? _orientation;

    /// <summary>Gets the orientation of the screen (https://w3c.github.io/screen-orientation/#extensions-to-the-screen-interface).</summary>
    /// <seealso href="https://w3c.github.io/screen-orientation/#dom-screen-orientation"/>
    [IdlMember("orientation", "https://w3c.github.io/screen-orientation/#dom-screen-orientation")]
    public ScreenOrientation Orientation => _orientation ??= new ScreenOrientation(_window);

    internal ScreenOrientation? OrientationOrNull => _orientation;
}

// The Screen Orientation API (https://w3c.github.io/screen-orientation/#extensions-to-the-document-interface)
public partial class Document
{
    /// <summary>The [[orientationPendingPromise]] slot (https://w3c.github.io/screen-orientation/#dfn-orientationpendingpromise).</summary>
    internal TaskCompletionSource? OrientationPendingPromise { get; set; }
}

/// <summary>
/// The algorithms of the Screen Orientation API (https://w3c.github.io/screen-orientation/#algorithms). The attempt to lock
/// the screen, which the specification runs in parallel, runs when <c>lock()</c> is called; the lock completes once the
/// screen has rotated, after a frame.
/// </summary>
internal static class ScreenOrientationModel
{
    /// <summary>The time the screen takes to rotate: the lock request completes after it (a document can be unloaded meanwhile).</summary>
    private static readonly TimeSpan RotationDuration = DomEventLoop.FrameDuration;

    /// <summary>The current orientation type and angle of the screen of a window.</summary>
    public static (OrientationType Type, ushort Angle) GetCurrentOrientation(Window window)
    {
        if (window.Options.ScreenOrientation?.Provider is { } provider)
            return (provider.CurrentType, provider.CurrentAngle);

        // A desktop screen: its natural orientation is the one of its shape
        var screen = window.Screen;
        return (screen.Width >= screen.Height ? OrientationType.LandscapePrimary : OrientationType.PortraitPrimary, 0);
    }

    /// <summary>https://w3c.github.io/screen-orientation/#dfn-common-safety-checks</summary>
    private static DomException? GetSafetyCheckError(Document document)
    {
        if (!document.IsFullyActive)
            return DomException.InvalidState("The document is not fully active");

        if ((document.ActiveSandboxingFlags & SandboxingFlags.OrientationLock) != SandboxingFlags.None)
            return new DomException("The document is sandboxed without allow-orientation-lock", "SecurityError");

        if (document.Hidden)
            return new DomException("The document is hidden", "SecurityError");

        return null;
    }

    /// <summary>https://w3c.github.io/screen-orientation/#dom-screenorientation-lock</summary>
    public static Task Lock(Document document, OrientationLockType orientation)
    {
        // https://wicg.github.io/nav-speculation/prerendering.html#patch-orientation-lock: the request of a prerendering
        // document is made once it is activated (by a task: the prerenderingchange event is fired first)
        if (document.Prerendering)
        {
            var deferred = new TaskCompletionSource();
            document.RunAfterPrerendering(() => document.QueueGlobalTask(() => _ = Lock(document, orientation).ContinueWith(
                task =>
                {
                    if (task.Exception is { } exception)
                    {
                        deferred.TrySetException(exception.InnerExceptions);
                    }
                    else
                    {
                        deferred.TrySetResult();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default)));
            return deferred.Task;
        }

        if (GetSafetyCheckError(document) is { } error)
            return Task.FromException(error);

        if (document.RelevantWindow?.Options.ScreenOrientation?.Provider is not { } provider || !provider.IsSupported(orientation))
            return Task.FromException(DomException.NotSupported($"The screen cannot be locked to the '{GetName(orientation)}' orientation"));

        // https://w3c.github.io/screen-orientation/#interaction-with-fullscreen-api: the pre-lock condition is that the
        // documents are displayed fullscreen (as in Chromium, the top-level document has a fullscreen element)
        if (document.BrowsingContext?.Top.Document is not { FullscreenElementInternal: not null })
            return Task.FromException(DomException.NotAllowed("The screen orientation can only be locked while the page is fullscreen"));

        if (document.OrientationPendingPromise is not null)
        {
            RejectAndNullifyCurrentLockPromise(document, "AbortError");
        }

        var promise = new TaskCompletionSource();
        document.OrientationPendingPromise = promise;
        ApplyOrientationLock(document, provider, orientation);
        return promise.Task;
    }

    /// <summary>https://w3c.github.io/screen-orientation/#dom-screenorientation-unlock</summary>
    public static void Unlock(Document document)
    {
        if (GetSafetyCheckError(document) is { } error)
            throw error;

        if (document.RelevantWindow?.Options.ScreenOrientation?.Provider is not { ActiveLock: not null } provider)
            return;

        if (document.OrientationPendingPromise is not null)
        {
            RejectAndNullifyCurrentLockPromise(document, "AbortError");
        }

        ApplyOrientationLock(document, provider, orientation: null);
    }

    /// <summary>
    /// https://w3c.github.io/screen-orientation/#dfn-fully-unlock-the-screen-orientation-steps: the top-level document is
    /// unloaded, or exits fullscreen.
    /// </summary>
    public static void FullyUnlock(Document document)
    {
        if (document.OrientationPendingPromise is not null)
        {
            RejectAndNullifyCurrentLockPromise(document, "AbortError");
        }

        if (document.BrowsingContext?.Top.Document is not { } topDocument || topDocument.RelevantWindow?.Options.ScreenOrientation?.Provider is not { ActiveLock: not null } provider)
            return;

        ApplyOrientationLock(topDocument, provider, orientation: null);
    }

    /// <summary>
    /// The document stops being fully active (it is unloaded or destroyed): its pending lock request is aborted
    /// (https://w3c.github.io/screen-orientation/#dfn-apply-orientation-lock, step 1), and the screen of a top-level document
    /// is unlocked (https://w3c.github.io/screen-orientation/#handling-unloading-documents).
    /// </summary>
    public static void OnDocumentUnloaded(Document document)
    {
        if (document.BrowsingContext is { Parent: null } browsingContext && ReferenceEquals(browsingContext.Document, document))
        {
            if (document.OrientationPendingPromise is not null)
            {
                RejectAndNullifyCurrentLockPromise(document, "AbortError", unloading: true);
            }

            FullyUnlock(document);
        }
        else if (document.OrientationPendingPromise is not null)
        {
            RejectAndNullifyCurrentLockPromise(document, "AbortError", unloading: true);
        }
    }

    /// <summary>https://w3c.github.io/screen-orientation/#dfn-reject-and-nullify-the-current-lock-promise</summary>
    /// <param name="document">The document.</param>
    /// <param name="exceptionName">The name of the <see cref="DomException"/>.</param>
    /// <param name="unloading">Whether the document is being unloaded: its global tasks would not run anymore.</param>
    private static void RejectAndNullifyCurrentLockPromise(Document document, string exceptionName, bool unloading = false)
    {
        var promise = document.OrientationPendingPromise!;
        document.OrientationPendingPromise = null;
        var exception = new DomException(exceptionName == "AbortError" ? "The screen orientation lock request was aborted" : "The screen cannot be locked to this orientation", exceptionName);

        // The task of a document that is no longer fully active would never run: the promise is rejected by a task of its event loop
        if (document.IsFullyActive && !unloading)
        {
            document.QueueGlobalTask(() => promise.TrySetException(exception));
        }
        else if (document.RelevantWindow is { } window)
        {
            window.EventLoop.QueueTask(() => promise.TrySetException(exception));
        }
    }

    /// <summary>https://w3c.github.io/screen-orientation/#dfn-apply-orientation-lock</summary>
    private static void ApplyOrientationLock(Document document, ScreenOrientationProvider provider, OrientationLockType? orientation)
    {
        if (document.BrowsingContext is not { } browsingContext)
            return;

        // The lock requests of the other documents of the tab are aborted
        var topDocument = browsingContext.Top.Document;
        foreach (var navigable in browsingContext.Top.GetInclusiveDescendantNavigables())
        {
            var doc = navigable.Document;
            if (!ReferenceEquals(doc, document) && doc.OrientationPendingPromise is not null)
            {
                RejectAndNullifyCurrentLockPromise(doc, "AbortError");
            }
        }

        // The attempt to lock (or unlock) the screen
        provider.Register(browsingContext.Top);
        if (!provider.ApplyLock(orientation))
        {
            document.QueueGlobalTask(() =>
            {
                if (document.OrientationPendingPromise is not null)
                {
                    RejectAndNullifyCurrentLockPromise(document, "NotSupportedError");
                }
            });
            return;
        }

        // The screen rotates, then the documents are notified ("change" events, from the top-level document), and the
        // promise is resolved after the events
        if (document.RelevantWindow is not { } window)
            return;

        window.EventLoop.QueueTimer(RotationDuration, () => window.QueueGlobalTask(() =>
        {
            var promise = document.OrientationPendingPromise;
            document.OrientationPendingPromise = null;
            if (topDocument.IsFullyActive)
            {
                RunChangeSteps(topDocument, recursive: true);
            }

            if (promise is not null)
            {
                document.QueueGlobalTask(() => promise.TrySetResult());
            }
        }));
    }

    /// <summary>
    /// https://w3c.github.io/screen-orientation/#dfn-screen-orientation-change-steps: the documents that are fully active and
    /// visible read the orientation of the screen, and fire their <c>change</c> event when it changed (from a task).
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="recursive">Whether the steps run for the documents of the descendant navigables too (the visibility of a document runs the steps for this document only).</param>
    public static void RunChangeSteps(Document document, bool recursive)
    {
        // A window whose ScreenOrientation does not exist yet reads the orientation when it is created
        if (document.IsFullyActive && !document.Hidden && document.RelevantWindow is { ScreenOrNull.OrientationOrNull: { } screenOrientation } window && ReferenceEquals(window.Document, document))
        {
            var (type, angle) = GetCurrentOrientation(window);
            if (type != screenOrientation.Type || angle != screenOrientation.Angle)
            {
                document.QueueGlobalTask(() =>
                {
                    screenOrientation.Angle = angle;
                    screenOrientation.Type = type;
                    screenOrientation.FireEvent(new Event("change"));
                });
            }
        }

        if (!recursive || document.BrowsingContext is not { } browsingContext || !ReferenceEquals(browsingContext.Document, document))
            return;

        foreach (var descendant in browsingContext.GetDescendantNavigables())
        {
            RunChangeSteps(descendant.Document, recursive: false);
        }
    }

    private static string GetName(OrientationLockType orientation) => orientation switch
    {
        OrientationLockType.Any => "any",
        OrientationLockType.Natural => "natural",
        OrientationLockType.Landscape => "landscape",
        OrientationLockType.Portrait => "portrait",
        OrientationLockType.PortraitPrimary => "portrait-primary",
        OrientationLockType.PortraitSecondary => "portrait-secondary",
        OrientationLockType.LandscapePrimary => "landscape-primary",
        _ => "landscape-secondary",
    };
}
