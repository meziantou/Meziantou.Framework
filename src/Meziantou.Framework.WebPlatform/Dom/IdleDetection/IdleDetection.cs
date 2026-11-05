namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The idle state of the user (https://wicg.github.io/idle-detection/#api-useridlestate).</summary>
/// <seealso href="https://wicg.github.io/idle-detection/#dom-useridlestate"/>
[IdlInterface("UserIdleState", "https://wicg.github.io/idle-detection/#dom-useridlestate")]
public enum UserIdleState
{
    /// <summary>The user interacted with the device within the threshold.</summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-useridlestate-active"/>
    [IdlMember("active", "https://wicg.github.io/idle-detection/#dom-useridlestate-active")]
    Active,

    /// <summary>The user did not interact with the device for at least the threshold.</summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-useridlestate-idle"/>
    [IdlMember("idle", "https://wicg.github.io/idle-detection/#dom-useridlestate-idle")]
    Idle,
}

/// <summary>The idle state of the screen (https://wicg.github.io/idle-detection/#api-screenidlestate).</summary>
/// <seealso href="https://wicg.github.io/idle-detection/#dom-screenidlestate"/>
[IdlInterface("ScreenIdleState", "https://wicg.github.io/idle-detection/#dom-screenidlestate")]
public enum ScreenIdleState
{
    /// <summary>A screensaver or a lock screen prevents the content from being seen or interacted with.</summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-screenidlestate-locked"/>
    [IdlMember("locked", "https://wicg.github.io/idle-detection/#dom-screenidlestate-locked")]
    Locked,

    /// <summary>The device displays the content and can be interacted with.</summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-screenidlestate-unlocked"/>
    [IdlMember("unlocked", "https://wicg.github.io/idle-detection/#dom-screenidlestate-unlocked")]
    Unlocked,
}

/// <summary>The host hooks of the Idle Detection API (https://wicg.github.io/idle-detection/, <see cref="BrowsingContextOptions.IdleDetection"/>).</summary>
public sealed class IdleDetectionOptions
{
    /// <summary>
    /// Gets or sets the source of the idle state of the user and of the screen (<c>IdleDetector</c>), e.g. an
    /// <see cref="IdleStateProvider"/> whose <see cref="IdleStateProvider.Status"/> is set by the application, or a subclass
    /// that reads the state of the operating system. A provider can be shared by several browsing contexts. The default is
    /// null: the user is active and the screen is unlocked (<see cref="IdleStatus.Default"/>).
    /// </summary>
    public IdleStateProvider? Provider { get; init; }
}

/// <summary>The idle state of the user and of the screen of the device, returned by an <see cref="IdleStateProvider"/>.</summary>
public sealed record IdleStatus
{
    /// <summary>Gets the status of a user who is interacting with the device, whose screen is unlocked.</summary>
    public static IdleStatus Default { get; } = new();

    /// <summary>
    /// Gets the time of the last interaction of the user with the device, measured by the clock of the browsing context
    /// (<see cref="BrowsingContextOptions.TimeProvider"/>), or null when the user is interacting with it. The user is idle for
    /// an <c>IdleDetector</c> once its threshold elapsed since this time. The default is null.
    /// </summary>
    public DateTimeOffset? LastInteractionTime { get; init; }

    /// <summary>Gets whether a screensaver or a lock screen prevents the content from being seen or interacted with. The default is false.</summary>
    public bool IsScreenLocked { get; init; }
}

/// <summary>
/// The source of the idle state of the user and of the screen (https://wicg.github.io/idle-detection/), read by the
/// <c>IdleDetector</c> objects of the browsing contexts that share a <see cref="BrowsingContextOptions"/>
/// (<see cref="IdleDetectionOptions.Provider"/>). The default implementation returns <see cref="Status"/>, set by the
/// application. Override <see cref="GetStatus"/> to read another source, and call <see cref="OnStatusChanged"/> when the
/// status changes, to update the detectors.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class IdleStateProvider
{
    private readonly Lock _lock = new();
    private readonly List<WeakReference<IdleDetector>> _detectors = [];
    private IdleStatus _status = IdleStatus.Default;

    /// <summary>Raised when the status changes (<see cref="OnStatusChanged"/>).</summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// Gets or sets the status returned by the default implementation of <see cref="GetStatus"/> (<see cref="IdleStatus.Default"/>
    /// by default). Setting it updates the started <c>IdleDetector</c> objects, which fire their <c>change</c> events.
    /// </summary>
    public IdleStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_lock)
            {
                _status = value;
            }

            OnStatusChanged();
        }
    }

    /// <summary>Returns the idle status. The default implementation returns <see cref="Status"/>. It is called on the thread of an event loop, and must not block it.</summary>
    public virtual IdleStatus GetStatus() => Status;

    /// <summary>
    /// Raises <see cref="StatusChanged"/>, then reads the status again for the started <c>IdleDetector</c> objects, which
    /// fire their <c>change</c> events. It can be called from any thread.
    /// </summary>
    protected void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
        List<IdleDetector> detectors = [];
        lock (_lock)
        {
            foreach (var reference in _detectors)
            {
                if (reference.TryGetTarget(out var detector))
                {
                    detectors.Add(detector);
                }
            }
        }

        foreach (var detector in detectors)
        {
            detector.OnStatusChanged();
        }
    }

    /// <summary>Registers a started <c>IdleDetector</c>, to update it.</summary>
    internal void Register(IdleDetector detector)
    {
        lock (_lock)
        {
            _detectors.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, detector));
            _detectors.Add(new WeakReference<IdleDetector>(detector));
        }
    }

    /// <summary>Unregisters a stopped <c>IdleDetector</c>.</summary>
    internal void Unregister(IdleDetector detector)
    {
        lock (_lock)
        {
            _detectors.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, detector));
        }
    }
}

/// <summary>The options of <see cref="IdleDetector.Start"/> (https://wicg.github.io/idle-detection/#api-idleoptions).</summary>
/// <seealso href="https://wicg.github.io/idle-detection/#dom-idleoptions"/>
[IdlInterface("IdleOptions", "https://wicg.github.io/idle-detection/#dom-idleoptions")]
public sealed class IdleOptions
{
    /// <summary>Gets or sets the time without interaction after which the user is idle, in milliseconds (at least 60,000, the default).</summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-idleoptions-threshold"/>
    [IdlMember("threshold", "https://wicg.github.io/idle-detection/#dom-idleoptions-threshold")]
    public ulong? Threshold { get; set; }

    /// <summary>Gets or sets the signal that stops the detector.</summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-idleoptions-signal"/>
    [IdlMember("signal", "https://wicg.github.io/idle-detection/#dom-idleoptions-signal")]
    public AbortSignal? Signal { get; set; }
}

/// <summary>
/// Observes the idle state of the user and of the screen (https://wicg.github.io/idle-detection/#api-idledetector), read
/// from the <see cref="IdleStateProvider"/> of the browsing context (<see cref="IdleDetectionOptions.Provider"/>): the
/// <c>change</c> event is fired when the state changes, once the detector is started.
/// </summary>
/// <seealso href="https://wicg.github.io/idle-detection/#dom-idledetector"/>
[IdlInterface("IdleDetector", "https://wicg.github.io/idle-detection/#dom-idledetector")]
public sealed class IdleDetector : EventTarget
{
    private const string PermissionName = "idle-detection";
    private const ulong MinimumThreshold = 60_000;

    private readonly GlobalScope _global;
    private DetectorState _state;
    private TimeSpan _threshold;

    /// <summary>The version of the timer that makes the user idle: a timer only runs while its version is the current one.</summary>
    private int _timerVersion;

    /// <summary>Creates a detector for the current global object (https://wicg.github.io/idle-detection/#api-idledetector).</summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-idledetector-constructor"/>
    [IdlMember("constructor", "https://wicg.github.io/idle-detection/#dom-idledetector-constructor")]
    public IdleDetector()
        : this(GlobalScope.Current ?? throw new InvalidOperationException("An idle detector can only be created by a window or a worker"))
    {
    }

    /// <summary>Creates a detector for a global object.</summary>
    /// <param name="global">The window or the dedicated worker whose detector it is.</param>
    [NotInIdl("The .NET constructor whose global object is explicit")]
    public IdleDetector(GlobalScope global)
    {
        ArgumentNullException.ThrowIfNull(global);
        _global = global;
    }

    private enum DetectorState
    {
        Stopped,
        Starting,
        Started,
    }

    internal override GlobalScope? RelevantGlobal => _global;

    /// <summary>Gets the last known idle state of the user, or null before the detector is started.</summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-idledetector-userstate"/>
    [IdlMember("userState", "https://wicg.github.io/idle-detection/#dom-idledetector-userstate")]
    public UserIdleState? UserState { get; private set; }

    /// <summary>Gets the last known idle state of the screen, or null before the detector is started.</summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-idledetector-screenstate"/>
    [IdlMember("screenState", "https://wicg.github.io/idle-detection/#dom-idledetector-screenstate")]
    public ScreenIdleState? ScreenState { get; private set; }

    /// <summary>Gets or sets the event handler of the <c>change</c> event.</summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-idledetector-onchange"/>
    [IdlMember("onchange", "https://wicg.github.io/idle-detection/#dom-idledetector-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    /// <summary>
    /// Asks the user to allow the origin to detect the idle state (https://wicg.github.io/idle-detection/#api-idledetector-requestpermission),
    /// with <see cref="BrowsingContextOptions.RequestPermission"/>, unless the permission is already granted or denied. The
    /// task fails with an <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully active, and with
    /// a <c>NotAllowedError</c> when the window has no transient activation.
    /// </summary>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-idledetector-requestpermission"/>
    [IdlMember("requestPermission", "https://wicg.github.io/idle-detection/#dom-idledetector-requestpermission")]
    public static Task<PermissionState> RequestPermission()
    {
        if (GlobalScope.Current is not Window window || !window.Document.IsFullyActive)
            return Task.FromException<PermissionState>(DomException.InvalidState("The document is not fully active"));

        if (!window.HasTransientActivation)
            return Task.FromException<PermissionState>(DomException.NotAllowed("The window has no transient activation"));

        var result = new TaskCompletionSource<PermissionState>();
        window.QueueGlobalTask(() => result.TrySetResult(Permissions.RequestPermissionToUse(window, PermissionName)));
        return result.Task;
    }

    /// <summary>
    /// Starts the detector (https://wicg.github.io/idle-detection/#api-idledetector-start): the <c>change</c> event is fired
    /// with the initial state, then at each change. The task fails with an <c>InvalidStateError</c> <see cref="DomException"/>
    /// when the document is not fully active or the detector is already started, a <c>NotAllowedError</c> when the document
    /// is not allowed to use the <c>idle-detection</c> feature or the <c>idle-detection</c> permission is denied, a
    /// <see cref="TypeErrorException"/> when the threshold is less than 60 seconds, and the abort reason of the
    /// signal when it is aborted.
    /// </summary>
    /// <param name="options">The threshold, and the signal that stops the detector.</param>
    /// <seealso href="https://wicg.github.io/idle-detection/#dom-idledetector-start"/>
    [IdlMember("start", "https://wicg.github.io/idle-detection/#dom-idledetector-start")]
    public Task Start(IdleOptions? options = null)
    {
        // Steps 1-3: the documents of a dedicated worker are the documents that own it
        var documents = PressureObserver.GetOwningDocumentSet(_global).ToArray();
        if (documents.Length == 0 || documents.Any(document => !document.IsFullyActive))
            return Task.FromException(DomException.InvalidState("The document is not fully active"));

        if (documents.Any(document => !PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.IdleDetection)))
            return Task.FromException(DomException.NotAllowed("The document is not allowed to use the idle-detection feature"));

        // Steps 4-6
        if (_state != DetectorState.Stopped)
            return Task.FromException(DomException.InvalidState("The detector is already started"));

        var threshold = options?.Threshold ?? MinimumThreshold;
        if (threshold < MinimumThreshold)
            return Task.FromException(new TypeErrorException("The threshold must be at least 60000 milliseconds"));

        _state = DetectorState.Starting;

        // Steps 7-8
        var result = new TaskCompletionSource();
        if (options?.Signal is { } signal)
        {
            if (signal.Aborted)
            {
                _state = DetectorState.Stopped;
                return Task.FromException(ToException(signal.AbortReason));
            }

            signal.AddAbortAlgorithm(() =>
            {
                Stop();
                result.TrySetException(ToException(signal.AbortReason));
            });
        }

        // Step 9: the permission is read in parallel, then a task starts the detector
        _global.QueueGlobalTask(() =>
        {
            if (Permissions.GetPermissionState(_global, PermissionName) == PermissionState.Denied)
            {
                _state = DetectorState.Stopped;
                result.TrySetException(DomException.NotAllowed("The idle-detection permission is denied"));
                return;
            }

            if (_state == DetectorState.Stopped)
                return;

            _state = DetectorState.Started;
            _threshold = TimeSpan.FromMilliseconds(threshold);

            // As in Chromium, the initial state is known when the promise is resolved, and reported by a change event
            // fired by the next task; then the detector reacts to the changes
            Update(fireEvent: false);
            result.TrySetResult();
            _global.Options.IdleDetection?.Provider?.Register(this);
            _global.QueueGlobalTask(() =>
            {
                if (_state == DetectorState.Started)
                {
                    FireEvent(new Event("change"));
                }
            });
        });
        return result.Task;
    }

    /// <summary>The status of the provider changed: it is read again by a task. Called from any thread.</summary>
    internal void OnStatusChanged() => _global.QueueGlobalTask(() => Update(fireEvent: true));

    private static Exception ToException(object? reason) => reason as Exception ?? new AbortSignalException(reason);

    private void Stop()
    {
        _state = DetectorState.Stopped;
        _timerVersion++;
        _global.Options.IdleDetection?.Provider?.Unregister(this);
    }

    /// <summary>
    /// https://wicg.github.io/idle-detection/#api-idledetector-reacting: the user and screen states are computed from the
    /// status of the provider, and the <c>change</c> event is fired when one of them changed. While the user is active, a
    /// timer makes them idle once the threshold elapsed since their last interaction.
    /// </summary>
    private void Update(bool fireEvent)
    {
        if (_state != DetectorState.Started)
            return;

        var status = IdleStatus.Default;
        if (_global.Options.IdleDetection?.Provider is { } provider)
        {
            try
            {
                status = provider.GetStatus();
            }
#pragma warning disable CA1031 // An error of the provider keeps the last state
            catch (Exception)
#pragma warning restore CA1031
            {
                return;
            }
        }

        var userState = UserIdleState.Active;
        var timerVersion = ++_timerVersion;
        if (status.LastInteractionTime is { } lastInteraction)
        {
            var remaining = lastInteraction + _threshold - _global.EventLoop.TimeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                userState = UserIdleState.Idle;
            }
            else
            {
                _global.EventLoop.QueueTimer(remaining, () => _global.QueueGlobalTask(() =>
                {
                    if (_timerVersion == timerVersion)
                    {
                        Update(fireEvent: true);
                    }
                }));
            }
        }

        var screenState = status.IsScreenLocked ? ScreenIdleState.Locked : ScreenIdleState.Unlocked;
        if (UserState == userState && ScreenState == screenState)
            return;

        UserState = userState;
        ScreenState = screenState;
        if (fireEvent)
        {
            FireEvent(new Event("change"));
        }
    }
}
