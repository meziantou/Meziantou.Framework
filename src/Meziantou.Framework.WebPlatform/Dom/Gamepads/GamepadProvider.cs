namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Gamepad API (https://w3c.github.io/gamepad/, <see cref="BrowsingContextOptions.Gamepads"/>).</summary>
public sealed class GamepadOptions
{
    /// <summary>
    /// Gets or sets the gamepads of the machine: a <see cref="GamepadProvider"/> to which the application connects the
    /// <see cref="GamepadDevice"/> objects whose inputs it updates. A provider can be shared by several browsing contexts. The
    /// default is null: no gamepad is connected.
    /// </summary>
    public GamepadProvider? Provider { get; init; }
}

/// <summary>
/// The gamepads connected to the machine, read by the windows of the browsing contexts that share a
/// <see cref="BrowsingContextOptions"/> (<see cref="GamepadOptions.Provider"/>). <see cref="Connect"/> and
/// <see cref="Disconnect"/> run the steps of the <c>gamepadconnected</c> and <c>gamepaddisconnected</c> events, and the
/// inputs of a device (<see cref="GamepadDevice.SetButton"/>, <see cref="GamepadDevice.SetAxis"/>) update the
/// <c>Gamepad</c> objects of the windows. Override <see cref="PlayEffect"/> to play the haptic effects on the device.
/// </summary>
/// <remarks>
/// A window sees the gamepads once it uses the API (<c>navigator.getGamepads()</c>, or a listener of the gamepad events),
/// and <c>getGamepads()</c> returns them once the user pressed a button or moved an axis while the window was visible. A
/// provider can be shared by several browsing contexts, and used from any thread.
/// </remarks>
public class GamepadProvider
{
    private readonly Lock _lock = new();
    private readonly List<GamepadDevice> _devices = [];
    private readonly List<WeakReference<WindowGamepads>> _windows = [];

    /// <summary>Gets the connected gamepads, in the order they were connected.</summary>
    public IReadOnlyList<GamepadDevice> Devices
    {
        get
        {
            lock (_lock)
            {
                return [.. _devices];
            }
        }
    }

    /// <summary>
    /// Connects a gamepad (https://w3c.github.io/gamepad/#the-gamepadconnected-event): the windows that use the API get a
    /// <c>Gamepad</c> for it, and a <c>gamepadconnected</c> event once the user interacted with a gamepad. Can be called from
    /// any thread.
    /// </summary>
    /// <param name="device">The gamepad. A device can be connected to one provider at a time.</param>
    /// <exception cref="InvalidOperationException">The device is connected to a provider.</exception>
    public void Connect(GamepadDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.Attach(this);
        lock (_lock)
        {
            _devices.Add(device);
        }

        foreach (var window in GetWindows())
        {
            window.OnConnected(device);
        }
    }

    /// <summary>
    /// Disconnects a gamepad (https://w3c.github.io/gamepad/#the-gamepaddisconnected-event): its <c>Gamepad</c> objects are not
    /// connected anymore, and the windows that exposed it get a <c>gamepaddisconnected</c> event. Can be called from any thread.
    /// </summary>
    /// <param name="device">The gamepad.</param>
    /// <returns>Whether the device was connected to this provider.</returns>
    public bool Disconnect(GamepadDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_lock)
        {
            if (!_devices.Remove(device))
                return false;
        }

        device.Detach();
        foreach (var window in GetWindows())
        {
            window.OnDisconnected(device);
        }

        return true;
    }

    /// <summary>
    /// Plays a haptic effect on a gamepad (https://w3c.github.io/gamepad/#dfn-issue-a-haptic-effect), for
    /// <c>vibrationActuator.playEffect()</c>: the type is one of <see cref="GamepadDevice.HapticEffects"/>, and the parameters
    /// are valid. The task completes when the effect completes; <paramref name="request"/> is canceled when the effect is
    /// stopped (<c>reset()</c>, another effect, or a hidden document). The default implementation waits for the delay and
    /// the duration of the effect with the clock of the window.
    /// </summary>
    /// <param name="request">The effect, its gamepad and the clock of the window.</param>
    public virtual Task PlayEffect(GamepadHapticEffectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parameters = request.Parameters;
        var duration = TimeSpan.FromMilliseconds((double)parameters.StartDelay + parameters.Duration);
        return Task.Delay(duration, request.TimeProvider, request.CancellationToken);
    }

    /// <summary>Registers the gamepads of a window, which then gets the connections and the inputs of the devices; returns the connected devices.</summary>
    internal GamepadDevice[] Register(WindowGamepads window)
    {
        lock (_lock)
        {
            _windows.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _windows.Add(new WeakReference<WindowGamepads>(window));
            return [.. _devices];
        }
    }

    /// <summary>The inputs of a device changed: the windows update their <c>Gamepad</c> objects.</summary>
    internal void OnInput(GamepadDevice device)
    {
        foreach (var window in GetWindows())
        {
            window.OnInput(device);
        }
    }

    private List<WindowGamepads> GetWindows()
    {
        lock (_lock)
        {
            var result = new List<WindowGamepads>(_windows.Count);
            foreach (var reference in _windows)
            {
                if (reference.TryGetTarget(out var window))
                {
                    result.Add(window);
                }
            }

            return result;
        }
    }
}

/// <summary>
/// A gamepad of a <see cref="GamepadProvider"/>: its identification string, its mapping, and the normalized values of its
/// buttons and axes, which the application updates with <see cref="SetButton"/> and <see cref="SetAxis"/> (the values of a
/// device in the order of the <c>buttons</c> and <c>axes</c> of the <c>Gamepad</c> objects: the application maps the inputs
/// of the hardware). The methods can be called from any thread.
/// </summary>
public sealed class GamepadDevice
{
    /// <summary>The value above which an analog button is pressed, and the displacement of an axis that is an interaction of the user.</summary>
    private const double Threshold = 0.5;

    private readonly Lock _lock = new();
    private readonly double[] _axes;
    private readonly GamepadButtonState[] _buttons;

    // Whether each button reported a released state, and each axis a neutral value, since the device was created: a press or
    // a displacement of these is a gamepad user gesture (https://w3c.github.io/gamepad/#dfn-contains-a-gamepad-user-gesture)
    private readonly bool[] _buttonsReleased;
    private readonly bool[] _axesNeutral;
    private GamepadProvider? _provider;

    /// <summary>Creates a gamepad whose buttons are released and whose axes are centered.</summary>
    /// <param name="id">
    /// The identification string of the gamepad (<c>Gamepad.id</c>), e.g. its vendor and product identifiers. It must not
    /// identify the device uniquely (no serial number).
    /// </param>
    /// <param name="buttonCount">The number of buttons (17 for the standard gamepad).</param>
    /// <param name="axisCount">The number of axes (4 for the standard gamepad).</param>
    public GamepadDevice(string id, int buttonCount, int axisCount)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentOutOfRangeException.ThrowIfNegative(buttonCount);
        ArgumentOutOfRangeException.ThrowIfNegative(axisCount);
        Id = id;
        _axes = new double[axisCount];
        _buttons = new GamepadButtonState[buttonCount];
        _buttonsReleased = new bool[buttonCount];
        _axesNeutral = new bool[axisCount];
        Array.Fill(_buttonsReleased, value: true);
        Array.Fill(_axesNeutral, value: true);
    }

    /// <summary>Gets the identification string of the gamepad.</summary>
    public string Id { get; }

    /// <summary>
    /// Gets the mapping of the buttons and the axes: <see cref="GamepadMappingType.Standard"/> when they are in the order of
    /// the standard gamepad (https://w3c.github.io/gamepad/#remapping), else <see cref="GamepadMappingType.None"/> (the
    /// default). <see cref="GamepadMappingType.XrStandard"/> is reported as <see cref="GamepadMappingType.None"/>.
    /// </summary>
    public GamepadMappingType Mapping { get; init; }

    /// <summary>Gets the haptic effects that the vibration actuator of the gamepad can play (none by default): see <see cref="GamepadProvider.PlayEffect"/>.</summary>
    public IReadOnlyList<GamepadHapticEffectType> HapticEffects { get; init; } = [];

    /// <summary>Gets the number of buttons.</summary>
    public int ButtonCount => _buttons.Length;

    /// <summary>Gets the number of axes.</summary>
    public int AxisCount => _axes.Length;

    /// <summary>
    /// Sets the state of a button, and updates the <c>Gamepad</c> objects of the windows.
    /// </summary>
    /// <param name="index">The index of the button.</param>
    /// <param name="value">The value of the button, from 0 (released) to 1 (fully pressed).</param>
    /// <param name="pressed">Whether the button is pressed; by default, whether <paramref name="value"/> is above 0.5.</param>
    /// <param name="touched">Whether the button is touched; by default, whether it is pressed or its value is above 0.</param>
    public void SetButton(int index, double value, bool? pressed = null, bool? touched = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _buttons.Length);
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a finite number");

        value = Math.Clamp(value, 0, 1);
        var isPressed = pressed ?? value > Threshold;
        lock (_lock)
        {
            _buttons[index] = new GamepadButtonState(isPressed, touched ?? (isPressed || value > 0), value);
            if (!isPressed)
            {
                _buttonsReleased[index] = true;
            }
        }

        _provider?.OnInput(this);
    }

    /// <summary>Sets the value of an axis, and updates the <c>Gamepad</c> objects of the windows.</summary>
    /// <param name="index">The index of the axis.</param>
    /// <param name="value">The value of the axis, from -1 to 1 (0 is the neutral position).</param>
    public void SetAxis(int index, double value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _axes.Length);
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a finite number");

        value = Math.Clamp(value, -1, 1);
        lock (_lock)
        {
            _axes[index] = value;
            if (Math.Abs(value) <= Threshold)
            {
                _axesNeutral[index] = true;
            }
        }

        _provider?.OnInput(this);
    }

    internal void Attach(GamepadProvider provider)
    {
        lock (_lock)
        {
            if (_provider is not null)
                throw new InvalidOperationException("The gamepad is already connected");

            _provider = provider;
        }
    }

    internal void Detach()
    {
        lock (_lock)
        {
            _provider = null;
        }
    }

    internal GamepadProvider? Provider
    {
        get
        {
            lock (_lock)
            {
                return _provider;
            }
        }
    }

    /// <summary>Returns the current values of the buttons and the axes, and whether they contain a gamepad user gesture.</summary>
    internal GamepadDeviceSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            var gesture = false;
            for (var i = 0; i < _buttons.Length && !gesture; i++)
            {
                gesture = _buttons[i].Pressed && _buttonsReleased[i];
            }

            for (var i = 0; i < _axes.Length && !gesture; i++)
            {
                gesture = Math.Abs(_axes[i]) > Threshold && _axesNeutral[i];
            }

            return new GamepadDeviceSnapshot([.. _buttons], [.. _axes], gesture);
        }
    }
}

/// <summary>A haptic effect to play on a gamepad, given to <see cref="GamepadProvider.PlayEffect"/>.</summary>
public sealed class GamepadHapticEffectRequest
{
    internal GamepadHapticEffectRequest(CallbackContext context, GamepadDevice device, GamepadHapticEffectType type, GamepadEffectParameters parameters, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        Context = context;
        Device = device;
        Type = type;
        Parameters = parameters;
        TimeProvider = timeProvider;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the context of the window that plays the effect.</summary>
    public CallbackContext Context { get; }

    /// <summary>Gets the gamepad.</summary>
    public GamepadDevice Device { get; }

    /// <summary>Gets the type of the effect.</summary>
    public GamepadHapticEffectType Type { get; }

    /// <summary>Gets the parameters of the effect: the magnitudes are between 0 and 1, and the delay plus the duration are at most 5 seconds.</summary>
    public GamepadEffectParameters Parameters { get; }

    /// <summary>Gets the clock of the event loop of the window (<see cref="BrowsingContextOptions.TimeProvider"/>).</summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>Gets the token canceled when the effect is stopped.</summary>
    public CancellationToken CancellationToken { get; }
}

internal readonly record struct GamepadButtonState(bool Pressed, bool Touched, double Value);

internal sealed record GamepadDeviceSnapshot(GamepadButtonState[] Buttons, double[] Axes, bool ContainsUserGesture);
