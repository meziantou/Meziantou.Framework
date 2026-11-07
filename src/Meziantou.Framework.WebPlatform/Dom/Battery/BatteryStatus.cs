using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Battery Status API (https://w3c.github.io/battery/, <see cref="BrowsingContextOptions.BatteryStatus"/>).</summary>
public sealed class BatteryStatusOptions
{
    /// <summary>
    /// Gets or sets the source of the battery status of the device (<c>navigator.getBattery()</c>), e.g. a
    /// <see cref="BatteryStatusProvider"/> whose <see cref="BatteryStatusProvider.Status"/> is set by the application, or a
    /// subclass that reads the battery of the operating system. A provider can be shared by several browsing contexts. The
    /// default is null: the user agent is unable to report the battery status, so the <c>BatteryManager</c> reports a fully
    /// charged battery that is plugged in (<see cref="BatteryStatus.Default"/>).
    /// </summary>
    public BatteryStatusProvider? Provider { get; init; }
}

/// <summary>
/// The state of the battery of the device (https://w3c.github.io/battery/#the-batterymanager-interface), returned by a
/// <see cref="BatteryStatusProvider"/>.
/// </summary>
public sealed record BatteryStatus
{
    /// <summary>Gets the status of a fully charged battery that is plugged in (the status reported when the user agent is unable to report it).</summary>
    public static BatteryStatus Default { get; } = new();

    /// <summary>Gets whether the battery is charging (or there is no battery). The default is true.</summary>
    public bool Charging { get; init; } = true;

    /// <summary>
    /// Gets the level of the battery, between 0 (depleted) and 1 (full). The default is 1. The documents see it rounded to
    /// two decimals, to reduce fingerprinting.
    /// </summary>
    public double Level
    {
        get;
        init
        {
            if (!double.IsFinite(value) || value is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The level must be between 0 and 1");

            field = value;
        }
    } = 1;

    /// <summary>Gets the remaining time until the battery is fully charged (0 when it is full), or null when it is unknown or the battery is discharging. The default is 0.</summary>
    public TimeSpan? ChargingTime { get; init; } = TimeSpan.Zero;

    /// <summary>Gets the remaining time until the battery is depleted, or null when it is unknown or the battery is charging. The default is null.</summary>
    public TimeSpan? DischargingTime { get; init; }
}

/// <summary>
/// The source of the battery status of the device (https://w3c.github.io/battery/), read by <c>navigator.getBattery()</c>, of
/// the browsing contexts that share a <see cref="BrowsingContextOptions"/> (<see cref="BatteryStatusOptions.Provider"/>).
/// The default implementation returns <see cref="Status"/>, set by the application (e.g. to simulate a battery). Override
/// <see cref="GetStatus"/> to read another source, and call <see cref="OnStatusChanged"/> when the status changes, to update
/// the <c>BatteryManager</c> objects of the documents.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class BatteryStatusProvider
{
    private readonly Lock _lock = new();
    private readonly List<WeakReference<BatteryManager>> _managers = [];
    private BatteryStatus _status = BatteryStatus.Default;

    /// <summary>Raised when the status changes (<see cref="OnStatusChanged"/>).</summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// Gets or sets the status returned by the default implementation of <see cref="GetStatus"/> (<see cref="BatteryStatus.Default"/>
    /// by default). Setting it updates the <c>BatteryManager</c> objects of the documents, which fire their change events.
    /// </summary>
    public BatteryStatus Status
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

    /// <summary>Returns the status of the battery. The default implementation returns <see cref="Status"/>. It is called on the thread of an event loop, and must not block it.</summary>
    public virtual BatteryStatus GetStatus() => Status;

    /// <summary>
    /// Raises <see cref="StatusChanged"/>, then reads the status again for the <c>BatteryManager</c> objects of the documents,
    /// which fire their change events. It can be called from any thread.
    /// </summary>
    protected void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
        List<BatteryManager> managers = [];
        lock (_lock)
        {
            foreach (var reference in _managers)
            {
                if (reference.TryGetTarget(out var manager))
                {
                    managers.Add(manager);
                }
            }
        }

        foreach (var manager in managers)
        {
            manager.OnStatusChanged();
        }
    }

    /// <summary>Registers a <c>BatteryManager</c>, to update it.</summary>
    internal void Register(BatteryManager manager)
    {
        lock (_lock)
        {
            _managers.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _managers.Add(new WeakReference<BatteryManager>(manager));
        }
    }
}

/// <summary>
/// The battery status of the device (https://w3c.github.io/battery/#the-batterymanager-interface), returned by
/// <see cref="Navigator.GetBattery"/>: it is read from the <see cref="BatteryStatusProvider"/> of the browsing context
/// (<see cref="BatteryStatusOptions.Provider"/>), and fires the <c>chargingchange</c>, <c>chargingtimechange</c>,
/// <c>dischargingtimechange</c> and <c>levelchange</c> events when it changes, while its document is visible.
/// </summary>
/// <seealso href="https://w3c.github.io/battery/#dom-batterymanager"/>
[IdlInterface("BatteryManager", "https://w3c.github.io/battery/#dom-batterymanager")]
public sealed class BatteryManager : EventTarget
{
    private readonly Window _window;
    private bool _changedWhileHidden;

    internal BatteryManager(Window window)
    {
        _window = window;
        if (window.Options.BatteryStatus?.Provider is { } provider)
        {
            Update(provider.GetStatus(), fireEvents: false);
            provider.Register(this);
        }
    }

    internal override GlobalScope? RelevantGlobal => _window;

    /// <summary>Gets whether the battery is charging (or there is no battery).</summary>
    /// <seealso href="https://w3c.github.io/battery/#dom-batterymanager-charging"/>
    [IdlMember("charging", "https://w3c.github.io/battery/#dom-batterymanager-charging")]
    public bool Charging { get; private set; } = true;

    /// <summary>Gets the remaining time in seconds until the battery is fully charged (0 when it is full), or positive infinity.</summary>
    /// <seealso href="https://w3c.github.io/battery/#dom-batterymanager-chargingtime"/>
    [IdlMember("chargingTime", "https://w3c.github.io/battery/#dom-batterymanager-chargingtime")]
    public double ChargingTime { get; private set; }

    /// <summary>Gets the remaining time in seconds until the battery is depleted, or positive infinity.</summary>
    /// <seealso href="https://w3c.github.io/battery/#dom-batterymanager-dischargingtime"/>
    [IdlMember("dischargingTime", "https://w3c.github.io/battery/#dom-batterymanager-dischargingtime")]
    public double DischargingTime { get; private set; } = double.PositiveInfinity;

    /// <summary>Gets the level of the battery, between 0 and 1, rounded to two decimals.</summary>
    /// <seealso href="https://w3c.github.io/battery/#dom-batterymanager-level"/>
    [IdlMember("level", "https://w3c.github.io/battery/#dom-batterymanager-level")]
    public double Level { get; private set; } = 1;

    /// <summary>Gets or sets the event handler of the <c>chargingchange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/battery/#dom-batterymanager-onchargingchange"/>
    [IdlMember("onchargingchange", "https://w3c.github.io/battery/#dom-batterymanager-onchargingchange")]
    public Action<Event>? OnChargingChange
    {
        get => GetEventHandler("chargingchange");
        set => SetEventHandler("chargingchange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>chargingtimechange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/battery/#dom-batterymanager-onchargingtimechange"/>
    [IdlMember("onchargingtimechange", "https://w3c.github.io/battery/#dom-batterymanager-onchargingtimechange")]
    public Action<Event>? OnChargingTimeChange
    {
        get => GetEventHandler("chargingtimechange");
        set => SetEventHandler("chargingtimechange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>dischargingtimechange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/battery/#dom-batterymanager-ondischargingtimechange"/>
    [IdlMember("ondischargingtimechange", "https://w3c.github.io/battery/#dom-batterymanager-ondischargingtimechange")]
    public Action<Event>? OnDischargingTimeChange
    {
        get => GetEventHandler("dischargingtimechange");
        set => SetEventHandler("dischargingtimechange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>levelchange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/battery/#dom-batterymanager-onlevelchange"/>
    [IdlMember("onlevelchange", "https://w3c.github.io/battery/#dom-batterymanager-onlevelchange")]
    public Action<Event>? OnLevelChange
    {
        get => GetEventHandler("levelchange");
        set => SetEventHandler("levelchange", value);
    }

    /// <summary>
    /// The status of the provider changed (https://w3c.github.io/battery/#dfn-update-the-battery-status-and-notify): the
    /// attributes are updated by a task, and the change events are fired, unless the document is hidden (the browsers do not
    /// notify the hidden pages: the status is read again when the document becomes visible). Called from any thread.
    /// </summary>
    internal void OnStatusChanged()
    {
        _window.QueueGlobalTask(() =>
        {
            if (_window.Document.Hidden)
            {
                _changedWhileHidden = true;
                return;
            }

            ReadStatus();
        });
    }

    /// <summary>The document became visible: the status that changed while it was hidden is read again.</summary>
    internal void OnDocumentVisible()
    {
        if (!_changedWhileHidden)
            return;

        _changedWhileHidden = false;
        ReadStatus();
    }

    private void ReadStatus()
    {
        if (_window.Options.BatteryStatus?.Provider is not { } provider)
            return;

        BatteryStatus status;
        try
        {
            status = provider.GetStatus();
        }
#pragma warning disable CA1031 // An error of the provider keeps the last status
        catch (Exception)
#pragma warning restore CA1031
        {
            return;
        }

        Update(status, fireEvents: true);
    }

    private void Update(BatteryStatus status, bool fireEvents)
    {
        var chargingTime = status.ChargingTime is { } charging ? Math.Max(0, charging.TotalSeconds) : double.PositiveInfinity;
        var dischargingTime = status.DischargingTime is { } discharging ? Math.Max(0, discharging.TotalSeconds) : double.PositiveInfinity;

        // The level is reported with a restricted precision, to reduce fingerprinting (as the browsers)
        var level = Math.Round(status.Level, 2, MidpointRounding.AwayFromZero);
        var changes = new List<string>(4);
        if (Charging != status.Charging)
        {
            Charging = status.Charging;
            changes.Add("chargingchange");
        }

        if (ChargingTime != chargingTime)
        {
            ChargingTime = chargingTime;
            changes.Add("chargingtimechange");
        }

        if (DischargingTime != dischargingTime)
        {
            DischargingTime = dischargingTime;
            changes.Add("dischargingtimechange");
        }

        if (Level != level)
        {
            Level = level;
            changes.Add("levelchange");
        }

        if (!fireEvents)
            return;

        foreach (var type in changes)
        {
            FireEvent(new Event(type));
        }
    }
}

// The Battery Status API (https://w3c.github.io/battery/)
public sealed partial class Navigator
{
    private TaskCompletionSource<BatteryManager>? _batteryPromise;
    private BatteryManager? _batteryManager;

    internal BatteryManager? BatteryManagerOrNull => _batteryManager;

    /// <summary>
    /// Returns the battery status of the device (https://w3c.github.io/battery/#the-getbattery-method), read from
    /// <see cref="BatteryStatusOptions.Provider"/>. It returns the same task at each call. The task fails with a
    /// <c>NotAllowedError</c> <see cref="DomException"/> when the document is not allowed to use the <c>battery</c> feature.
    /// </summary>
    /// <seealso href="https://w3c.github.io/battery/#dom-navigator-getbattery"/>
    [IdlMember("getBattery", "https://w3c.github.io/battery/#dom-navigator-getbattery")]
    public Task<BatteryManager> GetBattery()
    {
        if (_batteryPromise is { } existing)
            return existing.Task;

        var promise = new TaskCompletionSource<BatteryManager>();
        _batteryPromise = promise;

        // The promise is a slot of the navigator: it is the same promise object at each call
        PromiseSlots.Register(promise.Task);
        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(_window.Document, PolicyControlledFeature.Battery))
        {
            promise.TrySetException(DomException.NotAllowed("The document is not allowed to use the battery feature"));
        }
        else if (_window.Document.Prerendering)
        {
            // https://wicg.github.io/nav-speculation/prerendering.html#patch-battery: the promise is resolved once the page is activated
            _window.Document.RunAfterPrerendering(() =>
            {
                _batteryManager ??= new BatteryManager(_window);
                promise.TrySetResult(_batteryManager);
            });
        }
        else
        {
            _batteryManager ??= new BatteryManager(_window);
            promise.TrySetResult(_batteryManager);
        }

        return promise.Task;
    }
}
