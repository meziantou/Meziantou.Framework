namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A posture of the device (https://w3c.github.io/device-posture/#the-deviceposturetype-enum).</summary>
/// <seealso href="https://w3c.github.io/device-posture/#dom-deviceposturetype"/>
[IdlInterface("DevicePostureType", "https://w3c.github.io/device-posture/#dom-deviceposturetype")]
public enum DevicePostureType
{
    /// <summary>The screen is flat (a device that does not fold, or a foldable device that is fully unfolded).</summary>
    /// <seealso href="https://w3c.github.io/device-posture/#dom-deviceposturetype-continuous"/>
    [IdlMember("continuous", "https://w3c.github.io/device-posture/#dom-deviceposturetype-continuous")]
    Continuous,

    /// <summary>The device is folded (e.g. used as a book or a laptop).</summary>
    /// <seealso href="https://w3c.github.io/device-posture/#dom-deviceposturetype-folded"/>
    [IdlMember("folded", "https://w3c.github.io/device-posture/#dom-deviceposturetype-folded")]
    Folded,
}

/// <summary>The host hooks of the Device Posture API (https://w3c.github.io/device-posture/, <see cref="BrowsingContextOptions.DevicePosture"/>).</summary>
public sealed class DevicePostureOptions
{
    /// <summary>
    /// Gets or sets the source of the posture of the device (<c>navigator.devicePosture</c> and the <c>device-posture</c>
    /// media feature), e.g. a <see cref="DevicePostureProvider"/> whose <see cref="DevicePostureProvider.Posture"/> is set by
    /// the application, or a subclass that reads the hinge angle of the device. A provider can be shared by several browsing
    /// contexts. The default is null: the posture is <see cref="DevicePostureType.Continuous"/>.
    /// </summary>
    public DevicePostureProvider? Provider { get; init; }
}

/// <summary>
/// The source of the posture of the device (https://w3c.github.io/device-posture/#dfn-calculate-the-device-posture-information),
/// read by the documents of the browsing contexts that share a <see cref="BrowsingContextOptions"/>
/// (<see cref="DevicePostureOptions.Provider"/>). The default implementation returns <see cref="Posture"/>, set by the
/// application. Override <see cref="GetPosture"/> to read another source, and call <see cref="OnPostureChanged"/> when the
/// posture changes, to update the documents.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class DevicePostureProvider
{
    private readonly Lock _lock = new();
    private readonly List<WeakReference<DevicePosture>> _postures = [];
    private readonly List<WeakReference<DomEventLoop>> _eventLoops = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DomEventLoop, object> _registeredEventLoops = [];
    private DevicePostureType _posture;

    /// <summary>Raised when the posture changes (<see cref="OnPostureChanged"/>).</summary>
    public event EventHandler? PostureChanged;

    /// <summary>
    /// Gets or sets the posture returned by the default implementation of <see cref="GetPosture"/>
    /// (<see cref="DevicePostureType.Continuous"/> by default). Setting it updates the documents: the <c>DevicePosture</c>
    /// objects of the visible documents fire their <c>change</c> events, and the media queries are evaluated again.
    /// </summary>
    public DevicePostureType Posture
    {
        get
        {
            lock (_lock)
            {
                return _posture;
            }
        }
        set
        {
            lock (_lock)
            {
                _posture = value;
            }

            OnPostureChanged();
        }
    }

    /// <summary>Returns the posture of the device. The default implementation returns <see cref="Posture"/>. It is called on the thread of an event loop, and must not block it.</summary>
    public virtual DevicePostureType GetPosture() => Posture;

    /// <summary>
    /// Raises <see cref="PostureChanged"/>, then runs the device posture change steps (https://w3c.github.io/device-posture/#dfn-device-posture-change-steps)
    /// for the documents, and evaluates their media queries again. It can be called from any thread.
    /// </summary>
    protected void OnPostureChanged()
    {
        PostureChanged?.Invoke(this, EventArgs.Empty);
        List<DevicePosture> postures = [];
        List<DomEventLoop> eventLoops = [];
        lock (_lock)
        {
            foreach (var reference in _postures)
            {
                if (reference.TryGetTarget(out var posture))
                {
                    postures.Add(posture);
                }
            }

            foreach (var reference in _eventLoops)
            {
                if (reference.TryGetTarget(out var eventLoop))
                {
                    eventLoops.Add(eventLoop);
                }
            }
        }

        foreach (var posture in postures)
        {
            posture.OnPostureChanged();
        }

        // The device-posture media feature changed: the media query lists are evaluated by the next rendering update
        foreach (var eventLoop in eventLoops)
        {
            eventLoop.QueueTask(eventLoop.RequestRenderingUpdate);
        }
    }

    /// <summary>Registers a <c>DevicePosture</c> object, to update it.</summary>
    internal void Register(DevicePosture posture)
    {
        lock (_lock)
        {
            _postures.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _postures.Add(new WeakReference<DevicePosture>(posture));
        }
    }

    /// <summary>Registers the event loop of a document whose environment reads the posture, to update the rendering when it changes.</summary>
    internal void Register(DomEventLoop eventLoop)
    {
        if (_registeredEventLoops.TryGetValue(eventLoop, out _))
            return;

        lock (_lock)
        {
            if (!_registeredEventLoops.TryAdd(eventLoop, eventLoop))
                return;

            _eventLoops.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _eventLoops.Add(new WeakReference<DomEventLoop>(eventLoop));
        }
    }
}

/// <summary>
/// The posture of the device for a document (https://w3c.github.io/device-posture/#the-deviceposture-interface,
/// <c>navigator.devicePosture</c>), read from the <see cref="DevicePostureProvider"/> of the browsing context
/// (<see cref="DevicePostureOptions.Provider"/>): the <c>change</c> event is fired when it changes, while the document is
/// visible.
/// </summary>
/// <seealso href="https://w3c.github.io/device-posture/#dom-deviceposture"/>
[IdlInterface("DevicePosture", "https://w3c.github.io/device-posture/#dom-deviceposture")]
public sealed class DevicePosture : EventTarget
{
    private readonly Window _window;

    internal DevicePosture(Window window)
    {
        _window = window;

        // The [[CurrentPosture]] slot of the document is initialized when it is first read
        Type = GetPosture(window.BrowsingContext);
        window.Options.DevicePosture?.Provider?.Register(this);
    }

    internal override GlobalScope? RelevantGlobal => _window;

    /// <summary>Gets the current posture of the device (the [[CurrentPosture]] slot of the document).</summary>
    /// <seealso href="https://w3c.github.io/device-posture/#dom-deviceposture-type"/>
    [IdlMember("type", "https://w3c.github.io/device-posture/#dom-deviceposture-type")]
    public DevicePostureType Type { get; private set; }

    /// <summary>Gets or sets the event handler of the <c>change</c> event.</summary>
    /// <seealso href="https://w3c.github.io/device-posture/#dom-deviceposture-onchange"/>
    [IdlMember("onchange", "https://w3c.github.io/device-posture/#dom-deviceposture-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }

    /// <summary>
    /// https://w3c.github.io/device-posture/#dfn-calculate-the-device-posture-information: the posture of the provider of the
    /// browsing context (the navigables reflect the posture of their top-level traversable), or continuous.
    /// </summary>
    internal static DevicePostureType GetPosture(BrowsingContext? browsingContext)
    {
        if (browsingContext?.Options.DevicePosture?.Provider is not { } provider)
            return DevicePostureType.Continuous;

        provider.Register(browsingContext.EventLoop);
        try
        {
            return provider.GetPosture();
        }
#pragma warning disable CA1031 // An error of the provider is the default posture
        catch (Exception)
#pragma warning restore CA1031
        {
            return DevicePostureType.Continuous;
        }
    }

    /// <summary>The posture of the provider changed: the device posture change steps run by a task. Called from any thread.</summary>
    internal void OnPostureChanged() => _window.QueueGlobalTask(RunChangeSteps);

    /// <summary>
    /// https://w3c.github.io/device-posture/#dfn-device-posture-change-steps: the change event is fired when the posture
    /// differs from the current posture of the document, unless it is hidden (the steps run again when it becomes visible).
    /// </summary>
    internal void RunChangeSteps()
    {
        var document = _window.Document;
        if (!document.IsFullyActive || document.Hidden)
            return;

        var posture = GetPosture(_window.BrowsingContext);
        if (posture == Type)
            return;

        Type = posture;
        FireEvent(new Event("change"));
    }
}

// The Device Posture API (https://w3c.github.io/device-posture/)
public sealed partial class Navigator
{
    private DevicePosture? _devicePosture;

    /// <summary>Gets the posture of the device (https://w3c.github.io/device-posture/#extensions-to-the-navigator-interface).</summary>
    /// <seealso href="https://w3c.github.io/device-posture/#dom-navigator-deviceposture"/>
    [IdlMember("devicePosture", "https://w3c.github.io/device-posture/#dom-navigator-deviceposture")]
    public DevicePosture DevicePosture => _devicePosture ??= new DevicePosture(_window);

    internal DevicePosture? DevicePostureOrNull => _devicePosture;
}
