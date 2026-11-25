namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The mapping of the buttons and the axes of a gamepad (https://w3c.github.io/gamepad/#dom-gamepadmappingtype).</summary>
/// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadmappingtype"/>
[IdlInterface("GamepadMappingType", "https://w3c.github.io/gamepad/#dom-gamepadmappingtype")]
public enum GamepadMappingType
{
    /// <summary>No mapping: the buttons and the axes are in the order of the device.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadmappingtype"/>
    [IdlMember("", "https://w3c.github.io/gamepad/#dom-gamepadmappingtype")]
    None,

    /// <summary>The buttons and the axes are in the order of the standard gamepad (https://w3c.github.io/gamepad/#remapping).</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadmappingtype-standard"/>
    [IdlMember("standard", "https://w3c.github.io/gamepad/#dom-gamepadmappingtype-standard")]
    Standard,

    /// <summary>The mapping of the XR controllers of the WebXR Gamepads Module, which <c>getGamepads()</c> never reports.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadmappingtype-xr-standard"/>
    [IdlMember("xr-standard", "https://w3c.github.io/gamepad/#dom-gamepadmappingtype-xr-standard")]
    XrStandard,
}

/// <summary>The result of a haptic effect (https://w3c.github.io/gamepad/#dom-gamepadhapticsresult).</summary>
/// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadhapticsresult"/>
[IdlInterface("GamepadHapticsResult", "https://w3c.github.io/gamepad/#dom-gamepadhapticsresult")]
public enum GamepadHapticsResult
{
    /// <summary>The effect completed.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadhapticsresult-complete"/>
    [IdlMember("complete", "https://w3c.github.io/gamepad/#dom-gamepadhapticsresult-complete")]
    Complete,

    /// <summary>The effect was stopped or replaced by another one.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadhapticsresult-preempted"/>
    [IdlMember("preempted", "https://w3c.github.io/gamepad/#dom-gamepadhapticsresult-preempted")]
    Preempted,
}

/// <summary>The type of a haptic effect (https://w3c.github.io/gamepad/#dom-gamepadhapticeffecttype).</summary>
/// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadhapticeffecttype"/>
[IdlInterface("GamepadHapticEffectType", "https://w3c.github.io/gamepad/#dom-gamepadhapticeffecttype")]
public enum GamepadHapticEffectType
{
    /// <summary>A vibration of the motors of the two handles.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadhapticeffecttype-dual-rumble"/>
    [IdlMember("dual-rumble", "https://w3c.github.io/gamepad/#dom-gamepadhapticeffecttype-dual-rumble")]
    DualRumble,

    /// <summary>A vibration of the motors of the two handles and of the two triggers.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadhapticeffecttype-trigger-rumble"/>
    [IdlMember("trigger-rumble", "https://w3c.github.io/gamepad/#dom-gamepadhapticeffecttype-trigger-rumble")]
    TriggerRumble,
}

/// <summary>The parameters of a haptic effect (https://w3c.github.io/gamepad/#dom-gamepadeffectparameters).</summary>
/// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadeffectparameters"/>
[IdlInterface("GamepadEffectParameters", "https://w3c.github.io/gamepad/#dom-gamepadeffectparameters")]
public sealed class GamepadEffectParameters
{
    /// <summary>Gets the duration of the vibration, in milliseconds.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-duration"/>
    [IdlMember("duration", "https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-duration")]
    public ulong Duration { get; init; }

    /// <summary>Gets the delay before the vibration starts, in milliseconds.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-startdelay"/>
    [IdlMember("startDelay", "https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-startdelay")]
    public ulong StartDelay { get; init; }

    /// <summary>Gets the magnitude of the low-frequency rumble, from 0 to 1.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-strongmagnitude"/>
    [IdlMember("strongMagnitude", "https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-strongmagnitude")]
    public double StrongMagnitude { get; init; }

    /// <summary>Gets the magnitude of the high-frequency rumble, from 0 to 1.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-weakmagnitude"/>
    [IdlMember("weakMagnitude", "https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-weakmagnitude")]
    public double WeakMagnitude { get; init; }

    /// <summary>Gets the magnitude of the rumble of the left trigger (<c>trigger-rumble</c>), from 0 to 1.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-lefttrigger"/>
    [IdlMember("leftTrigger", "https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-lefttrigger")]
    public double LeftTrigger { get; init; }

    /// <summary>Gets the magnitude of the rumble of the right trigger (<c>trigger-rumble</c>), from 0 to 1.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-righttrigger"/>
    [IdlMember("rightTrigger", "https://w3c.github.io/gamepad/#dom-gamepadeffectparameters-righttrigger")]
    public double RightTrigger { get; init; }
}

/// <summary>A touch on a touch surface of a gamepad (https://w3c.github.io/gamepad/#dom-gamepadtouch). The library does not support touch surfaces.</summary>
/// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadtouch"/>
[IdlInterface("GamepadTouch", "https://w3c.github.io/gamepad/#dom-gamepadtouch")]
public sealed class GamepadTouch
{
    /// <summary>Gets the identifier of the touch.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadtouch-touchid"/>
    [IdlMember("touchId", "https://w3c.github.io/gamepad/#dom-gamepadtouch-touchid")]
    public uint TouchId { get; init; }

    /// <summary>Gets the identifier of the touch surface.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadtouch-surfaceid"/>
    [IdlMember("surfaceId", "https://w3c.github.io/gamepad/#dom-gamepadtouch-surfaceid")]
    public byte SurfaceId { get; init; }

    /// <summary>Gets the position of the touch, normalized from -1 to 1.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadtouch-position"/>
    [IdlMember("position", "https://w3c.github.io/gamepad/#dom-gamepadtouch-position")]
    public DomPointReadOnly? Position { get; init; }

    /// <summary>Gets the size of the touch surface, or null.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadtouch-surfacedimensions"/>
    [IdlMember("surfaceDimensions", "https://w3c.github.io/gamepad/#dom-gamepadtouch-surfacedimensions")]
    public DomRectReadOnly? SurfaceDimensions { get; init; }
}

/// <summary>The state of a button of a gamepad (https://w3c.github.io/gamepad/#gamepadbutton-interface).</summary>
/// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadbutton"/>
[IdlInterface("GamepadButton", "https://w3c.github.io/gamepad/#dom-gamepadbutton")]
public sealed class GamepadButton
{
    internal GamepadButton()
    {
    }

    /// <summary>Gets whether the button is pressed.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadbutton-pressed"/>
    [IdlMember("pressed", "https://w3c.github.io/gamepad/#dom-gamepadbutton-pressed")]
    public bool Pressed { get; internal set; }

    /// <summary>Gets whether the button is touched.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadbutton-touched"/>
    [IdlMember("touched", "https://w3c.github.io/gamepad/#dom-gamepadbutton-touched")]
    public bool Touched { get; internal set; }

    /// <summary>Gets the value of the button, from 0 (released) to 1 (fully pressed).</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadbutton-value"/>
    [IdlMember("value", "https://w3c.github.io/gamepad/#dom-gamepadbutton-value")]
    public double Value { get; internal set; }
}

/// <summary>
/// A gamepad of a window (https://w3c.github.io/gamepad/#gamepad-interface), for a <see cref="GamepadDevice"/> of the
/// <see cref="GamepadProvider"/> of the browsing context. The objects of the events are updated with the inputs of the
/// device; <see cref="Navigator.GetGamepads"/> returns snapshots.
/// </summary>
/// <seealso href="https://w3c.github.io/gamepad/#dom-gamepad"/>
[IdlInterface("Gamepad", "https://w3c.github.io/gamepad/#dom-gamepad")]
public sealed class Gamepad
{
    private static readonly GamepadTouch[] NoTouches = [];

    internal Gamepad(Window window, GamepadDevice device, int index, double timestamp, GamepadDeviceSnapshot snapshot)
    {
        Device = device;
        Id = device.Id;
        Index = index;
        Mapping = device.Mapping == GamepadMappingType.XrStandard ? GamepadMappingType.None : device.Mapping;
        Connected = true;
        Timestamp = timestamp;
        Axes = snapshot.Axes;
        var buttons = new GamepadButton[snapshot.Buttons.Length];
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i] = CreateButton(snapshot.Buttons[i]);
        }

        Buttons = buttons;
        VibrationActuator = new GamepadHapticActuator(window, device);
    }

    // https://w3c.github.io/gamepad/#dom-navigator-getgamepads: the copy of a gamepad, which shares its actuator
    internal Gamepad(Gamepad gamepad)
    {
        Device = gamepad.Device;
        Id = gamepad.Id;
        Index = gamepad.Index;
        Mapping = gamepad.Mapping;
        Connected = gamepad.Connected;
        Timestamp = gamepad.Timestamp;
        Axes = [.. gamepad.Axes];
        Buttons = [.. gamepad.Buttons.Select(button => new GamepadButton { Pressed = button.Pressed, Touched = button.Touched, Value = button.Value })];
        VibrationActuator = gamepad.VibrationActuator;
    }

    internal GamepadDevice Device { get; }

    /// <summary>Whether the gamepad was exposed to the scripts ([[exposed]]).</summary>
    internal bool Exposed { get; set; }

    /// <summary>Gets the identification string of the gamepad.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepad-id"/>
    [IdlMember("id", "https://w3c.github.io/gamepad/#dom-gamepad-id")]
    public string Id { get; }

    /// <summary>Gets the index of the gamepad in <see cref="Navigator.GetGamepads"/>.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepad-index"/>
    [IdlMember("index", "https://w3c.github.io/gamepad/#dom-gamepad-index")]
    public int Index { get; }

    /// <summary>Gets whether the gamepad is still connected.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepad-connected"/>
    [IdlMember("connected", "https://w3c.github.io/gamepad/#dom-gamepad-connected")]
    public bool Connected { get; internal set; }

    /// <summary>Gets the time of the last update of the buttons and the axes, in milliseconds since the time origin of the window.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepad-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/gamepad/#dom-gamepad-timestamp")]
    public double Timestamp { get; internal set; }

    /// <summary>Gets the mapping of the buttons and the axes.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepad-mapping"/>
    [IdlMember("mapping", "https://w3c.github.io/gamepad/#dom-gamepad-mapping")]
    public GamepadMappingType Mapping { get; }

    /// <summary>Gets the values of the axes, from -1 to 1. The list is replaced when a value changes.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepad-axes"/>
    [IdlMember("axes", "https://w3c.github.io/gamepad/#dom-gamepad-axes")]
    public IReadOnlyList<double> Axes { get; private set; }

    /// <summary>Gets the buttons.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepad-buttons"/>
    [IdlMember("buttons", "https://w3c.github.io/gamepad/#dom-gamepad-buttons")]
    public IReadOnlyList<GamepadButton> Buttons { get; }

    /// <summary>Gets the touches of the touch surfaces: always empty, as the library does not support touch surfaces.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepad-touches"/>
    [IdlMember("touches", "https://w3c.github.io/gamepad/#dom-gamepad-touches")]
    public IReadOnlyList<GamepadTouch> Touches => NoTouches;

    /// <summary>Gets the vibration actuator of the gamepad.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepad-vibrationactuator"/>
    [IdlMember("vibrationActuator", "https://w3c.github.io/gamepad/#dom-gamepad-vibrationactuator")]
    public GamepadHapticActuator VibrationActuator { get; }

    /// <summary>https://w3c.github.io/gamepad/#dfn-update-gamepad-state: the buttons and the axes take the values of the device.</summary>
    internal void UpdateState(GamepadDeviceSnapshot snapshot, double now)
    {
        Timestamp = now;
        if (!Axes.SequenceEqual(snapshot.Axes))
        {
            Axes = snapshot.Axes;
        }

        for (var i = 0; i < Buttons.Count; i++)
        {
            var button = Buttons[i];
            var state = snapshot.Buttons[i];
            button.Pressed = state.Pressed;
            button.Touched = state.Touched;
            button.Value = state.Value;
        }
    }

    private static GamepadButton CreateButton(GamepadButtonState state) => new() { Pressed = state.Pressed, Touched = state.Touched, Value = state.Value };
}

/// <summary>
/// The vibration actuator of a gamepad (https://w3c.github.io/gamepad/#gamepadhapticactuator-interface): it plays the haptic
/// effects of <see cref="GamepadDevice.HapticEffects"/> with <see cref="GamepadProvider.PlayEffect"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadhapticactuator"/>
[IdlInterface("GamepadHapticActuator", "https://w3c.github.io/gamepad/#dom-gamepadhapticactuator")]
public sealed class GamepadHapticActuator
{
    // The user agent may limit the total duration of an effect: 5 seconds, as the specification recommends
    private const ulong MaxEffectDuration = 5000;

    private readonly Window _window;
    private readonly GamepadDevice _device;
    private TaskCompletionSource<GamepadHapticsResult>? _playingEffect;
    private CancellationTokenSource? _playingEffectCancellation;

    internal GamepadHapticActuator(Window window, GamepadDevice device)
    {
        _window = window;
        _device = device;
        Effects = [.. device.HapticEffects.Distinct()];
    }

    /// <summary>Gets the types of haptic effects that the actuator can play.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadhapticactuator-effects"/>
    [IdlMember("effects", "https://w3c.github.io/gamepad/#dom-gamepadhapticactuator-effects")]
    public IReadOnlyList<GamepadHapticEffectType> Effects { get; }

    /// <summary>
    /// Plays a haptic effect (https://w3c.github.io/gamepad/#dom-gamepadhapticactuator-playeffect): the effect that is playing is
    /// stopped (its task completes with <see cref="GamepadHapticsResult.Preempted"/>), then the task completes with
    /// <see cref="GamepadHapticsResult.Complete"/> when the effect completes. The delay plus the duration are limited to 5
    /// seconds. The task fails with a <see cref="TypeErrorException"/> when a magnitude is not between 0 and 1, an
    /// <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully active or is hidden, and a
    /// <c>NotSupportedError</c> when the actuator cannot play effects of the type or the gamepad is disconnected.
    /// </summary>
    /// <param name="type">The type of the effect.</param>
    /// <param name="params">The parameters of the effect.</param>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadhapticactuator-playeffect"/>
    [IdlMember("playEffect", "https://w3c.github.io/gamepad/#dom-gamepadhapticactuator-playeffect")]
    public Task<GamepadHapticsResult> PlayEffect(GamepadHapticEffectType type, GamepadEffectParameters? @params = null)
    {
        @params ??= new GamepadEffectParameters();
        if (!IsValidEffect(type, @params))
            return Task.FromException<GamepadHapticsResult>(new TypeErrorException("The magnitudes of the effect must be between 0 and 1"));

        if (GetDocumentError() is { } error)
            return Task.FromException<GamepadHapticsResult>(error);

        PreemptPlayingEffect();
        if (!Effects.Contains(type) || _device.Provider is not { } provider)
            return Task.FromException<GamepadHapticsResult>(DomException.NotSupported($"The actuator cannot play '{(type == GamepadHapticEffectType.DualRumble ? "dual-rumble" : "trigger-rumble")}' effects"));

        var result = new TaskCompletionSource<GamepadHapticsResult>();
        var cancellation = new CancellationTokenSource();
        _playingEffect = result;
        _playingEffectCancellation = cancellation;

        // The total duration of the effect is limited
        var startDelay = Math.Min(@params.StartDelay, MaxEffectDuration);
        var parameters = new GamepadEffectParameters
        {
            StartDelay = startDelay,
            Duration = Math.Min(@params.Duration, MaxEffectDuration - startDelay),
            StrongMagnitude = @params.StrongMagnitude,
            WeakMagnitude = @params.WeakMagnitude,
            LeftTrigger = @params.LeftTrigger,
            RightTrigger = @params.RightTrigger,
        };

        Task effect;
        try
        {
            effect = provider.PlayEffect(new GamepadHapticEffectRequest(CallbackContext.For(_window), _device, type, parameters, _window.EventLoop.TimeProvider, cancellation.Token));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            effect = Task.FromException(exception);
        }

        // When the effect completes, a task resolves the promise, unless the effect was stopped
        _ = effect.ContinueWith(
            _ => _window.QueueGlobalTask(() =>
            {
                if (ReferenceEquals(_playingEffect, result))
                {
                    _playingEffect = null;
                    _playingEffectCancellation = null;
                    result.TrySetResult(GamepadHapticsResult.Complete);
                }

                cancellation.Dispose();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    /// <summary>
    /// Stops the effect that is playing (https://w3c.github.io/gamepad/#dom-gamepadhapticactuator-reset): its task completes
    /// with <see cref="GamepadHapticsResult.Preempted"/>. The task completes with <see cref="GamepadHapticsResult.Complete"/>,
    /// and fails with an <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully active or is hidden.
    /// </summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadhapticactuator-reset"/>
    [IdlMember("reset", "https://w3c.github.io/gamepad/#dom-gamepadhapticactuator-reset")]
    public Task<GamepadHapticsResult> Reset()
    {
        if (GetDocumentError() is { } error)
            return Task.FromException<GamepadHapticsResult>(error);

        PreemptPlayingEffect();
        return Task.FromResult(GamepadHapticsResult.Complete);
    }

    /// <summary>
    /// Stops the effect that is playing: the document is hidden (https://w3c.github.io/gamepad/#handling-visibility-change), or
    /// the gamepad is disconnected.
    /// </summary>
    internal void StopPlayingEffect() => PreemptPlayingEffect();

    /// <summary>Stops the effect that is playing, whose promise a task resolves with <c>preempted</c>.</summary>
    private void PreemptPlayingEffect()
    {
        if (_playingEffect is not { } effect)
            return;

        var cancellation = _playingEffectCancellation;
        _playingEffect = null;
        _playingEffectCancellation = null;
        cancellation?.Cancel();
        _window.QueueGlobalTask(() => effect.TrySetResult(GamepadHapticsResult.Preempted));
    }

    private DomException? GetDocumentError()
    {
        var document = (GlobalScope.Current as Window ?? _window).Document;
        return !document.IsFullyActive || document.Hidden ? DomException.InvalidState("The document is not fully active or is hidden") : null;
    }

    // https://w3c.github.io/gamepad/#dfn-valid-effect: the magnitudes of the effect are between 0 and 1
    private static bool IsValidEffect(GamepadHapticEffectType type, GamepadEffectParameters parameters)
    {
        static bool IsMagnitude(double value) => value is >= 0 and <= 1;

        if (!IsMagnitude(parameters.StrongMagnitude) || !IsMagnitude(parameters.WeakMagnitude))
            return false;

        return type != GamepadHapticEffectType.TriggerRumble || (IsMagnitude(parameters.LeftTrigger) && IsMagnitude(parameters.RightTrigger));
    }
}

/// <summary>The initialization dictionary of a <see cref="GamepadEvent"/> (https://w3c.github.io/gamepad/#dom-gamepadeventinit).</summary>
/// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadeventinit"/>
[IdlInterface("GamepadEventInit", "https://w3c.github.io/gamepad/#dom-gamepadeventinit")]
public sealed class GamepadEventInit : EventInit
{
    /// <summary>Gets the gamepad of the event, or null.</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadeventinit-gamepad"/>
    [IdlMember("gamepad", "https://w3c.github.io/gamepad/#dom-gamepadeventinit-gamepad")]
    public Gamepad? Gamepad { get; init; }
}

/// <summary>The event fired at a window when a gamepad is connected or disconnected (<c>gamepadconnected</c>, <c>gamepaddisconnected</c>, https://w3c.github.io/gamepad/#gamepadevent-interface).</summary>
/// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadevent"/>
[IdlInterface("GamepadEvent", "https://w3c.github.io/gamepad/#dom-gamepadevent")]
public sealed class GamepadEvent : Event
{
    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/gamepad/#dom-gamepadevent-constructor")]
    public GamepadEvent(string type, GamepadEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Gamepad = eventInitDict?.Gamepad;
    }

    internal GamepadEvent(string type, Gamepad gamepad)
        : base(type)
    {
        Gamepad = gamepad;
    }

    /// <summary>Gets the gamepad that was connected or disconnected (null for an event created without gamepad).</summary>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-gamepadevent-gamepad"/>
    [IdlMember("gamepad", "https://w3c.github.io/gamepad/#dom-gamepadevent-gamepad")]
    public Gamepad? Gamepad { get; }
}

// https://w3c.github.io/gamepad/#extensions-to-the-navigator-interface
public sealed partial class Navigator
{
    /// <summary>
    /// Returns the gamepads of the window (https://w3c.github.io/gamepad/#dom-navigator-getgamepads): a snapshot of each
    /// connected gamepad at its index, and null at the indexes of the disconnected ones. The list is empty until the user
    /// pressed a button or moved an axis of a gamepad while the document was visible, and for a document that is not fully
    /// active.
    /// </summary>
    /// <exception cref="DomException">A <c>SecurityError</c>: the document is not allowed to use the <c>gamepad</c> feature.</exception>
    /// <seealso href="https://w3c.github.io/gamepad/#dom-navigator-getgamepads"/>
    [IdlMember("getGamepads", "https://w3c.github.io/gamepad/#dom-navigator-getgamepads")]
    public IReadOnlyList<Gamepad?> GetGamepads() => _window.Gamepads.GetGamepads();
}

public sealed partial class Window
{
    private WindowGamepads? _gamepads;

    /// <summary>The gamepads of the window.</summary>
    internal WindowGamepads Gamepads => _gamepads ??= new WindowGamepads(this);

    internal WindowGamepads? GamepadsOrNull => _gamepads;

    /// <summary>A listener of the gamepad events starts the monitoring of the gamepads, as in Chromium.</summary>
    private void OnGamepadListenerAdded(EventListenerEntry listener)
    {
        if (listener.Type is "gamepadconnected" or "gamepaddisconnected")
        {
            Gamepads.EnsureRegistered();
        }
    }
}

/// <summary>
/// The gamepads of a window (the [[gamepads]] and [[hasGamepadGesture]] slots of its navigator): the steps of the
/// connections, the inputs and the disconnections of the devices of the <see cref="GamepadProvider"/>, which run in tasks.
/// </summary>
internal sealed class WindowGamepads(Window window)
{
    private readonly List<Gamepad?> _gamepads = [];
    private readonly Dictionary<GamepadDevice, Gamepad> _byDevice = [];
    private bool _registered;
    private bool _hasGamepadGesture;

    /// <summary>Registers the window to the provider of the browsing context, once: the devices already connected are connected to the window.</summary>
    public void EnsureRegistered()
    {
        if (_registered)
            return;

        _registered = true;
        if (window.Options.Gamepads?.Provider is not { } provider)
            return;

        foreach (var device in provider.Register(this))
        {
            OnConnected(device);
        }

        // https://wicg.github.io/nav-speculation/prerendering.html#patch-gamepads: the activated document gets a
        // gamepadconnected event for each connected gamepad
        if (window.Document.Prerendering)
        {
            window.Document.RunAfterPrerendering(() =>
            {
                foreach (var gamepad in _gamepads)
                {
                    if (gamepad is not null && PermissionsPolicyAlgorithms.IsAllowedToUse(window.Document, PolicyControlledFeature.Gamepad))
                    {
                        gamepad.Exposed = true;
                        window.FireEvent(new GamepadEvent("gamepadconnected", gamepad));
                    }
                }
            });
        }
    }

    /// <summary>https://w3c.github.io/gamepad/#dom-navigator-getgamepads</summary>
    public IReadOnlyList<Gamepad?> GetGamepads()
    {
        var document = window.Document;
        if (!document.IsFullyActive)
            return [];

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Gamepad))
            throw new DomException("The document is not allowed to use the gamepad feature", "SecurityError");

        // https://wicg.github.io/nav-speculation/prerendering.html#patch-gamepads
        EnsureRegistered();
        if (!_hasGamepadGesture || document.Prerendering)
            return [];

        var gamepads = new Gamepad?[_gamepads.Count];
        for (var i = 0; i < gamepads.Length; i++)
        {
            gamepads[i] = _gamepads[i] is { Exposed: true } gamepad ? new Gamepad(gamepad) : null;
        }

        return gamepads;
    }

    /// <summary>https://w3c.github.io/gamepad/#the-gamepadconnected-event. Called from any thread.</summary>
    public void OnConnected(GamepadDevice device) => window.QueueGlobalTask(() =>
    {
        var document = window.Document;
        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.Gamepad) || _byDevice.ContainsKey(device))
            return;

        var gamepad = new Gamepad(window, device, SelectUnusedIndex(), window.GetCurrentHighResolutionTime(), device.GetSnapshot());
        _gamepads[gamepad.Index] = gamepad;
        _byDevice[device] = gamepad;
        if (_hasGamepadGesture && !document.Prerendering)
        {
            gamepad.Exposed = true;
            if (document.IsFullyActive)
            {
                window.FireEvent(new GamepadEvent("gamepadconnected", gamepad));
            }
        }
    });

    /// <summary>https://w3c.github.io/gamepad/#receiving-inputs. Called from any thread.</summary>
    public void OnInput(GamepadDevice device) => window.QueueGlobalTask(() =>
    {
        if (!_byDevice.TryGetValue(device, out var gamepad))
            return;

        // https://w3c.github.io/gamepad/#dfn-update-gamepad-state
        var now = window.GetCurrentHighResolutionTime();
        var snapshot = device.GetSnapshot();
        gamepad.UpdateState(snapshot, now);

        // The first gesture of the user, in a visible document, exposes the gamepads (the user attention of a top-level
        // traversable is its visibility: the library has no system focus)
        var document = window.Document;
        if (_hasGamepadGesture || !document.IsFullyActive || document.Hidden || document.Prerendering || !snapshot.ContainsUserGesture)
            return;

        _hasGamepadGesture = true;
        foreach (var connectedGamepad in _gamepads)
        {
            if (connectedGamepad is null)
                continue;

            connectedGamepad.Exposed = true;
            connectedGamepad.Timestamp = now;
            window.QueueGlobalTask(() =>
            {
                if (window.Document.IsFullyActive)
                {
                    window.FireEvent(new GamepadEvent("gamepadconnected", connectedGamepad));
                }
            });
        }
    });

    /// <summary>https://w3c.github.io/gamepad/#the-gamepaddisconnected-event. Called from any thread.</summary>
    public void OnDisconnected(GamepadDevice device) => window.QueueGlobalTask(() =>
    {
        if (!_byDevice.Remove(device, out var gamepad))
            return;

        gamepad.Connected = false;
        gamepad.VibrationActuator.StopPlayingEffect();
        if (gamepad.Exposed && window.Document.IsFullyActive && !window.Document.Prerendering)
        {
            window.FireEvent(new GamepadEvent("gamepaddisconnected", gamepad));
        }

        _gamepads[gamepad.Index] = null;
        while (_gamepads.Count > 0 && _gamepads[^1] is null)
        {
            _gamepads.RemoveAt(_gamepads.Count - 1);
        }
    });

    /// <summary>https://w3c.github.io/gamepad/#handling-visibility-change: the haptic effects of a hidden document are stopped.</summary>
    public void OnDocumentHidden()
    {
        foreach (var gamepad in _byDevice.Values)
        {
            gamepad.VibrationActuator.StopPlayingEffect();
        }
    }

    /// <summary>https://w3c.github.io/gamepad/#dfn-selecting-an-unused-gamepad-index</summary>
    private int SelectUnusedIndex()
    {
        var index = _gamepads.IndexOf(null);
        if (index >= 0)
            return index;

        _gamepads.Add(null);
        return _gamepads.Count - 1;
    }
}
